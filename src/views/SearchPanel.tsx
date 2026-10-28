import React, { useEffect, useRef, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { IconButton, Segmented } from '../ui/widgets';
import { formatSize, kindMeta } from '../ui/kinds';

const KIND_FILTERS = [
  { id: '', label: '全部' },
  { id: 'image', label: '图片' },
  { id: 'video', label: '视频' },
  { id: 'audio', label: '音频' },
  { id: 'doc', label: '文档' },
  { id: 'archive', label: '压缩包' },
  { id: 'code', label: '代码' },
];

export function SearchPanel() {
  const app = useApp();
  const s = app.search;
  const inputRef = useRef<HTMLInputElement>(null);
  const [scope, setScope] = useState<'folder' | 'deep'>('deep');

  useEffect(() => {
    if (s.open) setTimeout(() => inputRef.current?.focus(), 30);
  }, [s.open]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        app.setSearch({ open: true });
      }
      if (e.key === 'Escape' && s.open) app.setSearch({ open: false });
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [app, s.open]);

  if (!s.open) return null;

  const base = app.activeTab.location.kind === 'dir'
    ? app.activeTab.location.path
    : (app.roots[0]?.path ?? '/');

  const runSearch = (q: string, kind: string, deep: boolean) => {
    app.setSearch({ query: q, kind, recursive: deep, busy: true });
    api.search(base, q, kind, deep)
      .then((r) => app.setSearch({ results: r.results.slice(0, 200), busy: false }))
      .catch(() => app.setSearch({ results: [], busy: false }));
  };

  const onInput = (v: string) => {
    app.setSearch({ query: v });
    if (!v.trim()) { app.setSearch({ results: [] }); return; }
    runSearch(v, s.kind, scope === 'deep');
  };

  return (
    <div className="searchpanel-scrim" onMouseDown={(e) => e.target === e.currentTarget && app.setSearch({ open: false })}>
      <div className="searchpanel" role="dialog" aria-label="搜索">
        <div className="searchpanel__row">
          <Icon name="search" size={22} className="searchpanel__icon" />
          <input
            ref={inputRef}
            className="searchpanel__input"
            placeholder="搜索文件名…"
            value={s.query}
            onChange={(e) => onInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key !== 'Escape') e.stopPropagation();
              if (e.key === 'Enter') runSearch(s.query, s.kind, true);
            }}
          />
          {s.busy && <span className="searchpanel__busy">搜索中…</span>}
          <IconButton name="close" title="关闭搜索" variant="plain" onClick={() => app.setSearch({ open: false })} />
        </div>

        <div className="searchpanel__filters">
          <div className="searchpanel__kinds">
            {KIND_FILTERS.map((k) => (
              <button
                key={k.id}
                className={`filterchip ${s.kind === k.id ? 'is-active' : ''}`}
                onClick={() => runSearch(s.query || '', k.id, scope === 'deep')}
              >
                {k.label}
              </button>
            ))}
          </div>
          <Segmented
            value={scope}
            options={[
              { id: 'folder', label: '当前目录' },
              { id: 'deep', label: '递归' },
            ]}
            onChange={(v) => { setScope(v); if (s.query.trim()) runSearch(s.query, s.kind, v === 'deep'); }}
          />
        </div>

        <div className="searchpanel__results">
          {s.results.length === 0 ? (
            <div className="searchpanel__hint">
              <p>{s.query ? '没有找到匹配的文件' : '输入关键词开始搜索；回车递归搜索子目录'}</p>
              <p className="searchpanel__hint-sub">搜索范围：{base}</p>
            </div>
          ) : (
            s.results.map((e) => <SearchRow key={e.path} e={e} />)
          )}
        </div>
      </div>
    </div>
  );
}

function SearchRow({ e }: { e: FsEntry }) {
  const app = useApp();
  const meta = kindMeta(e.kind);
  const parent = e.path.split('/').slice(0, -1).join('/');
  return (
    <button
      className="searchrow"
      onClick={() => {
        if (e.isDir) {
          app.navigate({ kind: 'dir', path: e.path });
        } else {
          app.navigate({ kind: 'dir', path: parent });
          app.setPreviewTarget(e);
        }
        app.setSearch({ open: false });
      }}
    >
      <span className="searchrow__well" style={{ background: meta.wellBg, color: meta.wellFg }}>
        <Icon name={meta.icon} size={18} />
      </span>
      <span className="searchrow__body">
        <span className="searchrow__name">{e.name}</span>
        <span className="searchrow__path">{parent}</span>
      </span>
      <span className="searchrow__meta">{e.isDir ? '文件夹' : formatSize(e.size)}</span>
    </button>
  );
}
