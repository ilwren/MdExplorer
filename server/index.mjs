// Eidoria web first-version server: filesystem API + Vite middleware (dev) / static (prod).
import express from 'express';
import http from 'node:http';
import path from 'node:path';
import fsp from 'node:fs/promises';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import AdmZip from 'adm-zip';

import {
  ROOTS, safePath, listDir, entryOf, rootsInfo, readText, mimeOf,
  searchEntries, searchContent, scanInsights, apiError, classify, extOf,
} from './fs.mjs';
import { zipList, zipEntryBytes, zipEntries, uniqueTarget } from './zip.mjs';
import {
  indexStatus, rebuildIndex, clearIndex, setIndexSettings, addIndexRoot, removeIndexRoot, searchIndex,
} from './indexstore.mjs';
import {
  getSettings, saveSettings, getFavorites, toggleFavorite, pushRecent, getRecent,
  trashList, trashDelete, trashRestore, trashEmpty,
} from './store.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PORT = Number(process.env.PORT || 5173);
const HOST = process.env.HOST || '0.0.0.0';

const app = express();
app.use(express.json({ limit: '20mb' }));

const wrap = (fn) => (req, res, next) =>
  Promise.resolve(fn(req, res))
    .then((data) => {
      if (data !== undefined && !res.headersSent) res.json(data);
    })
    .catch(next);

/* ------------------------------ bootstrap ------------------------------ */

app.get('/api/state', wrap(async () => {
  return {
    settings: await getSettings(),
    favorites: await getFavorites(),
    recent: await getRecent(),
    roots: await rootsInfo(),
  };
}));

app.post('/api/settings', wrap(async (req) => {
  return saveSettings(req.body || {});
}));

app.post('/api/favorites/toggle', wrap(async (req) => toggleFavorite(req.body.path)));

/* ------------------------------- listing ------------------------------- */

app.get('/api/list', wrap(async (req) => {
  const dir = String(req.query.path || ROOTS[0]);
  return listDir(dir);
}));

app.get('/api/stat', wrap(async (req) => {
  const e = await entryOf(safePath(String(req.query.path)));
  if (!e) throw apiError(404, 'not found');
  return e;
}));

app.get('/api/search', wrap(async (req) => {
  const dir = String(req.query.path || ROOTS[0]);
  const q = String(req.query.q || '');
  const recursive = String(req.query.recursive ?? '1') !== '0';
  const kind = String(req.query.kind || '');
  const content = String(req.query.content ?? '') === '1';
  if (content) return { results: await searchContent(dir, q) };
  return { results: await searchEntries(dir, q, { recursive, kind }) };
}));

app.get('/api/insights', wrap(async () => scanInsights()));

/* --------------------------- local search index --------------------------- */

app.get('/api/index/status', wrap(async () => indexStatus()));
app.post('/api/index/rebuild', wrap(async () => rebuildIndex()));
app.post('/api/index/clear', wrap(async () => clearIndex()));
app.post('/api/index/settings', wrap(async (req) => setIndexSettings(req.body || {})));
app.post('/api/index/roots', wrap(async (req) => {
  const { action, root } = req.body || {};
  return action === 'add' ? addIndexRoot(root) : removeIndexRoot(root);
}));
app.get('/api/index/search', wrap(async (req) => searchIndex(String(req.query.q || ''), {
  kind: String(req.query.kind || ''),
  root: String(req.query.root || ''),
  since: Number(req.query.since || 0),
  content: req.query.content === '1' || req.query.content === 'true',
})));

/* ------------------------------- file write ------------------------------- */

app.post('/api/fs/write', wrap(async (req) => {
  const abs = safePath(req.body.path);
  const content = String(req.body.content ?? '');
  if (content.length > 8 * 1024 * 1024) throw apiError(400, '内容过大');
  await fsp.writeFile(abs, content, 'utf8');
  return { ok: true };
}));

app.get('/api/text', wrap(async (req) => {
  return readText(safePath(String(req.query.path)));
}));

