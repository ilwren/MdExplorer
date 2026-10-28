import React, { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api';
import { Icon } from '../ui/icons';
import { Button, Chip, IconButton, Menu, TextField, type MenuItem } from '../ui/widgets';
import { locTitle, useApp, type Location, type TabState } from '../state/app';
import { CATEGORIES, formatSize } from '../ui/kinds';

/* ------------------------------ breakpoints ------------------------------ */

export type WidthClass = 'compact' | 'medium' | 'expanded' | 'large' | 'xlarge';

export function widthClassOf(w: number): WidthClass {
  if (w < 600) return 'compact';
  if (w < 840) return 'medium';
  if (w < 1200) return 'expanded';
  if (w < 1600) return 'large';
  return 'xlarge';
}

export function useWidthClass(): WidthClass {
  const [w, setW] = useState(() => window.innerWidth);
  useEffect(() => {
    const on = () => setW(window.innerWidth);
    window.addEventListener('resize', on);
    return () => window.removeEventListener('resize', on);
  }, []);
  return widthClassOf(w);
}

/* ------------------------------ nav destinations ------------------------------ */

interface NavItem { loc: Location; label: string; icon: string }

const MAIN_NAV: NavItem[] = [
  { loc: { kind: 'home' }, label: '主页', icon: 'home' },
  { loc: { kind: 'favorites' }, label: '收藏', icon: 'star' },
  { loc: { kind: 'recent' }, label: '最近', icon: 'schedule' },
  { loc: { kind: 'category', cat: 'image' }, label: '图片', icon: 'image' },
  { loc: { kind: 'category', cat: 'video' }, label: '视频', icon: 'movie' },
  { loc: { kind: 'category', cat: 'doc' }, label: '文档', icon: 'description' },
  { loc: { kind: 'trash' }, label: '回收站', icon: 'delete' },
  { loc: { kind: 'searchcenter' }, label: '搜索中心', icon: 'manage_search' },
  { loc: { kind: 'insights' }, label: '存储洞察', icon: 'data_usage' },
];

const BOTTOM_NAV: NavItem[] = [
  { loc: { kind: 'home' }, label: '主页', icon: 'home' },
  { loc: { kind: 'recent' }, label: '最近', icon: 'schedule' },
  { loc: { kind: 'favorites' }, label: '收藏', icon: 'star' },
  { loc: { kind: 'settings' }, label: '设置', icon: 'settings' },
];

function isSameLoc(a: Location, b: Location): boolean {
  return a.kind === b.kind && (a.kind !== 'dir' || (a as { path: string }).path === (b as { path: string }).path)
    && (a.kind !== 'category' || (a as { cat: string }).cat === (b as { cat: string }).cat);
}

/* ------------------------------ drawer / rail / bottom nav ------------------------------ */

export function DrawerContent({ onNavigate }: { onNavigate?: () => void }) {
  const app = useApp();
  return (
    <div className="drawer__content">
      <div className="brand">
        <img src="/eidoria-icon.png" alt="" className="brand__icon" />
        <div className="brand__text">
          <span className="brand__name">Eidoria</span>
          <span className="brand__sub">万象屿</span>
        </div>
      </div>

      <NewButton />

      <nav className="drawer__nav">
        {MAIN_NAV.map((it) => (
          <button
            key={it.label}
            className={`navitem ${isSameLoc(app.activeTab.location, it.loc) && !(it.loc.kind === 'home' && app.activeTab.location.kind !== 'home') ? 'is-active' : ''}`}
            onClick={() => { app.navigate(it.loc); onNavigate?.(); }}
          >
            <Icon name={it.icon} size={21} />
            <span>{it.label}</span>
          </button>
        ))}
      </nav>

      <div className="drawer__section-title">存储区</div>
      <div className="volumes">
        {app.roots.map((r) => {
          const pct = r.total > 0 ? Math.round((r.used / r.total) * 100) : 0;
          return (
            <button
              key={r.path}
              className="volume"
              onClick={() => { app.navigate({ kind: 'dir', path: r.path }); onNavigate?.(); }}
              title={r.path}
            >
              <Icon name="storage" size={20} className="volume__icon" />
              <span className="volume__body">
                <span className="volume__row">
                  <span className="volume__name">{r.name}</span>
                  <span className="volume__pct">{pct}%</span>
                </span>
                <span className="volume__bar"><span style={{ width: `${Math.max(3, pct)}%` }} /></span>
              </span>
            </button>
          );
        })}
      </div>

      <div className="drawer__spacer" />
      <button
        className={`navitem ${app.activeTab.location.kind === 'settings' ? 'is-active' : ''}`}
        onClick={() => { app.navigate({ kind: 'settings' }); onNavigate?.(); }}
      >
        <Icon name="settings" size={21} />
        <span>设置</span>
      </button>
    </div>
  );
}

export function NewButton() {
  const app = useApp();
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const dir = app.activeTab.location.kind === 'dir' ? app.activeTab.location.path : null;
  return (
    <>
      <button
        className="newbtn"
        disabled={!dir}
        title={dir ? '新建' : '进入文件夹后可新建'}
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setMenu({ x: r.left, y: r.bottom + 6 });
        }}
      >
        <Icon name="add" size={20} />
        <span>新建</span>
      </button>
      {menu && (
        <Menu
          x={menu.x} y={menu.y}
          onClose={() => setMenu(null)}
          onPick={(id) => {
            if (id === 'folder') app.setDialog({ kind: 'newfolder', dir: dir! });
            if (id === 'file') app.setDialog({ kind: 'newfile', dir: dir! });
          }}
          items={[
            { id: 'folder', label: '新建文件夹', icon: 'create_new_folder' },
            { id: 'file', label: '新建文件', icon: 'note_add' },
          ]}
        />
      )}
    </>
  );
}

