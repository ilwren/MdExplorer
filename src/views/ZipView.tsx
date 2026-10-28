import React, { useEffect, useMemo, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp, sortEntries } from '../state/app';
import { Icon } from '../ui/icons';
import { IconWell, Menu, type MenuItem } from '../ui/widgets';
import { formatSize, formatTime, kindMeta } from '../ui/kinds';

/** Archive workbench (concept §4.4): zip as read-only virtual folder. */
export function ZipView() {
  const app = useApp();
  const loc = app.activeTab.location;
  const zipPath = loc.kind === 'zip' ? loc.path : '';
  const inner = loc.kind === 'zip' ? loc.inner : '';
  const [tree, setTree] = useState<FsEntry[]>([]);
  const [treeOpen, setTreeOpen] = useState(true);

  useEffect(() => {
    let alive = true;
    api.zip(zipPath).then((r) => alive && setTree(r.entries)).catch(() => alive && setTree([]));
    return () => { alive = false; };
  }, [zipPath]);

  const entries = app.listing;
  const visible = app.activeTab.filter
    ? entries.filter((e) => e.name.toLowerCase().includes(app.activeTab.filter.toLowerCase()))
    : entries;

  const dirTree = useMemo(() => buildTree(tree), [tree]);

  return (
    <div className="zipwork">
      {treeOpen && (
        <aside className="ziptree">
          <button
            className={`ziptree__node ${inner === '' ? 'is-active' : ''}`}
            onClick={() => app.navigate({ kind: 'zip', path: zipPath, inner: '' }, { push: false })}
          >
            <Icon name="folder_zip" size={16} />
            <span className="ziptree__label">根目录</span>
          </button>
          <TreeNode nodes={dirTree} depth={1} current={inner} onPick={(p) => app.navigate({ kind: 'zip', path: zipPath, inner: p }, { push: false })} />
        </aside>
      )}

      <div className="zipmain">
        <div className="zipmain__head">
          <button className="zipmain__treetoggle" onClick={() => setTreeOpen((v) => !v)}>
            <Icon name={treeOpen ? 'chevron_left' : 'chevron_right'} size={18} />
            <span>目录树</span>
          </button>
          <span className="zipmain__hint">
            <Icon name="info" size={15} />
            压缩包为只读虚拟文件夹；单个条目可直接预览，无需先整包解压
          </span>
        </div>

        {visible.length === 0 ? (
          <div className="empty">
            <Icon name="folder_open" size={48} />
            <p className="empty__title">此目录为空</p>
          </div>
        ) : (
          <div className="ztable" role="table">
            <div className="ztable__row ztable__row--head" role="row">
              <span>名称</span><span>原始大小</span><span>压缩后</span><span>修改时间</span>
            </div>
            {visible.map((e) => (
              <ZipRow key={e.path} e={e} />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

interface ZipTreeNode { name: string; path: string; children: ZipTreeNode[] }

function buildTree(entries: FsEntry[]): ZipTreeNode[] {
  const root: ZipTreeNode[] = [];
  for (const e of entries) {
    // full entry path inside the zip (folders may be implicit -> derive from file paths)
    const parts = (e.name || '').split('/').filter(Boolean);
    if (!parts.length) continue;
    if (!e.isDir) parts.pop();
    let level = root;
    let acc = '';
    for (const p of parts) {
      acc = acc ? `${acc}/${p}` : p;
      let node = level.find((n) => n.name === p);
      if (!node) {
        node = { name: p, path: acc, children: [] };
        level.push(node);
      }
      level = node.children;
    }
  }
  return root;
}

function TreeNode({ nodes, depth, current, onPick }: {
  nodes: ZipTreeNode[];
  depth: number; current: string; onPick: (p: string) => void;
}) {
  return (
    <>
      {nodes.map((n) => (
        <React.Fragment key={n.path}>
          <button
            className={`ziptree__node ${current === n.path ? 'is-active' : ''}`}
            style={{ paddingLeft: 10 + depth * 14 }}
            onClick={() => onPick(n.path)}
          >
            <Icon name="folder" size={15} />
            <span className="ziptree__label">{n.name}</span>
          </button>
          <TreeNode nodes={n.children} depth={depth + 1} current={current} onPick={onPick} />
        </React.Fragment>
      ))}
    </>
  );
}

function ZipRow({ e }: { e: FsEntry }) {
  const app = useApp();
  const selected = app.activeTab.selection.includes(e.path);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const meta = kindMeta(e.kind);
  return (
    <div
      className={`ztable__row ${selected ? 'is-selected' : ''}`}
      role="row"
      onClick={(ev) => {
        if (ev.shiftKey) app.selectRange(e.path);
        else if (ev.ctrlKey || ev.metaKey) app.select(e.path, 'toggle');
        else { app.select(e.path); if (!e.isDir) app.setPreviewTarget(e); }
      }}
      onDoubleClick={() => app.openEntry(e)}
      onContextMenu={(ev) => {
        ev.preventDefault();
        if (!app.activeTab.selection.includes(e.path)) app.select(e.path);
        setMenu({ x: ev.clientX, y: ev.clientY });
      }}
    >
      <span className="ztable__name">
        {selected ? (
          <span className="filerow__check is-on"><Icon name="check" size={15} /></span>
        ) : e.isDir ? (
          <Icon name="folder" size={19} className="ztable__folder" />
        ) : (
          <IconWell bg={meta.wellBg} fg={meta.wellFg} icon={meta.icon} size={26} radius={8} />
        )}
        <span className="ztable__label">{e.name}</span>
      </span>
      <span>{e.isDir ? '' : formatSize(e.size)}</span>
      <span>{e.isDir || e.compressedSize === undefined ? '' : formatSize(e.compressedSize)}</span>
      <span>{e.mtime ? formatTime(e.mtime) : ''}</span>
      {menu && (
        <Menu
          x={menu.x} y={menu.y}
          onClose={() => setMenu(null)}
          onPick={(id) => {
            const sel = app.activeTab.selection.includes(e.path) ? app.activeTab.selection : [e.path];
            const selEntries = app.listing.filter((x) => sel.includes(x.path));
            if (id === 'open') app.openEntry(e);
            if (id === 'extract-one') app.setDialog({ kind: 'extract', path: app.activeTab.location.kind === 'zip' ? app.activeTab.location.path : '', entries: selEntries });
            if (id === 'extract-all') app.setDialog({ kind: 'extract', path: app.activeTab.location.kind === 'zip' ? app.activeTab.location.path : '', entries: null });
            if (id === 'copyname') navigator.clipboard?.writeText(e.name).then(() => app.toast('已复制名称'));
          }}
          items={[
            { id: 'open', label: e.isDir ? '打开' : '预览', icon: e.isDir ? 'folder_open' : 'preview' },
            { id: 'extract-one', label: '解压所选', icon: 'unarchive' },
            { id: 'extract-all', label: '解压全部', icon: 'folder_zip' },
            { id: 'separator', separator: true },
            { id: 'copyname', label: '复制名称', icon: 'content_paste' },
          ] as MenuItem[]}
        />
      )}
    </div>
  );
}
