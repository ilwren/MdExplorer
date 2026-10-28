// Thin fetch client for the Eidoria filesystem API (always relative URLs).
export interface FsEntry {
  name: string;
  path: string;
  isDir: boolean;
  size: number;
  mtime: number;
  kind: string;
  ext: string;
  children?: number;
  compressedSize?: number;
  /** set for entries inside a zip archive (virtual paths) */
  zipOf?: string;
  zipEntry?: string;
  /** content-search hit */
  snippet?: string;
}

export interface AppSettings {
  theme: 'system' | 'light' | 'dark';
  viewMode: 'grid' | 'list';
  sortBy: 'name' | 'size' | 'mtime' | 'kind';
  sortDir: 'asc' | 'desc';
  showHidden: boolean;
  reduceMotion: boolean;
  confirmDelete: boolean;
  compactDensity: boolean;
}

export interface RootInfo { path: string; name: string; total: number; free: number; used: number }

export interface IndexStatus {
  roots: string[];
  paused: boolean;
  contentEnabled: boolean;
  updatedAt: number;
  fileCount: number;
  sizeBytes: number;
  staleCount?: number;
}

export interface IndexSearchResult {
  results: FsEntry[];
  contentUsed: boolean;
  indexedAt: number;
  paused: boolean;
  contentEnabled: boolean;
}

export interface TrashItem {
  id: string; name: string; originalPath: string; deletedAt: number;
  isDir: boolean; size: number; kind: string;
}

async function req<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.error || `${res.status}`);
  return body as T;
}

const post = <T,>(url: string, data: unknown) =>
  req<T>(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) });

export const api = {
  state: () => req<{ settings: AppSettings; favorites: string[]; recent: string[]; roots: RootInfo[] }>('/api/state'),
  saveSettings: (patch: Partial<AppSettings>) => post<AppSettings>('/api/settings', patch),
  toggleFavorite: (path: string) => post<{ favorites: string[]; added: boolean }>('/api/favorites/toggle', { path }),
  list: (path: string) => req<{ path: string; name: string; parent: string; entries: FsEntry[] }>(`/api/list?path=${encodeURIComponent(path)}`),
  stat: (path: string) => req<FsEntry>(`/api/stat?path=${encodeURIComponent(path)}`),
  search: (path: string, q: string, kind = '', recursive = true, content = false) =>
    req<{ results: FsEntry[] }>(`/api/search?path=${encodeURIComponent(path)}&q=${encodeURIComponent(q)}&kind=${kind}&recursive=${recursive ? 1 : 0}&content=${content ? 1 : 0}`),
  text: (path: string) => req<{ text: string; truncated: boolean; size: number }>(`/api/text?path=${encodeURIComponent(path)}`),
  writeFile: (path: string, content: string) => post<{ ok: boolean }>('/api/fs/write', { path, content }),
  /* local search index (concept §4.1) */
  indexStatus: () => req<IndexStatus>('/api/index/status'),
  indexRebuild: () => post<IndexStatus & { truncated?: boolean }>('/api/index/rebuild', {}),
  indexClear: () => post<IndexStatus>('/api/index/clear', {}),
  indexSettings: (patch: { paused?: boolean; contentEnabled?: boolean }) => post<IndexStatus>('/api/index/settings', patch),
  indexRoot: (action: 'add' | 'remove', root: string) => post<IndexStatus>('/api/index/roots', { action, root }),
  indexSearch: (q: string, opts: { kind?: string; root?: string; since?: number; content?: boolean } = {}) =>
    req<IndexSearchResult>(`/api/index/search?q=${encodeURIComponent(q)}&kind=${opts.kind ?? ''}&root=${encodeURIComponent(opts.root ?? '')}&since=${opts.since ?? 0}&content=${opts.content ? 1 : 0}`),
  /** List zip contents. `inner` omitted -> flat dump of all entries; `inner` (even '') -> children of that virtual folder. */
  zip: (path: string, inner?: string) => req<{ entries: FsEntry[] }>(`/api/zip?path=${encodeURIComponent(path)}${inner !== undefined ? `&inner=${encodeURIComponent(inner)}` : ''}`),
  zipEntryText: (path: string, entry: string) => req<{ text: string; size: number }>(`/api/zip/entry-text?path=${encodeURIComponent(path)}&entry=${encodeURIComponent(entry)}`),
  zipEntryUrl: (path: string, entry: string) => `/api/zip/entry-raw?path=${encodeURIComponent(path)}&entry=${encodeURIComponent(entry)}`,
  zipExtract: (path: string, entry: string | null, dest: string, keepStructure: boolean, policy: string) =>
    post<{ results: { name: string; status: string; target?: string }[] }>('/api/zip/extract', { path, entry, dest, keepStructure, policy }),
  insights: () => req<Insights>('/api/insights'),
  rawUrl: (path: string) => `/api/raw?path=${encodeURIComponent(path)}`,
  /** Resolve the right URL for previewing an entry (real file or zip member). */
  entryUrl: (e: FsEntry) =>
    e.zipOf ? `/api/zip/entry-raw?path=${encodeURIComponent(e.zipOf)}&entry=${encodeURIComponent(e.zipEntry ?? '')}` : `/api/raw?path=${encodeURIComponent(e.path)}`,
  rename: (path: string, newName: string) => post<{ path: string }>('/api/ops/rename', { path, newName }),
  mkdir: (path: string, name: string) => post<{ path: string }>('/api/ops/mkdir', { path, name }),
  newfile: (path: string, name: string, content = '') => post<{ path: string }>('/api/ops/newfile', { path, name, content }),
  del: (paths: string[]) => post<{ deleted: string[] }>('/api/ops/delete', { paths }),
  transfer: (paths: string[], dest: string, move: boolean, policy: 'ask' | 'overwrite' | 'skip' | 'keep-both') =>
    post<{ results: TransferResult[] }>('/api/ops/transfer', { paths, dest, move, policy }),
  copy: (paths: string[], dest: string, overwrite = false) => post<{ results: { name: string; conflict?: boolean; ok?: boolean }[] }>('/api/ops/copy', { paths, dest, overwrite }),
  move: (paths: string[], dest: string, overwrite = false) => post<{ results: { name: string; conflict?: boolean; ok?: boolean }[] }>('/api/ops/move', { paths, dest, overwrite }),
  pasteInfo: (paths: string[], dest: string) => post<{ items: { source: string; target: string; exists: boolean }[] }>('/api/ops/paste-info', { paths, dest }),
  trash: () => req<{ items: TrashItem[] }>('/api/trash'),
  trashRestore: (ids: string[]) => post<{ restored: string[] }>('/api/trash/restore', { ids }),
  trashEmpty: () => post<{ ok: boolean }>('/api/trash/empty', {}),
  pushRecent: (path: string) => post<{ recent: string[] }>('/api/recent', { path }),
};

export interface TransferResult { name: string; status: 'ok' | 'kept' | 'skipped' | 'conflict'; target?: string }

export interface Insights {
  byKind: { kind: string; bytes: number; count: number }[];
  folders: { path: string; bytes: number; files: number }[];
  largeFiles: { path: string; name: string; size: number; kind: string }[];
  recentGrowth: { path: string; name: string; size: number; kind: string; mtime: number }[];
  totalBytes: number;
  totalFiles: number;
  truncated: boolean;
  scannedAt: number;
}
