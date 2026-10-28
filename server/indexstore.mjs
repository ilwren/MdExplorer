// Local search index store (concept §4.1 搜索与索引中心).
// Purely local JSON index under data/ — never suggests cloud or network services.
import fsp from 'node:fs/promises';
import fsSync from 'node:fs';
import path from 'node:path';
import { ROOTS, DATA_DIR, safePath, apiError, classify, extOf } from './fs.mjs';

const INDEX_FILE = path.join(DATA_DIR, 'index.json');
const INDEX_BUDGET_MS = 15000;

function defaultState() {
  return {
    roots: [...ROOTS],
    paused: false,
    contentEnabled: true,
    updatedAt: 0,
    files: [], // { path, name, size, mtime, kind, ext }
  };
}

export function loadIndex() {
  try {
    const raw = fsSync.readFileSync(INDEX_FILE, 'utf8');
    const s = JSON.parse(raw);
    if (!s || !Array.isArray(s.files)) return defaultState();
    return { ...defaultState(), ...s };
  } catch {
    return defaultState();
  }
}

function saveIndex(s) {
  fsSync.mkdirSync(DATA_DIR, { recursive: true });
  fsSync.writeFileSync(INDEX_FILE, JSON.stringify(s));
}

const SKIP = new Set(['node_modules', '.git', 'dist', 'build', '__pycache__', '.venv', '.cache', 'data']);

/** Rebuild the index by scanning every index root (budgeted). */
export async function rebuildIndex(budgetMs = INDEX_BUDGET_MS) {
  const state = loadIndex();
  if (state.paused) throw apiError(409, '索引维护已暂停');
  const deadline = Date.now() + budgetMs;
  const files = [];
  let truncated = false;

  async function walk(d) {
    if (Date.now() > deadline) { truncated = true; return; }
    let names;
    try { names = await fsp.readdir(d); } catch { return; }
    for (const name of names) {
      if (Date.now() > deadline) { truncated = true; return; }
      if (name.startsWith('.') || SKIP.has(name)) continue;
      const full = path.join(d, name);
      const st = await fsp.stat(full).catch(() => null);
      if (!st) continue;
      if (st.isDirectory()) {
        await walk(full);
      } else if (st.isFile()) {
        files.push({
          path: full, name, size: st.size, mtime: st.mtimeMs,
          kind: classify(name, false), ext: extOf(name),
        });
      }
    }
  }

  for (const r of state.roots) await walk(r);
  state.files = files;
  state.updatedAt = Date.now();
  saveIndex(state);
  return { ...statusOf(state), truncated };
}

/** Sampled staleness: entries whose size/mtime no longer match the file system. */
async function countStale(files, sample = 40) {
  const step = Math.max(1, Math.floor(files.length / sample));
  let stale = 0;
  for (let i = 0; i < files.length; i += step) {
    const f = files[i];
    const st = await fsp.stat(f.path).catch(() => null);
    if (!st || Math.abs(st.mtimeMs - f.mtime) > 1000 || st.size !== f.size) stale += 1;
  }
  return stale;
}

function statusOf(s) {
  let bytes = 0;
  try { bytes = fsSync.statSync(INDEX_FILE).size; } catch { /* no index yet */ }
  return {
    roots: s.roots,
    paused: s.paused,
    contentEnabled: s.contentEnabled,
    updatedAt: s.updatedAt,
    fileCount: s.files.length,
    sizeBytes: bytes,
  };
}

export async function indexStatus() {
  const s = loadIndex();
  const staleCount = await countStale(s.files);
  return { ...statusOf(s), staleCount };
}

export function setIndexSettings(patch) {
  const s = loadIndex();
  if (typeof patch.paused === 'boolean') s.paused = patch.paused;
  if (typeof patch.contentEnabled === 'boolean') s.contentEnabled = patch.contentEnabled;
  saveIndex(s);
  return statusOf(s);
}

export function clearIndex() {
  const s = loadIndex();
  s.files = [];
  s.updatedAt = 0;
  saveIndex(s);
  return statusOf(s);
}

export function addIndexRoot(dir) {
  const abs = safePath(dir);
  const s = loadIndex();
  if (!s.roots.includes(abs)) s.roots.push(abs);
  saveIndex(s);
  return statusOf(s);
}

export function removeIndexRoot(dir) {
  const s = loadIndex();
  if (s.roots.length <= 1) throw apiError(400, '至少保留一个索引根目录');
  s.roots = s.roots.filter((r) => r !== dir);
  s.files = s.files.filter((f) => f.path !== dir && !f.path.startsWith(dir + path.sep));
  saveIndex(s);
  return statusOf(s);
}

/**
 * Search the index. Name matching always works; content matching runs when
 * `content` is requested AND the content index switch is on (budgeted).
 */
export async function searchIndex(q, { kind = '', root = '', since = 0, content = false, limit = 200 } = {}) {
  const s = loadIndex();
  const query = String(q || '').trim().toLowerCase();
  const wantContent = !!content && s.contentEnabled;
  let hits = s.files.filter((f) => {
    if (root && f.path !== root && !f.path.startsWith(root + path.sep)) return false;
    if (kind && f.kind !== kind) return false;
    if (since && f.mtime < since) return false;
    return !query || f.name.toLowerCase().includes(query);
  });
  hits = hits.slice(0, limit);

  if (wantContent && query) {
    // content pass: scan matching text-ish files under roots for the phrase
    const deadline = Date.now() + 4000;
    const contentHits = [];
    for (const f of s.files) {
      if (Date.now() > deadline) break;
      if (contentHits.length + hits.length >= limit) break;
      if (kind && f.kind !== kind) continue;
      if (root && f.path !== root && !f.path.startsWith(root + path.sep)) continue;
      if (since && f.mtime < since) continue;
      if (hits.some((h) => h.path === f.path)) continue;
      if (!TEXT_KINDS.has(f.kind)) continue;
      if (f.size > 1024 * 1024) continue;
      const text = await fsp.readFile(f.path, 'utf8').catch(() => '');
      const idx = text.toLowerCase().indexOf(query);
      if (idx >= 0) {
        const start = Math.max(0, idx - 30);
        contentHits.push({ ...f, snippet: text.slice(start, idx + query.length + 50).replace(/\s+/g, ' ') });
      }
    }
    hits = [...hits, ...contentHits].slice(0, limit);
  }
  return {
    results: hits,
    contentUsed: wantContent,
    indexedAt: s.updatedAt,
    paused: s.paused,
    contentEnabled: s.contentEnabled,
  };
}

const TEXT_KINDS = new Set(['markdown', 'text', 'code', 'doc']);
