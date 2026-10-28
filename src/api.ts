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
  search: (path: string, q: string, kind = '', recursive = true) =>
    req<{ results: FsEntry[] }>(`/api/search?path=${encodeURIComponent(path)}&q=${encodeURIComponent(q)}&kind=${kind}&recursive=${recursive ? 1 : 0}`),
  text: (path: string) => req<{ text: string; truncated: boolean; size: number }>(`/api/text?path=${encodeURIComponent(path)}`),
  zip: (path: string) => req<{ entries: { name: string; isDir: boolean; size: number; compressedSize: number; kind: string }[] }>(`/api/zip?path=${encodeURIComponent(path)}`),
  rawUrl: (path: string) => `/api/raw?path=${encodeURIComponent(path)}`,
  rename: (path: string, newName: string) => post<{ path: string }>('/api/ops/rename', { path, newName }),
  mkdir: (path: string, name: string) => post<{ path: string }>('/api/ops/mkdir', { path, name }),
  newfile: (path: string, name: string, content = '') => post<{ path: string }>('/api/ops/newfile', { path, name, content }),
  del: (paths: string[]) => post<{ deleted: string[] }>('/api/ops/delete', { paths }),
  copy: (paths: string[], dest: string, overwrite = false) => post<{ results: { name: string; conflict?: boolean; ok?: boolean }[] }>('/api/ops/copy', { paths, dest, overwrite }),
  move: (paths: string[], dest: string, overwrite = false) => post<{ results: { name: string; conflict?: boolean; ok?: boolean }[] }>('/api/ops/move', { paths, dest, overwrite }),
  pasteInfo: (paths: string[], dest: string) => post<{ items: { source: string; target: string; exists: boolean }[] }>('/api/ops/paste-info', { paths, dest }),
  trash: () => req<{ items: TrashItem[] }>('/api/trash'),
  trashRestore: (ids: string[]) => post<{ restored: string[] }>('/api/trash/restore', { ids }),
  trashEmpty: () => post<{ ok: boolean }>('/api/trash/empty', {}),
  pushRecent: (path: string) => post<{ recent: string[] }>('/api/recent', { path }),
};