export function Rail() {
  const app = useApp();
  return (
    <aside className="rail">
      <div className="rail__fab"><NewButton /></div>
      <nav className="rail__nav">
        {MAIN_NAV.map((it) => (
          <button
            key={it.label}
            className={`railitem ${isSameLoc(app.activeTab.location, it.loc) ? 'is-active' : ''}`}
            onClick={() => app.navigate(it.loc)}
            title={it.label}
          >
            <Icon name={it.icon} size={22} />
            <span className="railitem__label">{it.label}</span>
          </button>
        ))}
        <button
          className={`railitem ${app.activeTab.location.kind === 'settings' ? 'is-active' : ''}`}
          onClick={() => app.navigate({ kind: 'settings' })}
          title="设置"
        >
          <Icon name="settings" size={22} />
          <span className="railitem__label">设置</span>
        </button>
      </nav>
    </aside>
  );
}

export function BottomNav() {
  const app = useApp();
  return (
    <nav className="bottomnav">
      {BOTTOM_NAV.map((it) => {
        const active = isSameLoc(app.activeTab.location, it.loc)
          || (it.loc.kind === 'home' && app.activeTab.location.kind === 'dir');
        return (
          <button key={it.label} className={`bottomnav__item ${active ? 'is-active' : ''}`} onClick={() => app.navigate(it.loc)}>
            <span className="bottomnav__icon"><Icon name={it.icon} size={23} /></span>
            <span className="bottomnav__label">{it.label}</span>
          </button>
        );
      })}
    </nav>
  );
}

/* ------------------------------ tab strip ------------------------------ */

export function TabStrip() {
  const app = useApp();
  return (
    <div className="tabstrip">
      <div className="tabstrip__shelf">
        {app.tabs.map((t: TabState) => {
          const active = t.id === app.activeTab.id;
          return (
            <div key={t.id} className={`tab ${active ? 'is-active' : ''}`} onClick={() => app.setActiveTab(t.id)}>
              <Icon name={tabIcon(t.location)} size={16} />
              <span className="tab__title">{locTitle(t.location)}</span>
              <button
                className="tab__close"
                title="关闭标签页"
                onClick={(e) => { e.stopPropagation(); app.closeTab(t.id); }}
              >
                <Icon name="close" size={14} />
              </button>
            </div>
          );
        })}
        <button className="tabstrip__add" title="新建标签页" onClick={() => app.newTab()}>
          <Icon name="add" size={18} />
        </button>
      </div>
    </div>
  );
}

function tabIcon(l: Location): string {
  switch (l.kind) {
    case 'home': return 'home';
    case 'trash': return 'delete';
    case 'settings': return 'settings';
    case 'recent': return 'schedule';
    case 'favorites': return 'star';
    case 'category': return CATEGORIES.find((c) => c.id === (l as { cat: string }).cat)?.icon ?? 'folder';
    default: return 'folder';
  }
}

