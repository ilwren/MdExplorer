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
import { DialogHost, Toasts } from './views/Dialogs';
import { IconButton } from './ui/widgets';

function MainContent() {
  const app = useApp();
  const loc = app.activeTab.location;
  switch (loc.kind) {
    case 'home': return <HomeView />;
    case 'settings': return <SettingsView />;
    case 'trash': return <TrashView />;
    default: return <BrowserView />;
  }
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

  // Folders: no floating preview shell; selection drives preview.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Delete' && app.activeTab.selection.length > 0 && !app.dialog) {
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
          {compact && <CompactTopBar onOpenDrawer={() => setDrawerOpen(true)} />}
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
          {compact && app.clipboard && (
            <div className="minitask">
              <span>{app.clipboard.paths.length} 项待粘贴</span>
              <button onClick={() => app.navigate(app.activeTab.location.kind === 'dir' ? app.activeTab.location : { kind: 'home' })}>前往粘贴</button>
            </div>
          )}
        </main>
      </div>

      {compact && <BottomNav />}

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
