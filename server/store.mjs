// Settings / favorites / recent / trash persistence (JSON files under data/).
import fsp from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { DATA_DIR, TRASH_DIR, ensureDirSync, safePath, apiError, entryOf } from './fs.mjs';

const SETTINGS_FILE = path.join(DATA_DIR, 'settings.json');
const FAV_FILE = path.join(DATA_DIR, 'favorites.json');
const RECENT_FILE = path.join(DATA_DIR, 'recent.json');
const TRASH_MANIFEST = path.join(TRASH_DIR, 'manifest.json');

const DEFAULT_SETTINGS = {
  theme: 'system',            // 'system' | 'light' | 'dark'
  viewMode: 'grid',           // 'grid' | 'list'
  sortBy: 'name',             // 'name' | 'size' | 'mtime' | 'kind'
  sortDir: 'asc',
  showHidden: false,
  reduceMotion: false,
  confirmDelete: true,
  compactDensity: false,
};

ensureDirSync(DATA_DIR);
ensureDirSync(TRASH_DIR);

async function readJson(file, fallback) {
  try {
    return JSON.parse(await fsp.readFile(file, 'utf8'));
  } catch {
    return fallback;
  }
}

async function writeJson(file, value) {
  const tmp = file + '.tmp';
  await fsp.writeFile(tmp, JSON.stringify(value, null, 2));
  await fsp.rename(tmp, file);
}

export async function getSettings() {
  return { ...DEFAULT_SETTINGS, ...(await readJson(SETTINGS_FILE, {})) };
}

export async function saveSettings(patch) {
  const cur = await getSettings();
  const next = { ...cur };
  for (const k of Object.keys(DEFAULT_SETTINGS)) {
    if (patch[k] !== undefined) next[k] = patch[k];
  }
  await writeJson(SETTINGS_FILE, next);
  return next;
}

export async function getFavorites() {
  return readJson(FAV_FILE, []);
}

export async function toggleFavorite(p) {
  const abs = safePath(p);
  const list = await getFavorites();
  const i = list.indexOf(abs);
  if (i >= 0) list.splice(i, 1);
  else list.push(abs);
  await writeJson(FAV_FILE, list);
  return { favorites: list, added: i < 0 };
}

export async function pushRecent(p) {
  const abs = safePath(p);
  const list = (await readJson(RECENT_FILE, [])).filter((x) => x !== abs);
  list.unshift(abs);
  await writeJson(RECENT_FILE, list.slice(0, 60));
  return list;
}

export async function getRecent() {
  return readJson(RECENT_FILE, []);
}

/* ------------------------------- trash ------------------------------- */

export async function trashList() {
  const manifest = await readJson(TRASH_MANIFEST, []);
  return manifest;
}

export async function trashDelete(paths) {
  const manifest = await readJson(TRASH_MANIFEST, []);
  const done = [];
  for (const p of paths) {
    const abs = safePath(p);
    const e = await entryOf(abs);
    if (!e) continue;
    const id = crypto.randomBytes(8).toString('hex');
    const dest = path.join(TRASH_DIR, id);
    await fsp.cp(abs, dest, { recursive: true });
    await fsp.rm(abs, { recursive: true, force: true });
    manifest.push({
      id,
      name: e.name,
      originalPath: abs,
      deletedAt: Date.now(),
      isDir: e.isDir,
      size: e.size,
      kind: e.kind,
    });
    done.push(e.name);
  }
  await writeJson(TRASH_MANIFEST, manifest);
  return done;
}

export async function trashRestore(ids) {
  const manifest = await readJson(TRASH_MANIFEST, []);
  const keep = [];
  const restored = [];
  for (const item of manifest) {
    if (!ids.includes(item.id)) { keep.push(item); continue; }
    const src = path.join(TRASH_DIR, item.id);
    let dest = item.originalPath;
    // If the original location no longer exists, restore into an ancestor that does.
    let dir = path.dirname(dest);
    let placed = false;
    for (let i = 0; i < 8; i++) {
      try { await fsp.access(dir); placed = true; break; } catch { dir = path.dirname(dir); }
    }
    if (!placed) dest = path.join(ROOT_FALLBACK(), item.name);
    if (await exists(dest)) {
      dest = uniqueName(dest);
    }
    await fsp.cp(src, dest, { recursive: true });
    await fsp.rm(src, { recursive: true, force: true });
    restored.push(item.name);
  }
  await writeJson(TRASH_MANIFEST, keep);
  return restored;
}

function ROOT_FALLBACK() {
  return process.env.EIDORIA_ROOTS ? process.env.EIDORIA_ROOTS.split(':')[0] : '/home/user';
}

async function exists(p) {
  try { await fsp.access(p); return true; } catch { return false; }
}

function uniqueName(p) {
  const dir = path.dirname(p);
  const ext = path.extname(p);
  const base = path.basename(p, ext);
  for (let i = 1; i < 1000; i++) {
    const cand = path.join(dir, `${base} (restored ${i})${ext}`);
    if (!fsExistsSync(cand)) return cand;
  }
  return path.join(dir, `${base} (restored)${ext}`);
}

import fs from 'node:fs';
function fsExistsSync(p) {
  try { fs.accessSync(p); return true; } catch { return false; }
}

export async function trashEmpty() {
  await fsp.rm(TRASH_DIR, { recursive: true, force: true });
  ensureDirSync(TRASH_DIR);
  await writeJson(TRASH_MANIFEST, []);
}
