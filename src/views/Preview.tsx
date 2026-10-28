import React, { useEffect, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, Chip, IconButton, Menu, useDrag } from '../ui/widgets';
import { formatSize, formatTime, kindMeta } from '../ui/kinds';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/common';

/* ------------------------------ preview body ------------------------------ */

export function useEntryFacts(e: FsEntry | null) {
  const [img, setImg] = useState<{ w: number; h: number } | null>(null);
  useEffect(() => {
    setImg(null);
    if (!e || e.kind !== 'image' || e.ext === 'svg') return;
    const im = new Image();
    im.onload = () => setImg({ w: im.naturalWidth, h: im.naturalHeight });
    im.src = api.rawUrl(e.path);
  }, [e?.path, e?.kind, e?.ext]);
  return img;
}

const LANG_BY_EXT: Record<string, string> = {
  js: 'javascript', mjs: 'javascript', ts: 'typescript', tsx: 'typescript', jsx: 'javascript',
  py: 'python', cs: 'csharp', java: 'java', go: 'go', rs: 'rust', rb: 'ruby', sh: 'bash',
  json: 'json', yaml: 'yaml', yml: 'yaml', xml: 'xml', html: 'xml', css: 'css', scss: 'css',
  sql: 'sql', lua: 'lua', c: 'cpp', cpp: 'cpp', h: 'cpp', hpp: 'cpp', md: 'markdown',
};

export function PreviewBody({ e }: { e: FsEntry }) {
  const meta = kindMeta(e.kind);
  const img = useEntryFacts(e);

  if (e.kind === 'image' && e.ext !== 'svg') {
    return (
      <div className="pv-image">
        <img src={api.rawUrl(e.path)} alt={e.name} />
      </div>
    );
  }
  if (e.ext === 'svg') {
    return (
      <div className="pv-image">
        <img src={api.rawUrl(e.path)} alt={e.name} />
      </div>
    );
  }
  if (e.kind === 'video') {
    return <div className="pv-media"><video src={api.rawUrl(e.path)} controls preload="metadata" /></div>;
  }
  if (e.kind === 'audio') {
    return (
      <div className="pv-audio">
        <span className="pv-audio__well" style={{ background: meta.wellBg, color: meta.wellFg }}>
          <Icon name="audio_file" size={56} />
        </span>
        <audio src={api.rawUrl(e.path)} controls preload="metadata" />
      </div>
    );
  }
  if (e.kind === 'pdf') {
    return (
      <div className="pv-pdf">
        <iframe src={api.rawUrl(e.path)} title={e.name} />
      </div>
    );
  }
  if (e.kind === 'archive') {
    return <ZipPreview e={e} />;
  }
  if (e.kind === 'markdown' || e.kind === 'code' || e.kind === 'text') {
    return <TextPreview e={e} />;
  }
  return (
    <div className="pv-generic">
      <span className="pv-generic__well" style={{ background: meta.wellBg, color: meta.wellFg }}>
        <Icon name={meta.icon} size={56} />
      </span>
      <p>{meta.label} · {formatSize(e.size)}</p>
      <p className="pv-generic__hint">此类型暂不支持内置预览</p>
    </div>
  );
}

function TextPreview({ e }: { e: FsEntry }) {
  const [html, setHtml] = useState<string | null>(null);
  const [text, setText] = useState<string | null>(null);
  useEffect(() => {
    let alive = true;
    setHtml(null); setText(null);
    api.text(e.path).then((r) => {
      if (!alive) return;
      if (e.kind === 'markdown') {
        const raw = marked.parse(r.text, { async: false }) as string;
        setHtml(DOMPurify.sanitize(raw));
      } else {
        setText(r.text);
      }
    }).catch(() => setText('（无法读取文件内容）'));
    return () => { alive = false; };
  }, [e.path, e.kind]);

  if (html !== null) return <div className="pv-markdown markdown-body" dangerouslySetInnerHTML={{ __html: html }} />;
  if (text === null) return <div className="pv-loading">读取中…</div>;
  const lang = LANG_BY_EXT[e.ext] ?? 'plaintext';
  let codeHtml: string;
  try {
    codeHtml = hljs.highlight(text, { language: lang, ignoreIllegals: true }).value;
  } catch {
    codeHtml = text.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  }
  return <pre className="pv-code"><code dangerouslySetInnerHTML={{ __html: codeHtml }} /></pre>;
}

