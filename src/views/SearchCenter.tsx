import React, { useCallback, useEffect, useRef, useState } from 'react';
import { api, type IndexStatus, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, Chip, IconWell, Segmented, Toggle } from '../ui/widgets';
import { formatSize, formatTime, kindMeta } from '../ui/kinds';

/**
 * 搜索与索引中心 (concept §4.1) — 独立目的地：跨根索引、筛选与索引健康。
 * 迷你搜索岛仍负责当前文件夹即时筛选/递归搜索，两者并存。
 */

const KINDS = [
  { id: '', label: '全部' },
  { id: 'image', label: '图片' },
  { id: 'video', label: '视频' },
  { id: 'audio', label: '音频' },
  { id: 'pdf', label: 'PDF' },
  { id: 'doc', label: '文档' },
  { id: 'text', label: '文本' },
  { id: 'markdown', label: 'Markdown' },
  { id: 'code', label: '代码' },
  { id: 'archive', label: '压缩包' },
];

const TIME_FILTERS = [
  { id: 0, label: '全部时间' },
  { id: 1, label: '今天' },
  { id: 7, label: '近 7 天' },
  { id: 30, label: '近 30 天' },
];

function sinceOf(days: number): number {
  if (!days) return 0;
  if (days === 1) {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d.getTime();
  }
  return Date.now() - days * 24 * 3600 * 1000;
}