/* ------------------------------ breadcrumb ------------------------------ */

export function Breadcrumb({ path, compact }: { path: string; compact?: boolean }) {
  const app = useApp();
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(path);

  const root = app.roots.find((r) => path === r.path || path.startsWith(r.path + '/'));
  const segments = useMemo(() => {
    const rel = root ? path.slice(root.path.length) : path;
    const parts = rel.split('/').filter(Boolean);
    const segs: { label: string; path: string }[] = [];
    if (root) segs.push({ label: root.name, path: root.path });
    let cur = root ? root.path : '';
    for (const p of parts) {
      cur = cur ? `${cur}/${p}` : p;
      segs.push({ label: p, path: cur });
    }
    return segs;
  }, [path, root]);

  const shown = compact && segments.length > 2 ? [segments[0], segments[segments.length - 1]] : segments;

  if (editing) {
    return (
      <div className="breadcrumb breadcrumb--edit">
        <input
          autoFocus
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key !== 'Escape') e.stopPropagation();
            if (e.key === 'Enter') { app.navigate({ kind: 'dir', path: text }); setEditing(false); }
            if (e.key === 'Escape') setEditing(false);
          }}
          onBlur={() => setEditing(false)}
        />
      </div>
    );
  }

  return (
    <div
      className="breadcrumb"
      title={path}
      onClick={(e) => {
        if (e.target === e.currentTarget) { setText(path); setEditing(true); }
      }}
    >
      {shown.map((s, i) => (
        <React.Fragment key={s.path}>
          {(i > 0 || compact) && <Icon name="chevron_right" size={16} className="breadcrumb__sep" />}
          <button
            className="breadcrumb__seg"
            onClick={(e) => { e.stopPropagation(); app.navigate({ kind: 'dir', path: s.path }); }}
          >
            {s.label}
          </button>
        </React.Fragment>
      ))}
    </div>
  );
}

/* ------------------------------ nav row ------------------------------ */

export function NavRow({ tab }: { tab?: TabState } = {}) {
  const app = useApp();
  const wc = useWidthClass();
  const [overflow, setOverflow] = useState<{ x: number; y: number } | null>(null);
  const t = tab ?? app.activeTab;
  const loc = t.location;
  const dirPath = loc.kind === 'dir' ? loc.path : null;

  return (
    <div className="navrow">
      <div className="navrow__history">
        <IconButton name="arrow_back" title="后退" onClick={app.back} disabled={t.histIndex <= 0} />
        <IconButton name="arrow_forward" title="前进" onClick={app.forward} disabled={t.histIndex >= t.history.length - 1} />
        <IconButton name="arrow_upward" title="上一级" onClick={app.up} disabled={loc.kind === 'home'} />
        <IconButton name="refresh" title="刷新" onClick={app.refresh} />
      </div>

      {loc.kind === 'zip' ? (
        <div className="breadcrumb" title={loc.path}>
          <button className="breadcrumb__seg" onClick={() => app.navigate({ kind: 'zip', path: loc.path, inner: '' })}>
            <Icon name="folder_zip" size={15} style={{ display: 'inline-block', verticalAlign: -2, marginRight: 4 }} />
            {loc.path.split('/').pop()}
          </button>
          {loc.inner.split('/').filter(Boolean).map((seg, i, arr) => (
            <React.Fragment key={i}>
              <Icon name="chevron_right" size={16} className="breadcrumb__sep" />
              <button
                className="breadcrumb__seg"
                onClick={() => app.navigate({ kind: 'zip', path: loc.path, inner: arr.slice(0, i + 1).join('/') })}
              >
                {seg}
              </button>
            </React.Fragment>
          ))}
        </div>
      ) : dirPath ? (
        <Breadcrumb path={dirPath} compact={wc === 'compact'} />
      ) : (
        <div className="breadcrumb"><span className="breadcrumb__seg">{locTitle(loc)}</span></div>
      )}

      <div className="navrow__right">
        <IconButton
          name="search"
          variant="search"
          title="搜索（Ctrl+K）"
          onClick={() => app.setSearch({ open: true })}
        />
        <IconButton
          name="more_vert"
          title="更多"
          onClick={(e) => {
            const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
            setOverflow({ x: r.right - 200, y: r.bottom + 6 });
          }}
        />
      </div>

      {overflow && (
        <Menu
          x={overflow.x} y={overflow.y}
          onClose={() => setOverflow(null)}
          onPick={(id) => {
            if (id === 'hidden') app.patchSettings({ showHidden: !app.settings.showHidden });
            if (id === 'selectall') app.selectAll();
            if (id === 'settings') app.navigate({ kind: 'settings' });
            if (id === 'newtab') app.newTab(loc.kind === 'dir' ? loc : { kind: 'home' });
            if (id === 'fav' && dirPath) app.toggleFavorite(dirPath);
          }}
          items={[
            { id: 'newtab', label: '在新标签页打开', icon: 'filter_none' },
            dirPath ? { id: 'fav', label: app.favorites.includes(dirPath) ? '取消收藏' : '收藏此文件夹', icon: 'star' } : null,
            { id: 'selectall', label: '全选', icon: 'select_all' },
            { id: 'hidden', label: app.settings.showHidden ? '隐藏系统文件' : '显示系统文件', icon: 'visibility' },
            { id: 'separator', separator: true },
            { id: 'settings', label: '设置', icon: 'settings' },
          ].filter(Boolean) as MenuItem[]}
        />
      )}
    </div>
  );
}