app.get('/api/raw', wrap(async (req, res) => {
  const abs = safePath(String(req.query.path));
  const st = await fsp.stat(abs);
  if (!st.isFile()) throw apiError(400, 'not a file');
  res.setHeader('Content-Type', mimeOf(abs));
  res.setHeader('Content-Length', st.size);
  res.setHeader('Cache-Control', 'private, max-age=5');
  fs.createReadStream(abs).pipe(res);
}));

app.get('/api/zip', wrap(async (req) => {
  const abs = safePath(String(req.query.path));
  const inner = req.query.inner;
  // no `inner` param -> flat entry dump (tree building / previews)
  if (inner === undefined) return { entries: zipEntries(abs) };
  // `inner` present (even empty) -> virtual-folder children listing
  return { entries: zipList(abs, String(inner)) };
}));

app.get('/api/zip/entry-text', wrap(async (req) => {
  const abs = safePath(String(req.query.path));
  const entry = String(req.query.entry || '');
  const buf = zipEntryBytes(abs, entry);
  return { text: buf.toString('utf8').slice(0, 256 * 1024), size: buf.length };
}));

app.get('/api/zip/entry-raw', wrap(async (req, res) => {
  const abs = safePath(String(req.query.path));
  const entry = String(req.query.entry || '');
  const buf = zipEntryBytes(abs, entry);
  res.setHeader('Content-Type', mimeOf(entry));
  res.setHeader('Content-Length', buf.length);
  res.end(buf);
}));

app.post('/api/zip/extract', wrap(async (req) => {
  const abs = safePath(req.body.path);
  const entry = req.body.entry ? String(req.body.entry) : null;
  const dest = safePath(req.body.dest);
  const keepStructure = req.body.keepStructure !== false;
  const policy = String(req.body.policy || 'ask'); // ask | overwrite | skip | keep-both
  await fsp.mkdir(dest, { recursive: true }).catch(() => {});
  const st = await fsp.stat(dest).catch(() => null);
  if (!st?.isDirectory()) throw apiError(400, 'destination is not a directory');

  const names = entry ? [entry] : zipEntries(abs).filter((e) => !e.isDir).map((e) => e.name);
  const results = [];
  for (const name of names) {
    if (name.endsWith('/')) continue;
    const data = zipEntryBytes(abs, name);
    const rel = keepStructure ? name : name.split('/').pop();
    let target = path.join(dest, rel);
    safePath(target);
    await fsp.mkdir(path.dirname(target), { recursive: true });
    if (fs.existsSync(target)) {
      if (policy === 'skip' || policy === 'ask') {
        results.push({ name, status: policy === 'ask' ? 'conflict' : 'skipped' });
        continue;
      }
      if (policy === 'keep-both') target = uniqueTarget(path.dirname(target), path.basename(target));
    }
    await fsp.writeFile(target, data);
    results.push({ name, status: 'ok', target });
  }
  return { results };
}));

/* --------------------------------- ops --------------------------------- */

app.post('/api/ops/rename', wrap(async (req) => {
  const abs = safePath(req.body.path);
  const newName = String(req.body.newName || '').trim();
  if (!newName || newName.includes('/') || newName.includes('\\')) throw apiError(400, 'invalid name');
  const dest = path.join(path.dirname(abs), newName);
  safePath(dest);
  await fsp.rename(abs, dest);
  return { path: dest };
}));

app.post('/api/ops/mkdir', wrap(async (req) => {
  const dir = safePath(req.body.path);
  const name = String(req.body.name || '').trim();
  if (!name || name.includes('/')) throw apiError(400, 'invalid name');
  const dest = path.join(dir, name);
  safePath(dest);
  await fsp.mkdir(dest, { recursive: true });
  return { path: dest };
}));

app.post('/api/ops/newfile', wrap(async (req) => {
  const dir = safePath(req.body.path);
  const name = String(req.body.name || '').trim();
  if (!name || name.includes('/')) throw apiError(400, 'invalid name');
  const dest = path.join(dir, name);
  safePath(dest);
  await fsp.writeFile(dest, req.body.content ?? '');
  return { path: dest };
}));

app.post('/api/ops/delete', wrap(async (req) => {
  const names = await trashDelete(req.body.paths || []);
  return { deleted: names };
}));