export function SearchCenter() {
  const app = useApp();
  const [q, setQ] = useState('');
  const [mode, setMode] = useState<'name' | 'content'>('name');
  const [kind, setKind] = useState('');
  const [root, setRoot] = useState('');
  const [days, setDays] = useState(0);
  const [results, setResults] = useState<FsEntry[]>([]);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<IndexStatus | null>(null);
  const [indexErr, setIndexErr] = useState('');
  const [showFilters, setShowFilters] = useState(false);
  const [statusOpen, setStatusOpen] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const reloadStatus = useCallback(() => {
    api.indexStatus().then((s) => { setStatus(s); setIndexErr(''); }).catch((e) => setIndexErr((e as Error).message));
  }, []);
  useEffect(reloadStatus, [reloadStatus]);
  useEffect(() => { inputRef.current?.focus(); }, []);

  const runSearch = useCallback((query: string, k: string, r: string, daysV: number, content: boolean) => {
    setBusy(true);
    api.indexSearch(query, { kind: k, root: r, since: sinceOf(daysV), content })
      .then((res) => { setResults(res.results); setBusy(false); })
      .catch(() => { setResults([]); setBusy(false); });
  }, []);

  // debounce typing; filters re-run immediately when a query exists
  useEffect(() => {
    const t = setTimeout(() => {
      if (q.trim()) runSearch(q, kind, root, days, mode === 'content');
    }, 250);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, kind, root, days, mode]);

  return (
    <div className="searchcenter">
      <header className="sc-head">
        <div className="sc-query">
          <Icon name="manage_search" size={20} />
          <input
            ref={inputRef}
            className="sc-input"
            placeholder="跨索引根搜索文件名或正文…"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && q.trim()) runSearch(q, kind, root, days, mode === 'content');
            }}
          />
          {busy && <span className="sc-busy">检索中…</span>}
        </div>
        <Segmented
          value={mode}
          options={[
            { id: 'name', label: '文件名' },
            { id: 'content', label: '正文' },
          ]}
          onChange={(v) => setMode(v as 'name' | 'content')}
        />
        <Chip disabled icon="policy">语义 · 后续</Chip>
        <button className="sc-filterbtn" onClick={() => setShowFilters((v) => !v)}>
          <Icon name="filter_alt" size={18} />
          筛选
        </button>
      </header>

      {/* expanded: merge right column into a collapsible status strip */}
      <button className="sc-statusstrip" onClick={() => setStatusOpen((v) => !v)}>
        <Icon name={status?.paused ? 'hourglass_top' : 'update'} size={15} />
        {status ? (
          <>
            <span>索引 {status.fileCount.toLocaleString()} 条 · {formatSize(status.sizeBytes)}</span>
            {status.updatedAt ? <span>更新 {formatTime(status.updatedAt)}</span> : <span>尚未建立</span>}
            {!!status.staleCount && <span className="sc-warn">{status.staleCount} 条失效</span>}
            <span>{status.contentEnabled ? '正文索引开' : '正文索引关'}</span>
            {status.paused && <span className="sc-warn">维护已暂停</span>}
          </>
        ) : (
          <span>索引状态读取中…</span>
        )}
        <Icon name={statusOpen ? 'chevron_left' : 'chevron_right'} size={16} />
      </button>

      <div className={`sc-grid ${showFilters ? 'show-filters' : ''} ${statusOpen ? 'show-status' : ''}`}>
        <aside className="sc-filters">
          <section className="sc-fgroup">
            <h3>索引根</h3>
            <div className="sc-chips">
              <Chip active={root === ''} onClick={() => setRoot('')}>全部根</Chip>
              {status?.roots.map((r) => (
                <Chip key={r} active={root === r} onClick={() => setRoot(root === r ? '' : r)}>
                  {r.split('/').pop() || r}
                </Chip>
              ))}
            </div>
          </section>
          <section className="sc-fgroup">
            <h3>类型</h3>
            <div className="sc-chips">
              {KINDS.map((k) => (
                <Chip key={k.id} active={kind === k.id} onClick={() => setKind(k.id)}>{k.label}</Chip>
              ))}
            </div>
          </section>
          <section className="sc-fgroup">
            <h3>时间</h3>
            <div className="sc-chips">
              {TIME_FILTERS.map((t) => (
                <Chip key={t.id} active={days === t.id} onClick={() => setDays(t.id)}>{t.label}</Chip>
              ))}
            </div>
          </section>
        </aside>

        <main className="sc-results">
          {!q.trim() && (
            <div className="empty">
              <Icon name="manage_search" size={48} />
              <p className="empty__title">输入查询，跨索引根检索</p>
              <p className="empty__hint">结果会显示父路径与正文片段；索引健康与隐私操作在右侧同屏可达</p>
            </div>
          )}
          {q.trim() && !busy && results.length === 0 && (
            <div className="empty">
              <Icon name="search" size={48} />
              <p className="empty__title">索引里没有匹配项</p>
              <p className="empty__hint">{mode === 'content' && status && !status.contentEnabled ? '正文索引已关闭，可在右侧开启' : '试试放宽类型或时间筛选'}</p>
            </div>
          )}
          {results.map((e) => {
            const meta = kindMeta(e.kind);
            const parent = e.path.split('/').slice(0, -1).join('/');
            return (
              <button
                key={e.path}
                className="searchrow"
                onClick={() => {
                  app.navigate({ kind: 'dir', path: parent });
                  if (!e.isDir) app.setPreviewTarget(e);
                }}
              >
                <span className="searchrow__well" style={{ background: meta.wellBg, color: meta.wellFg }}>
                  <Icon name={meta.icon} size={18} />
                </span>
                <span className="searchrow__body">
                  <span className="searchrow__name">{e.name}</span>
                  {e.snippet
                    ? <span className="searchrow__snippet">…{e.snippet}…</span>
                    : <span className="searchrow__path">{parent}</span>}
                </span>
                <span className="searchrow__meta">{formatSize(e.size)}</span>
              </button>
            );
          })}
        </main>

        <aside className={`sc-status ${statusOpen ? 'is-open' : ''}`}>
          <IndexStatusCard status={status} err={indexErr} onChange={reloadStatus} />
          <IndexPrivacyCard
            status={status}
            confirmClear={confirmClear}
            setConfirmClear={setConfirmClear}
            onChange={reloadStatus}
          />
        </aside>
      </div>
    </div>
  );
}

