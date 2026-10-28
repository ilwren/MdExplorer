import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { api, type AppSettings, type FsEntry, type RootInfo } from '../api';

export type Location =
  | { kind: 'home' }
  | { kind: 'dir'; path: string }
  | { kind: 'category'; cat: string }
  | { kind: 'recent' }
  | { kind: 'favorites' }
  | { kind: 'trash' }
  | { kind: 'settings' };

export function locKey(l: Location): string {
  return l.kind === 'dir' ? `dir:${l.path}` : l.kind === 'category' ? `cat:${l.cat}` : l.kind;
}

export function locTitle(l: Location): string {
  switch (l.kind) {
    case 'home': return '主页';
    case 'dir': return l.path.split('/').filter(Boolean).pop() || l.path;
    case 'category': return { image: '图片', video: '视频', audio: '音频', doc: '文档', archive: '压缩包' }[l.cat] ?? '分类';
    case 'recent': return '最近';
    case 'favorites': return '收藏';
    case 'trash': return '回收站';
    case 'settings': return '设置';
  }
}

export interface TabState {
  id: string;
  location: Location;
  history: Location[];
  histIndex: number;
  filter: string;
  selection: string[];
  previewTab: 'preview' | 'properties';
}

export interface DialogState {
  kind: 'rename' | 'newfolder' | 'newfile' | 'confirm-delete' | 'confirm-empty-trash' | 'confirm-restore-gone';
  path?: string;
  name?: string;
  paths?: string[];
  dir?: string;
}

export interface Toast { id: number; text: string }

interface SearchState {
  open: boolean;
  query: string;
  kind: string;
  recursive: boolean;
  results: FsEntry[];
  busy: boolean;
}

interface Ctx {
  ready: boolean;
  settings: AppSettings;
  roots: RootInfo[];
  favorites: string[];
  recent: string[];
  tabs: TabState[];
  activeTab: TabState;
  listing: FsEntry[];
  listingBusy: boolean;
  listingPath: string;
  refreshTick: number;
  clipboard: { op: 'copy' | 'cut'; paths: string[] } | null;
  search: SearchState;
  dialog: DialogState | null;
  toasts: Toast[];
  previewPaneOpen: boolean;
  floatPreviewPos: { x: number; y: number } | null;
  previewTarget: FsEntry | null;
  /* actions */
  navigate: (l: Location, opts?: { push?: boolean }) => void;
  back: () => void;
  forward: () => void;
  up: () => void;
  refresh: () => void;
  newTab: (l?: Location) => void;
  closeTab: (id: string) => void;
  setActiveTab: (id: string) => void;
  setFilter: (s: string) => void;
  setPreviewTab: (t: 'preview' | 'properties') => void;
  select: (path: string, mode?: 'single' | 'toggle' | 'add') => void;
  selectRange: (path: string) => void;
  selectAll: () => void;
  clearSelection: () => void;
  openEntry: (e: FsEntry) => void;
  setPreviewTarget: (e: FsEntry | null) => void;
  setPreviewPaneOpen: (b: boolean) => void;
  setFloatPreviewPos: (p: { x: number; y: number } | null) => void;
  setClipboard: (c: Ctx['clipboard']) => void;
  setSearch: (patch: Partial<SearchState>) => void;
  setDialog: (d: DialogState | null) => void;
  toast: (text: string) => void;
  patchSettings: (p: Partial<AppSettings>) => void;
  toggleFavorite: (path: string) => void;
  reloadMeta: () => void;
}

const AppCtx = createContext<Ctx>(null as unknown as Ctx);
export const useApp = () => useContext(AppCtx);

let tabSeq = 1;
let toastSeq = 1;

function makeTab(loc: Location): TabState {
  return {
    id: `t${tabSeq++}`,
    location: loc,
    history: [loc],
    histIndex: 0,
    filter: '',
    selection: [],
    previewTab: 'preview',
  };
}

