import React, { useEffect, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp, sortEntries, type TabState } from '../state/app';
import { Icon } from '../ui/icons';
import { Button } from '../ui/widgets';
import { FileCard } from './BrowserView';
import { NavRow } from '../shell/Shell';

/**
 * ExtraLarge 双窗格 (concept §5.1)：
 * A/B 各是完整 BrowserSession（面包屑、选集、滚动位置独立）；
 * 活动窗格主色描边 +「活动」标示；工具栏只作用于活动窗格；
 * 跨窗格拖放默认移动，拖影写明 Ctrl = 复制；跨窗格按钮收在预览侧栏专用区。
 */

function usePaneListing(tab: TabState | undefined, tick: number) {
  const app = useApp();
  const [entries, setEntries] = useState<FsEntry[]>([]);
  const [busy, setBusy] = useState(false);
  const loc = tab?.location;
  useEffect(() => {
    if (!loc) { setEntries([]); return; }
    let alive = true;
    setBusy(true);
    const done = (es: FsEntry[]) => {
      if (!alive) return;
      setEntries(sortEntries(es, app.settings));
      setBusy(false);
    };
    if (loc.kind === 'dir') {
      api.list(loc.path)
        .then((r) => done(r.entries.filter((e) => app.settings.showHidden || !e.name.startsWith('.'))))
        .catch(() => done([]));
    } else if (loc.kind === 'zip') {
      api.zip(loc.path, loc.inner)
        .then((r) => done(r.entries.map((e) => ({
          ...e,
          zipOf: loc.path,
          zipEntry: e.path,
          path: `${loc.path}\u0000${e.path}`,
        }))))
        .catch(() => done([]));
    } else {
      done([]);
    }
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loc, tick]);
  return { entries, busy };
}

function PaneView({ tabId, label }: { tabId: string; label: 'A' | 'B' }) {
  const app = useApp();
  const tab = app.tabs.find((t) => t.id === tabId);
  const active = app.activeTabId === tabId;
  const { entries, busy } = usePaneListing(tab, app.refreshTick);
  const [dropHover, setDropHover] = useState(false);
  if (!tab) return null;
  const loc = tab.location;
  const dirPath = loc.kind === 'dir' ? loc.path : loc.kind === 'zip' ? loc.path : null;

  return (
    <section
      className={`pane ${active ? 'is-active' : ''} ${dropHover ? 'is-drop' : ''}`}
      onMouseDownCapture={() => { if (!active) app.setActiveTab(tabId); }}
      onDragOver={(e) => {
        if (dirPath && e.dataTransfer.types.includes('text/plain')) {
          e.preventDefault();
          setDropHover(true);
        }
      }}
      onDragLeave={() => setDropHover(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDropHover(false);
        if (!dirPath) return;
        try {
          const paths = JSON.parse(e.dataTransfer.getData('text/plain')) as string[];
          if (Array.isArray(paths) && paths.length) {
            // default = move; Ctrl = copy (concept §5.1)
            void app.runTransfer(!e.ctrlKey, paths, dirPath);
          }
        } catch { /* ignore malformed payloads */ }
      }}
    >
      <div className="pane__head">
        <span className={`pane__badge ${active ? 'is-active' : ''}`}>{active ? '活动窗格' : `窗格 ${label}`}</span>
        <NavRow tab={tab} />
      </div>
      <div className="pane__body">
        {busy && entries.length === 0 ? (
          <div className="empty"><p className="empty__title">加载中…</p></div>
        ) : entries.length === 0 ? (
          <div className="empty">
            <Icon name="folder_open" size={42} />
            <p className="empty__title">{tab.filter ? '没有匹配的文件' : '这里还没有文件'}</p>
          </div>
        ) : (
          <div className="filegrid pane__grid">
            {entries
              .filter((e) => !tab.filter || e.name.toLowerCase().includes(tab.filter.toLowerCase()))
              .map((e) => <FileCard key={e.path} e={e} tab={tab} />)}
          </div>
        )}
      </div>
      {dropHover && (
        <div className="pane__drop">
          <Icon name="swap_horiz" size={18} />
          移动到此文件夹 · Ctrl = 复制
        </div>
      )}
    </section>
  );
}

export function DualPane() {
  const app = useApp();
  const paneB = app.paneB!;

  // if the active tab leaves the pair (tabstrip click), re-form the pair around it
  useEffect(() => {
    if (app.activeTabId !== app.paneA && app.activeTabId !== paneB) {
      app.setPaneA(app.activeTabId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [app.activeTabId, paneB]);

  const aId = app.paneA ?? app.activeTabId;
  return (
    <div className="dualpane">
      <PaneView tabId={aId} label="A" />
      <div className="dualpane__divider" aria-hidden />
      <PaneView tabId={paneB} label="B" />
    </div>
  );
}

/** Cross-pane actions — a dedicated zone inside the preview sidebar (concept §5.1). */
export function CrossPaneActions() {
  const app = useApp();
  if (!app.dualPane || !app.paneB) return null;
  const fromB = app.activeTabId === app.paneB;
  const otherId = fromB ? app.paneA : app.paneB;
  const other = app.tabs.find((t) => t.id === otherId);
  const sel = app.activeTab.selection;
  const otherLoc = other?.location;
  const destName = otherLoc
    ? otherLoc.kind === 'dir' ? otherLoc.path.split('/').pop() || otherLoc.path
    : otherLoc.kind === 'zip' ? otherLoc.path.split('/').pop() ?? '' : ''
    : '';
  return (
    <section className="crosspane">
      <h3 className="crosspane__title">
        <Icon name="vertical_split" size={16} />
        跨窗格动作
      </h3>
      <p className="conflict__meta">
        目标：窗格 {fromB ? 'A' : 'B'} · {destName || '非文件夹'}{otherLoc?.kind === 'dir' ? '' : '（仅文件夹可接收）'}
      </p>
      <div className="crosspane__row">
        <Button
          variant="filled"
          icon="content_copy"
          disabled={!sel.length || otherLoc?.kind !== 'dir'}
          onClick={() => void app.sendToOtherPane(false)}
        >
          复制到对侧（{sel.length}）
        </Button>
        <Button
          variant="tonal"
          icon="content_cut"
          disabled={!sel.length || otherLoc?.kind !== 'dir'}
          onClick={() => void app.sendToOtherPane(true)}
        >
          移动到对侧
        </Button>
      </div>
    </section>
  );
}
