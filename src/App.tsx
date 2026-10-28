import React, { useEffect, useState } from 'react';
import { AppProvider, useApp } from './state/app';
import {
  BottomNav, CompactTopBar, DrawerContent, NavRow, Rail, StatusBar, TabStrip, Toolbar, useWidthClass,
} from './shell/Shell';
import { BrowserView } from './views/BrowserView';
import { HomeView } from './views/HomeView';
import { FloatingPreview, PreviewPane } from './views/Preview';
import { SearchPanel } from './views/SearchPanel';
import { SettingsView, TrashView } from './views/MiscViews';
import { InsightsView } from './views/InsightsView';
import { ZipView } from './views/ZipView';
import { SearchCenter } from './views/SearchCenter';
import { DualPane } from './views/DualPane';
import { DialogHost, Toasts } from './views/Dialogs';
import { MiniTaskBar, TaskPanel } from './views/TaskCenter';
import { IconButton, Menu, type MenuItem } from './ui/widgets';
import { Icon } from './ui/icons';

function MainContent() {
  const app = useApp();
  const loc = app.activeTab.location;
  if (app.dualPane && app.paneB && (loc.kind === 'dir' || loc.kind === 'zip')) {
    return <DualPane />;
  }
  switch (loc.kind) {
    case 'home': return <HomeView />;
    case 'settings': return <SettingsView />;
    case 'trash': return <TrashView />;
    case 'insights': return <InsightsView />;
    case 'searchcenter': return <SearchCenter />;
    case 'zip': return <ZipView />;
    default: return <BrowserView />;
  }
}

/** Compact FAB — 新建 (folder pages only), per responsive spec §2. */
function CompactFab() {
  const app = useApp();
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const loc = app.activeTab.location;
  const dir = loc.kind === 'dir' ? loc.path : null;
  if (!dir) return null;
  return (
    <>
      <button
        className="fab"
        title="新建"
        onClick={(e) => {
          const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
          setMenu({ x: Math.max(8, r.left - 120), y: r.top - 100 });
        }}
      >
        <Icon name="add" size={26} />
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
          ] as MenuItem[]}
        />
      )}
    </>
  );
}

/** Compact selection context bar (long-press/multi-select) per spec §2. */
function CompactSelectionBar() {
  const app = useApp();
  const sel = app.activeTab.selection;
  if (sel.length === 0) return null;
  return (
    <div className="ctxbar">
      <IconButton name="close" title="退出选择" variant="plain" onClick={() => app.clearSelection()} />
      <span className="ctxbar__count">已选 {sel.length}</span>
      <IconButton name="select_all" title="全选" variant="plain" onClick={app.selectAll} />
      <IconButton name="content_copy" title="复制" variant="plain" onClick={() => app.setClipboard({ op: 'copy', paths: sel })} />
      <IconButton name="content_cut" title="剪切" variant="plain" onClick={() => app.setClipboard({ op: 'cut', paths: sel })} />
      <IconButton name="delete" title="删除" variant="plain" onClick={() => app.setDialog({ kind: 'confirm-delete', paths: sel })} />
    </div>
  );
}

function Shell() {
  const app = useApp();
  const wc = useWidthClass();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const compact = wc === 'compact';
  const medium = wc === 'medium';
  const showTabs = wc === 'large' || wc === 'xlarge';
  const showPane = !compact && !medium && app.previewPaneOpen && !!app.previewTarget;
  const showFloat = (compact || medium) && !!app.previewTarget;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Delete' && app.activeTab.selection.length > 0 && !app.dialog && !app.conflict) {
        app.setDialog({ kind: 'confirm-delete', paths: app.activeTab.selection });
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a' && !app.dialog && !app.search.open) {
        const tag = (e.target as HTMLElement)?.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA') return;
        e.preventDefault();
        app.selectAll();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  return (
    <div className={`app-backdrop ${compact ? 'is-compact' : ''}`}>
      <div className={`app-window wc-${wc}`}>
        {medium && <Rail />}
        {!compact && !medium && (
          <aside className="drawer"><DrawerContent /></aside>
        )}

        <main className="main">
          {compact && app.activeTab.selection.length > 0 ? (
            <CompactSelectionBar />
          ) : compact ? (
            <CompactTopBar onOpenDrawer={() => setDrawerOpen(true)} />
          ) : null}
          {showTabs && <TabStrip />}
          <NavRow />
          {!compact && <Toolbar />}

          <div className="workspace-row">
            <div
              className="workspace"
              onClick={(e) => {
                if (e.target === e.currentTarget) { app.clearSelection(); app.setPreviewTarget(null); }
              }}
            >
              <MainContent />
            </div>
            {showPane && <PreviewPane />}
          </div>

          {!compact && <StatusBar />}
          <MiniTaskBar />
        </main>
      </div>

      {compact && <BottomNav />}
      {compact && <CompactFab />}

      {drawerOpen && (
        <div className="drawer-scrim" onMouseDown={(e) => e.target === e.currentTarget && setDrawerOpen(false)}>
          <aside className="drawer drawer--modal">
            <div className="drawer__close-row">
              <IconButton name="close" title="关闭导航" onClick={() => setDrawerOpen(false)} />
            </div>
            <DrawerContent onNavigate={() => setDrawerOpen(false)} />
          </aside>
        </div>
      )}

      {showFloat && <FloatingPreview />}
      <TaskPanel />
      <SearchPanel />
      <DialogHost />
      <Toasts />
    </div>
  );
}

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  );
}