function ZipPreview({ e }: { e: FsEntry }) {
  const [entries, setEntries] = useState<Awaited<ReturnType<typeof api.zip>>['entries'] | null>(null);
  const [err, setErr] = useState('');
  useEffect(() => {
    let alive = true;
    api.zip(e.path).then((r) => alive && setEntries(r.entries.slice(0, 500))).catch((ex) => alive && setErr(ex.message));
    return () => { alive = false; };
  }, [e.path]);
  if (err) return <div className="pv-loading">无法读取压缩包：{err}</div>;
  if (!entries) return <div className="pv-loading">读取压缩包…</div>;
  return (
    <div className="pv-zip">
      <div className="pv-zip__head">
        <span>名称</span><span>大小</span><span>压缩后</span>
      </div>
      {entries.map((en, i) => (
        <div key={i} className="pv-zip__row">
          <span className="pv-zip__name">
            <Icon name={en.isDir ? 'folder' : kindMeta(en.kind).icon} size={16} />
            {en.name}
          </span>
          <span>{en.isDir ? '' : formatSize(en.size)}</span>
          <span>{en.isDir ? '' : formatSize(en.compressedSize)}</span>
        </div>
      ))}
      {entries.length >= 500 && <p className="pv-loading">仅显示前 500 项</p>}
    </div>
  );
}

/* ------------------------------ properties ------------------------------ */

export function PropertiesBody({ e }: { e: FsEntry }) {
  const app = useApp();
  const img = useEntryFacts(e);
  const rows: [string, React.ReactNode][] = [
    ['名称', e.name],
    ['类型', `${kindMeta(e.kind).label}${e.ext ? ` (.${e.ext})` : ''}`],
    ['大小', e.isDir ? `${e.children ?? '—'} 个项目` : formatSize(e.size)],
    ['修改时间', formatTime(e.mtime)],
    ['位置', <span className="props__path" title={e.path}>{e.path}</span>],
  ];
  if (img) rows.splice(3, 0, ['尺寸', `${img.w} × ${img.h}`]);
  return (
    <div className="props">
      {rows.map(([k, v]) => (
        <div key={k} className="props__row">
          <span className="props__key">{k}</span>
          <span className="props__val">{v}</span>
        </div>
      ))}
      <div className="props__actions">
        <Button variant="outline" icon="content_paste" onClick={() => navigator.clipboard?.writeText(e.path).then(() => app.toast('已复制路径'))}>
          复制路径
        </Button>
      </div>
    </div>
  );
}

/* ------------------------------ panel chrome ------------------------------ */

