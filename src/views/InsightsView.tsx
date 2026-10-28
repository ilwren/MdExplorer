import React, { useEffect, useMemo, useState } from 'react';
import { api, type Insights } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, Chip, IconWell } from '../ui/widgets';
import { formatSize, formatTime, kindMeta } from '../ui/kinds';

/** Storage insights (concept §4.2) — read-only review, never auto-clean. */
export function InsightsView() {
  const app = useApp();
  const [data, setData] = useState<Insights | null>(null);
  const [busy, setBusy] = useState(true);

  useEffect(() => {
    let alive = true;
    setBusy(true);
    api.insights().then((d) => { if (alive) { setData(d); setBusy(false); } }).catch(() => { if (alive) setBusy(false); });
    return () => { alive = false; };
  }, []);

  const kindRows = useMemo(() => data?.byKind ?? [], [data]);
  const maxKind = kindRows[0]?.bytes || 1;
  const treemap = useMemo(() => {
    if (!data) return [];
    // space map: top folders of each root + notable children
    return data.folders.filter((f) => f.bytes > 0).slice(0, 18);
  }, [data]);

  return (
    <div className="insights">
      <h2 className="page-title">存储洞察</h2>
      <p className="page-desc">
        只读复核视图：定位什么占用了空间。删除动作请回到文件列表逐项确认——这里不做一键清理。
        {data?.truncated && '（扫描受时间预算限制，结果为部分视图）'}
      </p>

      {busy && <div className="skeleton-grid">{Array.from({ length: 6 }).map((_, i) => <div key={i} className="skeleton-card" style={{ height: 96 }} />)}</div>}

      {data && !busy && (
        <>
          <section className="insights__stats">
            <div className="statcard">
              <span className="statcard__label">总用量</span>
              <span className="statcard__value">{formatSize(data.totalBytes)}</span>
              <span className="statcard__sub">{data.totalFiles} 个文件</span>
            </div>
            <div className="statcard">
              <span className="statcard__label">扫描目录</span>
              <span className="statcard__value">{data.folders.length}</span>
              <span className="statcard__sub">按体积排序</span>
            </div>
            <div className="statcard">
              <span className="statcard__label">最大类型</span>
              <span className="statcard__value">{kindMeta(kindRows[0]?.kind ?? 'other').label}</span>
              <span className="statcard__sub">{formatSize(kindRows[0]?.bytes)}</span>
            </div>
          </section>

          <section className="setcard">
            <h3 className="setcard__title">类型占比</h3>
            <div className="typebar">
              {kindRows.slice(0, 8).map((k) => (
                <div
                  key={k.kind}
                  className="typebar__seg"
                  style={{ flexGrow: Math.max(0.02, k.bytes / maxKind), background: kindMeta(k.kind).wellBg === 'var(--well-folder-bg)' ? 'var(--primary)' : kindMeta(k.kind).wellBg }}
                  title={`${kindMeta(k.kind).label} ${formatSize(k.bytes)}`}
                />
              ))}
            </div>
            <div className="typelegend">
              {kindRows.slice(0, 8).map((k) => (
                <Chip key={k.kind} icon={kindMeta(k.kind).icon}>
                  {kindMeta(k.kind).label} · {formatSize(k.bytes)} · {k.count}
                </Chip>
              ))}
            </div>
          </section>

          <section className="setcard">
            <h3 className="setcard__title">空间地图</h3>
            <div className="treemap">
              {treemap.map((f) => (
                <button
                  key={f.path}
                  className="treemap__block"
                  style={{ flexGrow: Math.max(1, Math.round((f.bytes / (treemap[0]?.bytes || 1)) * 100)) }}
                  onClick={() => app.navigate({ kind: 'dir', path: f.path })}
                  title={`${f.path} · ${formatSize(f.bytes)}`}
                >
                  <span className="treemap__name">{f.path.split('/').pop()}</span>
                  <span className="treemap__size">{formatSize(f.bytes)}</span>
                </button>
              ))}
            </div>
            <p className="confirm__hint">点击块定位到文件夹，不在图上直接放删除按钮。</p>
          </section>

          <section className="setcard">
            <h3 className="setcard__title">大文件（前 {Math.min(30, data.largeFiles.length)}）</h3>
            <div className="rows rows--plain">
              {data.largeFiles.slice(0, 30).map((f) => {
                const meta = kindMeta(f.kind);
                return (
                  <div key={f.path} className="filerow">
                    <IconWell bg={meta.wellBg} fg={meta.wellFg} icon={meta.icon} size={38} radius={10} />
                    <div className="filerow__name">
                      {f.name}
                      <div className="filerow__sub">{f.path.split('/').slice(0, -1).join('/')}</div>
                    </div>
                    <div className="filerow__meta">{formatSize(f.size)}</div>
                    <Button
                      variant="tonal"
                      icon="my_location"
                      onClick={() => {
                        app.navigate({ kind: 'dir', path: f.path.split('/').slice(0, -1).join('/') });
                      }}
                    >
                      定位
                    </Button>
                    <Button
                      variant="outline"
                      icon="preview"
                      onClick={() => {
                        app.navigate({ kind: 'dir', path: f.path.split('/').slice(0, -1).join('/') });
                        api.stat(f.path).then((e) => app.setPreviewTarget(e)).catch(() => {});
                      }}
                    >
                      预览
                    </Button>
                  </div>
                );
              })}
            </div>
          </section>

          <section className="setcard">
            <h3 className="setcard__title">最近增长（近 30 天写入）</h3>
            <div className="rows rows--plain">
              {(data.recentGrowth ?? []).map((f) => {
                const meta = kindMeta(f.kind);
                return (
                  <div key={f.path} className="filerow">
                    <IconWell bg={meta.wellBg} fg={meta.wellFg} icon={meta.icon} size={38} radius={10} />
                    <div className="filerow__name">
                      {f.name}
                      <div className="filerow__sub">{formatTime(f.mtime)} · {f.path.split('/').slice(0, -1).join('/')}</div>
                    </div>
                    <div className="filerow__meta">{formatSize(f.size)}</div>
                    <Button
                      variant="tonal"
                      icon="my_location"
                      onClick={() => {
                        app.navigate({ kind: 'dir', path: f.path.split('/').slice(0, -1).join('/') });
                      }}
                    >
                      定位
                    </Button>
                  </div>
                );
              })}
              {!data.recentGrowth?.length && <p className="confirm__hint">近 30 天没有写入变化。</p>}
            </div>
          </section>
        </>
      )}
    </div>
  );
}