app.post('/api/ops/transfer', wrap(async (req) => {
  return transfer(req.body, !!req.body.move);
}));

app.post('/api/ops/copy', wrap(async (req) => {
  return transfer({ ...req.body, policy: req.body.overwrite ? 'overwrite' : 'ask' }, false);
}));

app.post('/api/ops/move', wrap(async (req) => {
  return transfer({ ...req.body, policy: req.body.overwrite ? 'overwrite' : 'ask' }, true);
}));

async function transfer({ paths, dest, policy = 'ask' }, move) {
  const destDir = safePath(dest);
  const st = await fsp.stat(destDir).catch(() => null);
  if (!st?.isDirectory()) throw apiError(400, 'destination is not a directory');
  const results = [];
  for (const p of paths || []) {
    const abs = safePath(p);
    const base = path.basename(abs);
    let target = path.join(destDir, base);
    safePath(target);
    if (fs.existsSync(target)) {
      if (policy === 'ask') { results.push({ name: base, status: 'conflict' }); continue; }
      if (policy === 'skip') { results.push({ name: base, status: 'skipped' }); continue; }
      if (policy === 'keep-both') target = uniqueTarget(destDir, base);
      else await fsp.rm(target, { recursive: true, force: true });
    }
    if (move) {
      await fsp.rename(abs, target).catch(async () => {
        await fsp.cp(abs, target, { recursive: true });
        await fsp.rm(abs, { recursive: true, force: true });
      });
    } else {
      await fsp.cp(abs, target, { recursive: true });
    }
    results.push({ name: base, status: policy === 'keep-both' ? 'kept' : 'ok', target });
  }
  return { results };
}

app.post('/api/ops/paste-info', wrap(async (req) => {
  // Check whether paste targets already exist (conflict pre-check).
  const destDir = safePath(req.body.dest);
  const out = [];
  for (const p of req.body.paths || []) {
    const abs = safePath(p);
    const target = path.join(destDir, path.basename(abs));
    out.push({ source: abs, target, exists: fs.existsSync(target) });
  }
  return { items: out };
}));

/* --------------------------------- trash -------------------------------- */

app.get('/api/trash', wrap(async () => ({ items: await trashList() })));
app.post('/api/trash/restore', wrap(async (req) => ({ restored: await trashRestore(req.body.ids || []) })));
app.post('/api/trash/empty', wrap(async () => { await trashEmpty(); return { ok: true }; }));

/* ------------------------------- transfer ------------------------------- */

app.post('/api/recent', wrap(async (req) => ({ recent: await pushRecent(req.body.path) })));

/* -------------------------------- static -------------------------------- */

const distDir = path.join(__dirname, '..', 'dist');
const publicDir = path.join(__dirname, '..', 'public');

app.use('/eidoria-icon.png', (req, res) => {
  const icon = path.join(publicDir, 'eidoria-icon.png');
  if (fs.existsSync(icon)) return res.sendFile(icon);
  res.status(404).end();
});

app.use((err, req, res, next) => {
  const status = err.status || 500;
  if (status >= 500) console.error(err);
  res.status(status).json({ error: err.message || 'error' });
});

const server = http.createServer(app);

const wantDev = process.env.EIDORIA_SERVE_STATIC !== '1' || !fs.existsSync(path.join(distDir, 'index.html'));
if (wantDev) {
  const { createServer: createViteServer } = await import('vite');
  const vite = await createViteServer({
    configFile: path.join(__dirname, '..', 'vite.config.ts'),
    server: { middlewareMode: true, hmr: { server }, allowedHosts: true },
    appType: 'spa',
  });
  app.use(vite.middlewares);
  console.log('[eidoria] vite dev middleware enabled');
} else {
  app.use(express.static(distDir));
  app.get('*', (req, res) => res.sendFile(path.join(distDir, 'index.html')));
  console.log('[eidoria] serving static dist/');
}

server.listen(PORT, HOST, () => {
  console.log(`[eidoria] listening on http://${HOST}:${PORT}  roots=${ROOTS.join(':')}`);
});