/* ------------------------------ toolbar ------------------------------ */

export function Toolbar() {
  const app = useApp();
  const sel = app.activeTab.selection;
  const loc = app.activeTab.location;
  const isZip = loc.kind === 'zip';
  const dirPath = loc.kind === 'dir' ? loc.path : null;
  const canPaste = !!app.clipboard && !!dirPath;
  const one = sel.length === 1 ? sel[0] : null;
  const [sortMenu, setSortMenu] = useState<{ x: number; y: number } | null>(null);
  const [viewMenu, setViewMenu] = useState<{ x: number; y: number } | null>(null);
  const [batchOpen, setBatchOpen] = useState(false);

  if (isZip) {
    const zipPath = loc.path;
    return (
      <div className="toolbar">
        <IconButton
          name="unarchive"
          title="解压所选"
          disabled={!sel.length}
          onClick={() => app.setDialog({
            kind: 'extract', path: zipPath,
            entries: app.listing.filter((x) => sel.includes(x.path)),
          })}
        />
        <IconButton
          name="folder_zip"
          title="解压全部"
          onClick={() => app.setDialog({ kind: 'extract', path: zipPath, entries: null })}
        />
        <IconButton
          name="fact_check"
          title="任务中心"
          onClick={() => app.setTaskPanelOpen(true)}
        />
        <IconButton
          name="vertical_split"
          title="双窗格"
          onClick={app.toggleDualPane}
          className={app.dualPane ? 'is-active' : ''}
        />
        <div className="toolbar__gap" />
        <IconButton
          name="sort" title="排序"
          onClick={(e) => {
            const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
            setSortMenu({ x: r.left, y: r.bottom + 6 });
          }}
        />
        {sortMenu && (
          <Menu
            x={sortMenu.x} y={sortMenu.y}
            onClose={() => setSortMenu(null)}
            onPick={(id) => {
              if (id === 'dir-toggle') app.patchSettings({ sortDir: app.settings.sortDir === 'asc' ? 'desc' : 'asc' });
              else app.patchSettings({ sortBy: id as 'name' | 'size' | 'mtime' | 'kind' });
            }}
            items={[
              { id: 'name', label: `名称${app.settings.sortBy === 'name' ? '  ✓' : ''}`, icon: 'sort_by_alpha' },
              { id: 'size', label: `大小${app.settings.sortBy === 'size' ? '  ✓' : ''}`, icon: 'data_usage' },
              { id: 'mtime', label: `修改时间${app.settings.sortBy === 'mtime' ? '  ✓' : ''}`, icon: 'schedule' },
              { id: 'dir-toggle', label: app.settings.sortDir === 'asc' ? '改为降序' : '改为升序', icon: 'swap_vert' },
            ]}
          />
        )}
      </div>
    );
  }

  return (
    <div className="toolbar">
      <IconButton name="create_new_folder" title="新建文件夹" disabled={!dirPath} onClick={() => app.setDialog({ kind: 'newfolder', dir: dirPath! })} />
      <IconButton name="content_cut" title="剪切" disabled={!sel.length} onClick={() => app.setClipboard({ op: 'cut', paths: sel })} />
      <IconButton name="content_copy" title="复制" disabled={!sel.length} onClick={() => app.setClipboard({ op: 'copy', paths: sel })} />
      <IconButton name="content_paste" title="粘贴" disabled={!canPaste} onClick={() => doPaste()} />
      <IconButton name="drive_file_rename_outline" title="重命名" disabled={!one} onClick={() => one && app.setDialog({ kind: 'rename', path: one, name: one.split('/').pop()! })} />
      <IconButton name="delete" title="删除" disabled={!sel.length} onClick={() => app.setDialog({ kind: 'confirm-delete', paths: sel })} />
      <IconButton name="edit_note" title="批量重命名" disabled={!sel.length} onClick={() => setBatchOpen(true)} />
      <IconButton name="fact_check" title="任务中心" onClick={() => app.setTaskPanelOpen(true)} />
      <IconButton
        name="vertical_split"
        title="双窗格"
        onClick={app.toggleDualPane}
        className={app.dualPane ? 'is-active' : ''}
      />
      <div className="toolbar__gap" />
      <IconButton
        name="sort" title="排序"
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setSortMenu({ x: r.left, y: r.bottom + 6 });
        }}
      />
      <IconButton
        name={app.settings.viewMode === 'grid' ? 'grid_view' : 'view_list'}
        title="视图"
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setViewMenu({ x: r.left, y: r.bottom + 6 });
        }}
      />

      {sortMenu && (
        <Menu
          x={sortMenu.x} y={sortMenu.y}
          onClose={() => setSortMenu(null)}
          onPick={(id) => {
            if (id === 'dir-toggle') app.patchSettings({ sortDir: app.settings.sortDir === 'asc' ? 'desc' : 'asc' });
            else app.patchSettings({ sortBy: id as 'name' | 'size' | 'mtime' | 'kind' });
          }}
          items={[
            { id: 'name', label: `名称${app.settings.sortBy === 'name' ? '  ✓' : ''}`, icon: 'sort_by_alpha' },
            { id: 'size', label: `大小${app.settings.sortBy === 'size' ? '  ✓' : ''}`, icon: 'data_usage' },
            { id: 'mtime', label: `修改时间${app.settings.sortBy === 'mtime' ? '  ✓' : ''}`, icon: 'schedule' },
            { id: 'kind', label: `类型${app.settings.sortBy === 'kind' ? '  ✓' : ''}`, icon: 'category' },
            { id: 'separator', separator: true },
            { id: 'dir-toggle', label: app.settings.sortDir === 'asc' ? '改为降序' : '改为升序', icon: 'swap_vert' },
          ]}
        />
      )}
      {viewMenu && (
        <Menu
          x={viewMenu.x} y={viewMenu.y}
          onClose={() => setViewMenu(null)}
          onPick={(id) => app.patchSettings({ viewMode: id as 'grid' | 'list' })}
          items={[
            { id: 'grid', label: `网格${app.settings.viewMode === 'grid' ? '  ✓' : ''}`, icon: 'grid_view' },
            { id: 'list', label: `列表${app.settings.viewMode === 'list' ? '  ✓' : ''}`, icon: 'view_list' },
          ]}
        />
      )}
      {batchOpen && <BatchRenameDialog onClose={() => setBatchOpen(false)} />}
    </div>
  );

  async function doPaste() {
    const cb = app.clipboard!;
    await app.runTransfer(cb.op === 'cut', cb.paths, dirPath!);
    app.setTaskPanelOpen(true);
  }
}

