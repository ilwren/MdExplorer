// Filesystem helpers: safe path resolution, listing, kind classification.
import fsp from 'node:fs/promises';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

export const ROOTS = (process.env.EIDORIA_ROOTS || '/home/user')
  .split(':').map((p) => path.resolve(p)).filter(Boolean);

export const DATA_DIR = process.env.EIDORIA_DATA || path.join(process.cwd(), 'data');
export const TRASH_DIR = path.join(DATA_DIR, 'trash');

/** Resolve a user-supplied path and ensure it stays inside an allowed root. */
export function safePath(p) {
  if (!p) throw apiError(400, 'missing path');
  const resolved = path.resolve(p);
  const ok = ROOTS.some((r) => resolved === r || resolved.startsWith(r + path.sep));
  if (!ok) throw apiError(403, `path outside allowed roots`);
  return resolved;
}

export function apiError(status, message) {
  const e = new Error(message);
  e.status = status;
  return e;
}

const IMAGE = new Set(['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'ico', 'avif']);
const VIDEO = new Set(['mp4', 'mkv', 'avi', 'mov', 'webm', 'm4v', 'flv', 'wmv']);
const AUDIO = new Set(['mp3', 'wav', 'flac', 'ogg', 'm4a', 'aac', 'opus', 'wma']);
const ARCHIVE = new Set(['zip', '7z', 'rar', 'tar', 'gz', 'bz2', 'xz', 'tgz', 'zst']);
const CODE = new Set([
  'js', 'mjs', 'cjs', 'ts', 'tsx', 'jsx', 'py', 'cs', 'java', 'go', 'rs', 'rb', 'sh', 'bash',
  'zsh', 'fish', 'ps1', 'bat', 'cmd', 'json', 'toml', 'yaml', 'yml', 'xml', 'html', 'htm', 'css',
  'scss', 'less', 'sql', 'lua', 'c', 'cc', 'cpp', 'h', 'hpp', 'swift', 'kt', 'dart', 'vue', 'svelte',
]);
const TEXT = new Set(['txt', 'log', 'csv', 'tsv', 'ini', 'conf', 'cfg', 'env', 'gitignore', 'mdc', 'rst']);
const DOC = new Set(['doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'odt', 'ods', 'odp', 'rtf', 'epub']);
const FONT = new Set(['ttf', 'otf', 'woff', 'woff2', 'eot']);
const APPPKG = new Set(['exe', 'msi', 'apk', 'deb', 'rpm', 'appimage', 'dmg', 'pkg', 'jar', 'dll', 'so']);

export function classify(name, isDir) {
  if (isDir) return 'folder';
  const ext = path.extname(name).slice(1).toLowerCase();
  const base = name.toLowerCase();
  if (IMAGE.has(ext)) return 'image';
  if (VIDEO.has(ext)) return 'video';
  if (AUDIO.has(ext)) return 'audio';
  if (ext === 'pdf') return 'pdf';
  if (ARCHIVE.has(ext) || (ext === 'gz' && base.endsWith('.tar.gz'))) return 'archive';
  if (ext === 'md' || ext === 'markdown' || ext === 'mdown') return 'markdown';
  if (CODE.has(ext)) return 'code';
  if (TEXT.has(ext) || ext === '') {
    if (base === 'readme' || base.startsWith('license') || base.startsWith('copying')) return 'markdown';
    return 'text';
  }
  if (DOC.has(ext)) return 'doc';
  if (FONT.has(ext)) return 'font';
  if (APPPKG.has(ext)) return 'package';
  return 'other';
}

export function extOf(name) {
  return path.extname(name).slice(1).toLowerCase();
}

export async function entryOf(full, { countChildren = false } = {}) {
  const st = await fsp.stat(full).catch(() => null);
  if (!st) return null;
  const name = path.basename(full);
  let children;
  if (st.isDirectory() && countChildren) {
    children = await fsp.readdir(full).then((n) => n.length).catch(() => undefined);
  }
  return {
    name,
    path: full,
    isDir: st.isDirectory(),
    size: st.isDirectory() ? 0 : st.size,
    mtime: st.mtimeMs,
    kind: classify(name, st.isDirectory()),
    ext: st.isDirectory() ? '' : extOf(name),
    children,
  };
}

export async function listDir(dir) {
  const abs = safePath(dir);
  const st = await fsp.stat(abs).catch(() => null);
  if (!st) throw apiError(404, 'not found');
  if (!st.isDirectory()) throw apiError(400, 'not a directory');
  const names = await fsp.readdir(abs);
  const entries = [];
  for (const name of names) {
    const e = await entryOf(path.join(abs, name), { countChildren: true });
    if (e) entries.push(e);
  }
  return { path: abs, name: path.basename(abs) || abs, parent: path.dirname(abs), entries };
}

export async function rootsInfo() {
  const out = [];
  for (const r of ROOTS) {
    let total = 0, free = 0;
    try {
      const st = await fsp.statfs(r);
      total = Number(st.blocks) * Number(st.bsize);
      free = Number(st.bavail) * Number(st.bsize);
    } catch { /* ignore */ }
    out.push({ path: r, name: path.basename(r) || r, total, free, used: total - free });
  }
  return out;
}

const MIME = {
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
  bmp: 'image/bmp', svg: 'image/svg+xml', ico: 'image/x-icon', avif: 'image/avif',
  pdf: 'application/pdf', mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
  mp3: 'audio/mpeg', wav: 'audio/wav', flac: 'audio/flac', ogg: 'audio/ogg', m4a: 'audio/mp4',
  txt: 'text/plain; charset=utf-8', md: 'text/plain; charset=utf-8', json: 'text/plain; charset=utf-8',
  html: 'text/html; charset=utf-8', css: 'text/plain; charset=utf-8', js: 'text/plain; charset=utf-8',
};

export function mimeOf(name) {
  const ext = extOf(name);
  return MIME[ext] || 'application/octet-stream';
}

export const TEXT_PREVIEW_MAX = 256 * 1024;

export async function readText(full) {
  const st = await fsp.stat(full);
  if (!st.isFile()) throw apiError(400, 'not a file');
  const fh = await fsp.open(full, 'r');
  try {
    const size = Math.min(st.size, TEXT_PREVIEW_MAX);
    const buf = Buffer.alloc(size);
    await fh.read(buf, 0, size, 0);
    return {
      text: buf.toString('utf8'),
      truncated: st.size > TEXT_PREVIEW_MAX,
      size: st.size,
    };
  } finally {
    await fh.close();
  }
}

const SKIP_DIRS = new Set([
  'node_modules', '.git', 'dist', 'build', 'out', 'target', '__pycache__', '.venv', 'venv',
  '.cache', '.npm', '.local', '.mypy_cache', '.ruff_cache', '.pytest_cache', '.tox', '.next',
  '.output', '.parcel-cache', '.svelte-kit', '.turbo', '.nox', '.nuxt', '.venv',
]);

export async function searchEntries(dir, query, { recursive = true, kind = '', limit = 400, budgetMs = 5000 } = {}) {
  const abs = safePath(dir);
  const q = query.trim().toLowerCase();
  const results = [];
  const qKind = kind && kind !== 'all' ? kind : '';
  const deadline = Date.now() + budgetMs;

  async function walk(d, depth) {
    if (results.length >= limit || Date.now() > deadline) return;
    let names;
    try { names = await fsp.readdir(d); } catch { return; }
    for (const name of names) {
      if (results.length >= limit || Date.now() > deadline) return;
      if (name.startsWith('.') && !q.startsWith('.')) continue;
      const full = path.join(d, name);
      const e = await entryOf(full);
      if (!e) continue;
      const kindOk = !qKind || e.kind === qKind ||
        (qKind === 'doc' && (e.kind === 'doc' || e.kind === 'pdf' || e.kind === 'markdown' || e.kind === 'text'));
      const nameOk = q === '' || name.toLowerCase().includes(q);
      if (kindOk && nameOk) results.push(e);
      if (e.isDir && recursive && depth < 12 && !SKIP_DIRS.has(name)) await walk(full, depth + 1);
    }
  }
  await walk(abs, 0);
  return results;
}

export function ensureDirSync(p) {
  fs.mkdirSync(p, { recursive: true });
}
