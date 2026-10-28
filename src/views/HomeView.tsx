import React, { useEffect, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { IconWell } from '../ui/widgets';
import { CATEGORIES, formatSize, kindMeta } from '../ui/kinds';

export function HomeView() {
  const app = useApp();
  return (
    <div className="home">
      <section className="home__storage">
        {app.roots.map((r) => {
          const pct = r.total > 0 ? Math.round((r.used / r.total) * 100) : 0;
          return (
            <button key={r.path} className="storagecard" onClick={() => app.navigate({ kind: 'dir', path: r.path })}>
              <span className="storagecard__well"><Icon name="storage" size={24} /></span>
              <span className="storagecard__body">
                <span className="storagecard__top">
                  <span className="storagecard__name">{r.name}</span>
                  <span className="storagecard__pct">{pct}%</span>
                </span>
                <span className="storagecard__bar"><span style={{ width: `${Math.max(3, pct)}%` }} /></span>
                <span className="storagecard__sub">已用 {formatSize(r.used)} · 共 {formatSize(r.total)}</span>
              </span>
            </button>
          );
        })}
      </section>

      <section className="home__cats">
        {CATEGORIES.map((c) => (
          <button key={c.id} className="catcard" onClick={() => app.navigate({ kind: 'category', cat: c.id })}>
            <IconWell bg="var(--cat-well)" fg="var(--on-surface-variant)" icon={c.icon} size={52} radius={16} />
            <span>{c.label}</span>
          </button>
        ))}
      </section>

      {app.favorites.length > 0 && (
        <section className="home__section">
          <h3 className="home__title">收藏</h3>
          <div className="home__chips">
            {app.favorites.slice(0, 12).map((p) => (
              <button key={p} className="favchip" onClick={() => app.navigate({ kind: 'dir', path: p })}>
                <Icon name="folder" size={16} />
                <span>{p.split('/').pop()}</span>
              </button>
            ))}
          </div>
        </section>
      )}

      <section className="home__section">
        <h3 className="home__title">最近</h3>
        <RecentList />
      </section>

      <section className="home__section">
        <h3 className="home__title">快速入口</h3>
        <div className="home__chips">
          {app.roots.map((r) => (
            <button key={r.path} className="favchip" onClick={() => app.navigate({ kind: 'dir', path: r.path })}>
              <Icon name="storage" size={16} />
              <span>{r.path.split('/').pop() || r.path}</span>
            </button>
          ))}
          <button className="favchip" onClick={() => app.navigate({ kind: 'trash' })}>
            <Icon name="delete" size={16} /><span>回收站</span>
          </button>
        </div>
      </section>
    </div>
  );
}

export function RecentList() {
  const app = useApp();
  const [entries, setEntries] = useState<FsEntry[]>([]);
  useEffect(() => {
    let alive = true;
    Promise.all(app.recent.slice(0, 10).map((p) => api.stat(p).catch(() => null)))
      .then((rs) => alive && setEntries(rs.filter(Boolean) as FsEntry[]));
    return () => { alive = false; };
  }, [app.recent]);

  if (entries.length === 0) {
    return <p className="home__emptyhint">还没有访问记录，浏览过的文件夹会出现在这里。</p>;
  }
  return (
    <div className="rows rows--plain">
      {entries.map((e) => (
        <div key={e.path} className="filerow" onClick={() => app.openEntry(e)} onDoubleClick={() => app.openEntry(e)}>
          <IconWell bg={kindMeta(e.kind).wellBg} fg={kindMeta(e.kind).wellFg} icon={e.isDir ? 'folder' : kindMeta(e.kind).icon} size={38} radius={10} />
          <div className="filerow__name">{e.name}</div>
          <div className="filerow__meta">{e.path}</div>
        </div>
      ))}
    </div>
  );
}