export function BatchRenameDialog({ onClose }: { onClose: () => void }) {
  const app = useApp();
  const [find, setFind] = useState('');
  const [replace, setReplace] = useState('');
  return (
    <DialogLite
      title={`批量重命名（已选 ${app.activeTab.selection.length} 项）`}
      onClose={onClose}
      actions={
        <>
          <Button variant="text" onClick={onClose}>取消</Button>
          <Button
            onClick={async () => {
              if (!find) { onClose(); return; }
              let n = 0;
              for (const p of app.activeTab.selection) {
                const name = p.split('/').pop()!;
                const next = name.split(find).join(replace);
                if (next !== name) { await api.rename(p, next); n++; }
              }
              app.refresh();
              app.toast(`已重命名 ${n} 项`);
              onClose();
            }}
          >
            应用
          </Button>
        </>
      }
    >
      <TextField label="查找" value={find} onChange={setFind} autoFocus />
      <TextField label="替换为" value={replace} onChange={setReplace} onEnter={() => {}} />
    </DialogLite>
  );
}

function DialogLite(props: { title: string; children: React.ReactNode; actions: React.ReactNode; onClose: () => void }) {
  return (
    <div className="scrim" onMouseDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="dialog" style={{ width: 420 }} role="dialog" aria-modal="true">
        <h2 className="dialog__title">{props.title}</h2>
        <div className="dialog__body">{props.children}</div>
        <div className="dialog__actions">{props.actions}</div>
      </div>
    </div>
  );
}