function PreviewChrome({ e, floating, onClose }: { e: FsEntry; floating?: boolean; onClose?: () => void }) {
  const app = useApp();
  const img = useEntryFacts(e);
  const tab = app.activeTab.previewTab;
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);
  const facts = [
    img ? { icon: 'aspect_ratio', text: `${img.w}×${img.h}` } : null,
    e.isDir ? { icon: 'folder', text: formatCountSafe(e) } : { icon: 'save', text: formatSize(e.size) },
    floating ? null : { icon: 'schedule', text: formatTime(e.mtime).slice(0, 10) },
  ].filter(Boolean) as { icon: string; text: string }[];

  return (
    <>
      <div className="pv-head">
        <h2 className="pv-title" title={e.name}>{e.name}</h2>
        {floating && <IconButton name="close" title="关闭预览" variant="tonal" onClick={onClose} />}
      </div>
      <div className="pv-tabs" role="tablist">
        <button role="tab" aria-selected={tab === 'preview'} className={`pv-tab ${tab === 'preview' ? 'is-active' : ''}`} onClick={() => app.setPreviewTab('preview')}>
          预览
        </button>
        <button role="tab" aria-selected={tab === 'properties'} className={`pv-tab ${tab === 'properties' ? 'is-active' : ''}`} onClick={() => app.setPreviewTab('properties')}>
          属性
        </button>
      </div>
      <div className="pv-body">
        {tab === 'preview' ? <PreviewBody e={e} /> : <PropertiesBody e={e} />}
      </div>
      {tab === 'preview' && (
        <div className="pv-facts">
          {facts.map((f, i) => <Chip key={i} icon={f.icon}>{f.text}</Chip>)}
        </div>
      )}
      <div className="pv-actions">
        <Button
          variant="filled"
          icon="open_in_new"
          className="pv-open"
          onClick={() => {
            if (e.isDir) app.openEntry(e);
            else window.open(api.rawUrl(e.path), '_blank', 'noopener');
          }}
        >
          打开
        </Button>
        <IconButton
          name="share"
          variant="outline"
          title="共享"
          onClick={() => {
            const nav = navigator as Navigator & { share?: (d: ShareData) => Promise<void> };
            if (nav.share) nav.share({ title: e.name, text: e.path }).catch(() => {});
            else navigator.clipboard?.writeText(e.path).then(() => app.toast('已复制路径'));
          }}
        />
        <IconButton
          name="more_vert"
          variant="outline"
          title="更多"
          onClick={(ev) => {
            const r = (ev.currentTarget as HTMLElement).getBoundingClientRect();
            setMenu({ x: r.left - 160, y: r.bottom + 4 });
          }}
        />
      </div>
      {menu && (
        <Menu
          x={menu.x} y={menu.y}
          onClose={() => setMenu(null)}
          onPick={(id) => {
            if (id === 'rename') app.setDialog({ kind: 'rename', path: e.path, name: e.name });
            if (id === 'delete') app.setDialog({ kind: 'confirm-delete', paths: [e.path] });
            if (id === 'copy') app.setClipboard({ op: 'copy', paths: [e.path] });
            if (id === 'copypath') navigator.clipboard?.writeText(e.path).then(() => app.toast('已复制路径'));
          }}
          items={[
            { id: 'copy', label: '复制', icon: 'content_copy' },
            { id: 'rename', label: '重命名', icon: 'drive_file_rename_outline' },
            { id: 'copypath', label: '复制路径', icon: 'content_paste' },
            { id: 'separator', separator: true },
            { id: 'delete', label: '删除', icon: 'delete', danger: true },
          ]}
        />
      )}
    </>
  );
}

function formatCountSafe(e: FsEntry) {
  return `${e.children ?? '—'} 个项目`;
}

/* ------------------------------ side pane ------------------------------ */

export function PreviewPane() {
  const app = useApp();
  const e = app.previewTarget;
  if (!e) return null;
  return (
    <aside className="pv-pane">
      <PreviewChrome e={e} />
    </aside>
  );
}

/* ------------------------------ floating window ------------------------------ */

export function FloatingPreview() {
  const app = useApp();
  const e = app.previewTarget;
  const [pos, setPos] = useState<{ x: number; y: number }>(() => app.floatPreviewPos ?? { x: 120, y: 140 });
  const drag = useDrag((dx, dy) => {
    setPos((p) => {
      const n = { x: Math.max(8, p.x + dx), y: Math.max(8, p.y + dy) };
      app.setFloatPreviewPos(n);
      return n;
    });
  });
  if (!e) return null;
  return (
    <div className="pv-float" style={{ left: pos.x, top: pos.y }}>
      <div className="pv-float__handle" {...drag}>
        <span className="pv-float__grip" />
      </div>
      <PreviewChrome e={e} floating onClose={() => app.setPreviewTarget(null)} />
    </div>
  );
}
