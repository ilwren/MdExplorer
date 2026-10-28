import React, { useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { IconWell, Menu, type MenuItem } from '../ui/widgets';
import { formatCount, formatSize, kindMeta } from '../ui/kinds';

/* ------------------------------ entry visuals ------------------------------ */

function EntryVisual({ e, size, radius }: { e: FsEntry; size: number; radius: number }) {
  const meta = kindMeta(e.kind);
  const isThumb = e.kind === 'image' && e.ext !== 'svg' && e.ext !== 'ico';
  if (isThumb) {
    return (
      <span className="thumb" style={{ borderRadius: radius }}>
        <img src={api.rawUrl(e.path)} alt="" loading="lazy" />
      </span>
    );
  }
  return <IconWell bg={meta.wellBg} fg={meta.wellFg} icon={meta.icon} size={size} radius={radius} />;
}

export function FileCard({ e }: { e: FsEntry }) {
  const app = useApp();
  const selected = app.activeTab.selection.includes(e.path);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const metric = e.isDir ? formatCount(e.children) : formatSize(e.size);

  return (
    <article
      className={`filecard ${selected ? 'is-selected' : ''}`}
      onClick={(ev) => {
        if (ev.shiftKey) app.selectRange(e.path);
        else if (ev.ctrlKey || ev.metaKey) app.select(e.path, 'toggle');
        else { app.select(e.path); app.setPreviewTarget(e); }
      }}
      onDoubleClick={() => app.openEntry(e)}
      onContextMenu={(ev) => {
        ev.preventDefault();
        if (!app.activeTab.selection.includes(e.path)) app.select(e.path);
        setMenu({ x: ev.clientX, y: ev.clientY });
      }}
    >
      <div className="filecard__thumb">
        <EntryVisual e={e} size={62} radius={18} />
        {selected && <span className="checkbadge"><Icon name="check" size={14} /></span>}
      </div>
      <div className="filecard__name" title={e.name}>{e.name}</div>
      <div className="filecard__meta">{metric}</div>
      {menu && (
        <EntryMenu
          e={e}
          x={menu.x} y={menu.y}
          onClose={() => setMenu(null)}
        />
      )}
    </article>
  );
}

export function FileRow({ e }: { e: FsEntry }) {
  const app = useApp();
  const selected = app.activeTab.selection.includes(e.path);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const metric = e.isDir ? formatCount(e.children) : formatSize(e.size);
  return (
    <div
      className={`filerow ${selected ? 'is-selected' : ''}`}
      onClick={(ev) => {
        if (ev.shiftKey) app.selectRange(e.path);
        else if (ev.ctrlKey || ev.metaKey) app.select(e.path, 'toggle');
        else { app.select(e.path); app.setPreviewTarget(e); }
      }}
      onDoubleClick={() => app.openEntry(e)}
      onContextMenu={(ev) => {
        ev.preventDefault();
        if (!app.activeTab.selection.includes(e.path)) app.select(e.path);
        setMenu({ x: ev.clientX, y: ev.clientY });
      }}
    >
      {selected ? (
        <span className="filerow__check is-on"><Icon name="check" size={16} /></span>
      ) : (
        <EntryVisual e={e} size={38} radius={10} />
      )}
      <div className="filerow__name" title={e.name}>{e.name}</div>
      <div className="filerow__meta">{metric}</div>
      <button
        className="filerow__more"
        title="更多操作"
        onClick={(ev) => {
          ev.stopPropagation();
          const r = (ev.currentTarget as HTMLElement).getBoundingClientRect();
          setMenu({ x: r.right - 200, y: r.bottom + 4 });
        }}
      >
        <Icon name="more_vert" size={18} />
      </button>
      {menu && <EntryMenu e={e} x={menu.x} y={menu.y} onClose={() => setMenu(null)} />}
    </div>
  );
}

export function EntryMenu({ e, x, y, onClose }: { e: FsEntry; x: number; y: number; onClose: () => void }) {
  const app = useApp();
  return (
    <Menu
      x={x} y={y}
      onClose={onClose}
      onPick={(id) => {
        const sel = app.activeTab.selection.includes(e.path) ? app.activeTab.selection : [e.path];
        switch (id) {
          case 'open': app.openEntry(e); break;
          case 'preview': app.setPreviewTarget(e); break;
          case 'rename': app.setDialog({ kind: 'rename', path: e.path, name: e.name }); break;
          case 'copy': app.setClipboard({ op: 'copy', paths: sel }); break;
          case 'cut': app.setClipboard({ op: 'cut', paths: sel }); break;
          case 'delete': app.setDialog({ kind: 'confirm-delete', paths: sel }); break;
          case 'copypath':
            navigator.clipboard?.writeText(e.path).then(() => app.toast('已复制路径'));
            break;
          case 'props':
            app.setPreviewTarget(e);
            app.setPreviewTab('properties');
            app.setPreviewPaneOpen(true);
            break;
          case 'fav': app.toggleFavorite(e.path); break;
          case 'newtab':
            if (e.isDir) app.newTab({ kind: 'dir', path: e.path });
            break;
        }
      }}
      items={[
        { id: 'open', label: e.isDir ? '打开' : '预览', icon: e.isDir ? 'folder_open' : 'preview' },
        e.isDir ? { id: 'newtab', label: '在新标签页打开', icon: 'filter_none' } : null,
        { id: 'props', label: '属性', icon: 'info' },
        { id: 'separator', separator: true },
        { id: 'copy', label: '复制', icon: 'content_copy' },
        { id: 'cut', label: '剪切', icon: 'content_cut' },
        { id: 'rename', label: '重命名', icon: 'drive_file_rename_outline' },
        { id: 'fav', label: app.favorites.includes(e.path) ? '取消收藏' : '收藏', icon: 'star' },
        { id: 'separator2', separator: true },
        { id: 'copypath', label: '复制路径', icon: 'content_paste' },
        { id: 'delete', label: '删除', icon: 'delete', danger: true },
      ].filter(Boolean) as MenuItem[]}
    />
  );
}

/* ------------------------------ browser view ------------------------------ */

export function BrowserView() {
  const app = useApp();
  const wc = app.listing.length;
  const visible = app.activeTab.filter
    ? app.listing.filter((e) => e.name.toLowerCase().includes(app.activeTab.filter.toLowerCase()))
    : app.listing;

  if (app.listingBusy && visible.length === 0) {
    return <div className="skeleton-grid">{Array.from({ length: 12 }).map((_, i) => <div key={i} className="skeleton-card" />)}</div>;
  }
  if (visible.length === 0) {
    return (
      <div className="empty">
        <Icon name="folder_open" size={56} />
        <p className="empty__title">{app.activeTab.filter ? '没有匹配的文件' : '这里还没有文件'}</p>
        <p className="empty__desc">{wc === 0 ? '可以新建文件夹，或粘贴内容到这里' : '试试修改筛选词'}</p>
      </div>
    );
  }

  if (app.settings.viewMode === 'grid') {
    return (
      <div className="grid">
        {visible.map((e) => <FileCard key={e.path} e={e} />)}
      </div>
    );
  }
  return (
    <div className="rows">
      {visible.map((e) => <FileRow key={e.path} e={e} />)}
    </div>
  );
}