export function IndexStatusCard({ status, err, onChange }: {
  status: IndexStatus | null; err?: string; onChange: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState('');
  return (
    <section className="setcard">
      <h3 className="setcard__title">索引状态</h3>
      {err && <p className="conflict__meta">{err}</p>}
      {status ? (
        <div className="insights__stats sc-statgrid">
          <div className="statcard">
            <span className="statcard__label">索引体积</span>
            <span className="statcard__value">{formatSize(status.sizeBytes)}</span>
            <span className="statcard__sub">{status.fileCount.toLocaleString()} 条目</span>
          </div>
          <div className="statcard">
            <span className="statcard__label">更新时间</span>
            <span className="statcard__value" style={{ fontSize: 15 }}>
              {status.updatedAt ? formatTime(status.updatedAt) : '尚未建立'}
            </span>
            <span className="statcard__sub">
              失效条目 <b className={status.staleCount ? 'sc-warn' : ''}>{status.staleCount ?? 0}</b>
            </span>
          </div>
        </div>
      ) : (
        <p className="conflict__meta">读取中…</p>
      )}
      <Toggle
        label="正文索引"
        desc="关闭后只搜索文件名，正文片段不再检索"
        checked={!!status?.contentEnabled}
        onChange={(v) => {
          api.indexSettings({ contentEnabled: v }).then(() => onChange());
        }}
      />
      <div className="sc-actions">
        <Button
          variant="filled"
          icon="update"
          disabled={busy || status?.paused}
          onClick={() => {
            setBusy(true);
            api.indexRebuild()
              .then((r) => {
                setBusy(false);
                setNote(r.truncated ? '扫描超出预算，已索引部分目录' : '索引已重建');
                onChange();
              })
              .catch((e) => { setBusy(false); setNote((e as Error).message); });
          }}
        >
          {busy ? '重建中…' : '重建索引'}
        </Button>
        {note && <span className="conflict__meta">{note}</span>}
      </div>
    </section>
  );
}

export function IndexPrivacyCard({ status, confirmClear, setConfirmClear, onChange }: {
  status: IndexStatus | null;
  confirmClear: boolean;
  setConfirmClear: (b: boolean) => void;
  onChange: () => void;
}) {
  return (
    <section className="setcard">
      <h3 className="setcard__title">隐私卡</h3>
      <p className="confirm__hint">索引只保存在本机，清空与暂停随时可用，不上传任何内容。</p>
      <Toggle
        label="暂停维护"
        desc="暂停自动重建；已有索引继续用于搜索"
        checked={!!status?.paused}
        onChange={(v) => api.indexSettings({ paused: v }).then(() => onChange())}
      />
      <div className="sc-fgroup">
        <h3>索引根目录</h3>
        <div className="sc-chips">
          {status?.roots.map((r) => (
            <Chip key={r}>
              {r.split('/').pop() || r}
              {status.roots.length > 1 && (
                <button
                  className="chip__x"
                  title={`移除 ${r}`}
                  onClick={() => api.indexRoot('remove', r).then(() => onChange())}
                >
                  <Icon name="close" size={13} />
                </button>
              )}
            </Chip>
          ))}
        </div>
      </div>
      {confirmClear ? (
        <div className="sc-actions">
          <Button variant="filled" icon="delete" onClick={() => api.indexClear().then(() => { setConfirmClear(false); onChange(); })}>
            确认清空
          </Button>
          <Button variant="text" onClick={() => setConfirmClear(false)}>取消</Button>
        </div>
      ) : (
        <div className="sc-actions">
          <Button variant="outline" icon="delete" onClick={() => setConfirmClear(true)}>清空索引</Button>
        </div>
      )}
    </section>
  );
}

/** 索引管理（设置子页，Compact 规格：索引管理进入设置子页）。 */
export function IndexAdmin() {
  const [status, setStatus] = useState<IndexStatus | null>(null);
  const [err, setErr] = useState('');
  const [confirmClear, setConfirmClear] = useState(false);
  const reload = useCallback(() => {
    api.indexStatus().then((s) => { setStatus(s); setErr(''); }).catch((e) => setErr((e as Error).message));
  }, []);
  useEffect(reload, [reload]);
  return (
    <>
      <IndexStatusCard status={status} err={err} onChange={reload} />
      <IndexPrivacyCard
        status={status}
        confirmClear={confirmClear}
        setConfirmClear={setConfirmClear}
        onChange={reload}
      />
    </>
  );
}