/* ------------------------------ status bar ------------------------------ */

export function StatusBar() {
  const app = useApp();
  const sel = app.activeTab.selection;
  const dirPath = app.activeTab.location.kind === 'dir' ? app.activeTab.location.path : null;
  const root = app.roots.find((r) => dirPath && (dirPath === r.path || dirPath.startsWith(r.path + '/')));
  return (
    <div className="statusbar">
      <span>{app.listing.length} 项</span>
      {sel.length > 0 && <span>已选 {sel.length} 项</span>}
      {root && <span>{root.name} 剩余 {formatSize(root.free)}</span>}
      <div className="statusbar__gap" />
      {app.clipboard && <Chip icon={app.clipboard.op === 'cut' ? 'content_cut' : 'content_copy'}>{app.clipboard.paths.length} 项待粘贴</Chip>}
      <Chip icon="storage">{app.listingBusy ? '读取中…' : '就绪'}</Chip>
    </div>
  );
}

/* ------------------------------ compact top bar ------------------------------ */

export function CompactTopBar({ onOpenDrawer }: { onOpenDrawer: () => void }) {
  const app = useApp();
  const [tabsMenu, setTabsMenu] = useState<{ x: number; y: number } | null>(null);
  const [overflow, setOverflow] = useState<{ x: number; y: number } | null>(null);
  return (
    <div className="ctopbar">
      <IconButton name="menu" title="导航" variant="plain" onClick={onOpenDrawer} />
      <span className="ctopbar__title">Eidoria</span>
      <div className="ctopbar__gap" />
      <IconButton
        name="layers"
        title="标签页"
        variant="plain"
        badge={app.tabs.length}
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setTabsMenu({ x: Math.max(8, r.right - 220), y: r.bottom + 6 });
        }}
      />
      <IconButton name="search" title="搜索" variant="plain" onClick={() => app.setSearch({ open: true })} />
      <IconButton
        name="more_vert"
        title="更多"
        variant="plain"
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setOverflow({ x: Math.max(8, r.right - 200), y: r.bottom + 6 });
        }}
      />
      {tabsMenu && (
        <Menu
          x={tabsMenu.x} y={tabsMenu.y}
          onClose={() => setTabsMenu(null)}
          onPick={(id) => {
            if (id === 'new') app.newTab();
            else app.setActiveTab(id);
          }}
          items={[
            ...app.tabs.map((t) => ({ id: t.id, label: locTitle(t.location), icon: tabIcon(t.location) })),
            { id: 'separator', separator: true },
            { id: 'new', label: '新建标签页', icon: 'add' },
          ]}
        />
      )}
      {overflow && (
        <Menu
          x={overflow.x} y={overflow.y}
          onClose={() => setOverflow(null)}
          onPick={(id) => {
            if (id === 'hidden') app.patchSettings({ showHidden: !app.settings.showHidden });
            if (id === 'selectall') app.selectAll();
            if (id === 'settings') app.navigate({ kind: 'settings' });
          }}
          items={[
            { id: 'selectall', label: '全选', icon: 'select_all' },
            { id: 'hidden', label: app.settings.showHidden ? '隐藏系统文件' : '显示系统文件', icon: 'visibility' },
            { id: 'settings', label: '设置', icon: 'settings' },
          ]}
        />
      )}
    </div>
  );
}