export function AppProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [settings, setSettings] = useState<AppSettings>({
    theme: 'system', viewMode: 'grid', sortBy: 'name', sortDir: 'asc',
    showHidden: false, reduceMotion: false, confirmDelete: true, compactDensity: false,
  });
  const [roots, setRoots] = useState<RootInfo[]>([]);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [recent, setRecent] = useState<string[]>([]);
  const [tabs, setTabs] = useState<TabState[]>(() => [makeTab({ kind: 'home' })]);
  const [activeTabId, setActiveTabId] = useState<string>(() => tabs[0].id);
  const [listing, setListing] = useState<FsEntry[]>([]);
  const [listingBusy, setListingBusy] = useState(false);
  const [listingPath, setListingPath] = useState('');
  const [refreshTick, setRefreshTick] = useState(0);
  const [clipboard, setClipboard] = useState<Ctx['clipboard']>(null);
  const [search, setSearchState] = useState<SearchState>({
    open: false, query: '', kind: '', recursive: true, results: [], busy: false,
  });
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [previewPaneOpen, setPreviewPaneOpen] = useState(true);
  const [floatPreviewPos, setFloatPreviewPos] = useState<{ x: number; y: number } | null>(null);
  const [previewTarget, setPreviewTarget] = useState<FsEntry | null>(null);
  const lastSelected = useRef<string | null>(null);

  const activeTab = useMemo(
    () => tabs.find((t) => t.id === activeTabId) ?? tabs[0],
    [tabs, activeTabId],
  );

  /* ------------------------------ bootstrap ------------------------------ */
  const reloadMeta = useCallback(() => {
    api.state().then((s) => {
      setSettings(s.settings);
      setFavorites(s.favorites);
      setRecent(s.recent);
      setRoots(s.roots);
      setReady(true);
    }).catch(() => setReady(true));
  }, []);
  useEffect(reloadMeta, [reloadMeta]);

  /* ------------------------------ theme ------------------------------ */
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const apply = () => {
      const dark = settings.theme === 'dark' || (settings.theme === 'system' && mq.matches);
      document.documentElement.dataset.theme = dark ? 'dark' : 'light';
    };
    apply();
    mq.addEventListener('change', apply);
    return () => mq.removeEventListener('change', apply);
  }, [settings.theme]);

  useEffect(() => {
    document.documentElement.dataset.reduceMotion = settings.reduceMotion ? '1' : '0';
  }, [settings.reduceMotion]);

  /* ------------------------------ listing ------------------------------ */
  const loc = activeTab.location;
  useEffect(() => {
    let alive = true;
    setListingBusy(true);
    const done = (entries: FsEntry[], path: string) => {
      if (!alive) return;
      setListing(entries);
      setListingPath(path);
      setListingBusy(false);
    };
    const fail = () => { if (alive) { setListing([]); setListingBusy(false); } };

    if (loc.kind === 'dir') {
      api.list(loc.path).then((r) => {
        let es = r.entries;
        if (!settings.showHidden) es = es.filter((e) => !e.name.startsWith('.'));
        done(sortEntries(es, settings), r.path);
      }).catch(fail);
    } else if (loc.kind === 'category') {
      const kinds = { image: ['image'], video: ['video'], audio: ['audio'], doc: ['pdf', 'doc', 'markdown', 'text'], archive: ['archive'] }[loc.cat] ?? [];
      Promise.all(roots.map((r) => api.search(r.path, '', '', true).catch(() => ({ results: [] }))))
        .then((rs) => {
          let es = rs.flatMap((r) => r.results).filter((e) => kinds.includes(e.kind));
          if (!settings.showHidden) es = es.filter((e) => !e.name.startsWith('.'));
          done(sortEntries(es, settings), '');
        }).catch(fail);
    } else if (loc.kind === 'recent') {
      Promise.all(recent.map((p) => api.stat(p).catch(() => null)))
        .then((rs) => done(sortEntries(rs.filter(Boolean) as FsEntry[], settings), ''))
        .catch(fail);
    } else if (loc.kind === 'favorites') {
      Promise.all(favorites.map((p) => api.stat(p).catch(() => null)))
        .then((rs) => done(rs.filter(Boolean) as FsEntry[], ''))
        .catch(fail);
    } else {
      done([], '');
    }
    return () => { alive = false; };
  }, [loc, settings.showHidden, settings.sortBy, settings.sortDir, refreshTick, roots, recent, favorites]);

  /* ------------------------------ nav helpers ------------------------------ */
  const setActiveTab = useCallback((id: string) => {
    setActiveTabId(id);
    setPreviewTarget(null);
  }, []);

  const patchTab = useCallback((patch: Partial<TabState>) => {
    setTabs((ts) => ts.map((t) => (t.id === activeTabId ? { ...t, ...patch } : t)));
  }, [activeTabId]);

  const navigate = useCallback((l: Location, opts?: { push?: boolean }) => {
    setTabs((ts) => ts.map((t) => {
      if (t.id !== activeTabId) return t;
      const push = opts?.push !== false;
      const history = push ? [...t.history.slice(0, t.histIndex + 1), l] : t.history;
      return {
        ...t,
        location: l,
        history,
        histIndex: push ? history.length - 1 : t.histIndex,
        filter: '',
        selection: [],
      };
    }));
    setPreviewTarget(null);
  }, [activeTabId]);

  const back = useCallback(() => {
    setTabs((ts) => ts.map((t) => {
      if (t.id !== activeTabId || t.histIndex <= 0) return t;
      return { ...t, histIndex: t.histIndex - 1, location: t.history[t.histIndex - 1], selection: [] };
    }));
    setPreviewTarget(null);
  }, [activeTabId]);

  const forward = useCallback(() => {
    setTabs((ts) => ts.map((t) => {
      if (t.id !== activeTabId || t.histIndex >= t.history.length - 1) return t;
      return { ...t, histIndex: t.histIndex + 1, location: t.history[t.histIndex + 1], selection: [] };
    }));
    setPreviewTarget(null);
  }, [activeTabId]);

  const up = useCallback(() => {
    if (activeTab.location.kind !== 'dir') { navigate({ kind: 'home' }); return; }
    const parent = activeTab.location.path.split('/').slice(0, -1).join('/') || '/';
    if (roots.some((r) => parent === r.path || parent.startsWith(r.path))) navigate({ kind: 'dir', path: parent });
    else navigate({ kind: 'home' });
  }, [activeTab, navigate, roots]);

  const refresh = useCallback(() => setRefreshTick((n) => n + 1), []);

  const newTab = useCallback((l: Location = { kind: 'home' }) => {
    const t = makeTab(l);
    setTabs((ts) => [...ts, t]);
    setActiveTabId(t.id);
    setPreviewTarget(null);
  }, []);

  const closeTab = useCallback((id: string) => {
    setTabs((ts) => {
      if (ts.length <= 1) return ts;
      const next = ts.filter((t) => t.id !== id);
      if (id === activeTabId) setActiveTabId(next[next.length - 1].id);
      return next;
    });
  }, [activeTabId]);

  /* ------------------------------ selection ------------------------------ */
  const select = useCallback((path: string, mode: 'single' | 'toggle' | 'add' = 'single') => {
    lastSelected.current = path;
    setTabs((ts) => ts.map((t) => {
      if (t.id !== activeTabId) return t;
      if (mode === 'single') return { ...t, selection: [path] };
      if (mode === 'toggle') {
        const has = t.selection.includes(path);
        return { ...t, selection: has ? t.selection.filter((p) => p !== path) : [...t.selection, path] };
      }
      return { ...t, selection: t.selection.includes(path) ? t.selection : [...t.selection, path] };
    }));
  }, [activeTabId]);

  const selectRange = useCallback((path: string) => {
    const anchor = lastSelected.current;
    const visible = listing.map((e) => e.path);
    const a = anchor ? visible.indexOf(anchor) : -1;
    const b = visible.indexOf(path);
    if (a < 0 || b < 0) { select(path); return; }
    const [lo, hi] = a < b ? [a, b] : [b, a];
    patchTab({ selection: visible.slice(lo, hi + 1) });
  }, [listing, patchTab, select]);

  const selectAll = useCallback(() => patchTab({ selection: listing.map((e) => e.path) }), [listing, patchTab]);
  const clearSelection = useCallback(() => patchTab({ selection: [] }), [patchTab]);

  const setFilter = useCallback((s: string) => patchTab({ filter: s }), [patchTab]);
  const setPreviewTab = useCallback((t: 'preview' | 'properties') => patchTab({ previewTab: t }), [patchTab]);

  /* ------------------------------ open / preview ------------------------------ */
  const openEntry = useCallback((e: FsEntry) => {
    if (e.isDir) {
      api.pushRecent(e.path).then((r) => setRecent(r.recent)).catch(() => {});
      navigate({ kind: 'dir', path: e.path });
      return;
    }
    setPreviewTarget(e);
    if (e.kind === 'markdown' || e.kind === 'code' || e.kind === 'text' || e.kind === 'pdf' || e.kind === 'image' || e.kind === 'video' || e.kind === 'audio' || e.kind === 'archive') {
      setPreviewTarget(e);
    }
  }, [navigate]);

  /* ------------------------------ misc ------------------------------ */
  const toast = useCallback((text: string) => {
    const id = toastSeq++;
    setToasts((ts) => [...ts, { id, text }]);
    setTimeout(() => setToasts((ts) => ts.filter((t) => t.id !== id)), 3200);
  }, []);

  const patchSettings = useCallback((p: Partial<AppSettings>) => {
    setSettings((s) => ({ ...s, ...p }));
    api.saveSettings(p).catch(() => {});
  }, []);

  const toggleFavorite = useCallback((path: string) => {
    api.toggleFavorite(path).then((r) => setFavorites(r.favorites)).catch(() => {});
  }, []);

  const setSearch = useCallback((patch: Partial<SearchState>) => {
    setSearchState((s) => ({ ...s, ...patch }));
  }, []);

  const value: Ctx = {
    ready, settings, roots, favorites, recent,
    tabs, activeTab, listing, listingBusy, listingPath, refreshTick,
    clipboard, search, dialog, toasts,
    previewPaneOpen, floatPreviewPos, previewTarget,
    navigate, back, forward, up, refresh,
    newTab, closeTab, setActiveTab, setFilter, setPreviewTab,
    select, selectRange, selectAll, clearSelection,
    openEntry, setPreviewTarget, setPreviewPaneOpen, setFloatPreviewPos,
    setClipboard, setSearch, setDialog, toast, patchSettings, toggleFavorite, reloadMeta,
  };
  return <AppCtx.Provider value={value}>{children}</AppCtx.Provider>;
}

export function sortEntries(es: FsEntry[], s: AppSettings): FsEntry[] {
  const dir = s.sortDir === 'desc' ? -1 : 1;
  const arr = [...es];
  arr.sort((a, b) => {
    if (a.isDir !== b.isDir) return a.isDir ? -1 : 1;
    switch (s.sortBy) {
      case 'size': return dir * (a.size - b.size);
      case 'mtime': return dir * (b.mtime - a.mtime);
      case 'kind': return dir * a.kind.localeCompare(b.kind) || dir * a.name.localeCompare(b.name, 'zh');
      default: return dir * a.name.localeCompare(b.name, 'zh', { numeric: true, sensitivity: 'base' });
    }
  });
  return arr;
}
