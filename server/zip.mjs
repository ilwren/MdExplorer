// Zip helpers: entry listing, entry bytes, extraction with conflict policy.
import fsp from 'node:fs/promises';
import fsSync from 'node:fs';
import path from 'node:path';
import AdmZip from 'adm-zip';
import { safePath, apiError, classify, extOf } from './fs.mjs';

export function openZip(p) {
  const abs = safePath(p);
  try {
    return new AdmZip(abs);
  } catch (e) {
    throw apiError(400, `无法读取压缩包: ${e.message}`);
  }
}

export function zipEntries(abs) {
  const zip = openZip(abs);
  return zip.getEntries().map((en) => ({
    name: en.entryName.replace(/\/+$/, ''),
    isDir: en.isDirectory,
    size: en.header.size,
    compressedSize: en.header.compressedSize,
    kind: classify(path.basename(en.entryName), en.isDirectory),
    mtime: en.header.time?.getTime?.() ?? 0,
  }));
}

/** List entries under a prefix (virtual folder inside the zip). */
export function zipList(abs, inner) {
  const all = zipEntries(abs);
  const prefix = inner ? inner.replace(/\/+$/, '') + '/' : '';
  const out = [];
  const seen = new Set();
  for (const e of all) {
    if (!e.name.startsWith(prefix)) continue;
    const rest = e.name.slice(prefix.length);
    if (!rest) continue;
    const slash = rest.indexOf('/');
    if (slash >= 0) {
      // child folder
      const dirName = rest.slice(0, slash);
      if (seen.has(dirName)) continue;
      seen.add(dirName);
      const children = all.filter((x) => x.name.startsWith(prefix + dirName + '/'));
      out.push({
        name: dirName,
        path: `${prefix}${dirName}`,
        isDir: true,
        size: 0,
        mtime: 0,
        kind: 'folder',
        ext: '',
        children: children.length,
      });
    } else {
      out.push({
        name: rest,
        path: e.name,
        isDir: false,
        size: e.size,
        compressedSize: e.compressedSize,
        mtime: e.mtime,
        kind: e.kind,
        ext: extOf(rest),
      });
    }
  }
  return out;
}

export function zipEntryBytes(abs, entryName) {
  const zip = openZip(abs);
  const en = zip.getEntry(entryName);
  if (!en || en.isDirectory) throw apiError(404, 'entry not found');
  return en.getData();
}

/** Unique target name: `name.ext` -> `name (2).ext` … */
export function uniqueTarget(destDir, base) {
  const ext = path.extname(base);
  const stem = path.basename(base, ext);
  for (let i = 2; i < 1000; i++) {
    const cand = path.join(destDir, `${stem} (${i})${ext}`);
    if (!fsExists(cand)) return cand;
  }
  return path.join(destDir, `${stem} (restored)${ext}`);
}

function fsExists(p) {
  try { fsSync.accessSync(p); return true; } catch { return false; }
}
