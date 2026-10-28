import React, { useEffect, useRef, useState } from 'react';
import { api, type FsEntry } from '../api';
import { Icon } from '../ui/icons';
import { Button, IconButton } from '../ui/widgets';
import { SpeakButton } from './TextEditor';
// Vite resolves this to a worker asset URL in dev and build
import PdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

/**
 * PDF 阅读工作台 (concept §5.2)：连续滚动 + 目录 + 文档内搜索；
 * 命中高亮留在纸面内；深色主题下纸张不反色。
 */

type PdfJs = typeof import('pdfjs-dist');
type PdfDoc = import('pdfjs-dist').PDFDocumentProxy;

let pdfjsLib: PdfJs | null = null;
async function loadPdfjs(): Promise<PdfJs> {
  if (!pdfjsLib) {
    pdfjsLib = await import('pdfjs-dist');
    try {
      pdfjsLib.GlobalWorkerOptions.workerSrc = PdfWorkerUrl;
    } catch { /* environment without worker URL — pdf.js falls back */ }
  }
  return pdfjsLib;
}

interface OutlineNode { title: string; page: number | null; items: OutlineNode[] }
interface Hit { page: number; snippet: string; rects: { x: number; y: number; w: number; h: number }[] }

const SCALE = 1.15;

export function PdfWorkbench({ e }: { e: FsEntry }) {
  const [doc, setDoc] = useState<PdfDoc | null>(null);
  const [err, setErr] = useState('');
  const [outline, setOutline] = useState<OutlineNode[]>([]);
  const [pagesText, setPagesText] = useState<Record<number, string>>({});
  const [q, setQ] = useState('');
  const [hits, setHits] = useState<Hit[]>([]);
  const [activePage, setActivePage] = useState(1);
  const [showOutline, setShowOutline] = useState(true);
  const [fullText, setFullText] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);

  // load document
  useEffect(() => {
    let alive = true;
    setDoc(null);
    setErr('');
    setOutline([]);
    setHits([]);
    setQ('');
    (async () => {
      try {
        const lib = await loadPdfjs();
        const d = await lib.getDocument({ url: api.entryUrl(e) }).promise;
        if (!alive) return;
        setDoc(d);
        try {
          const ol = await d.getOutline();
          const flatten = async (items: NonNullable<typeof ol>): Promise<OutlineNode[]> => {
            const out: OutlineNode[] = [];
            for (const it of items) {
              let page: number | null = null;
              try {
                if (typeof it.dest === 'string') {
                  const dest = await d.getDestination(it.dest);
                  if (dest && Array.isArray(dest) && dest[0]) page = (await d.getPageIndex(dest[0] as never)) + 1;
                } else if (Array.isArray(it.dest) && it.dest[0]) {
                  page = (await d.getPageIndex(it.dest[0] as never)) + 1;
                }
              } catch { /* unresolved destination */ }
              out.push({ title: it.title, page, items: await flatten((it.items ?? []) as never) });
            }
            return out;
          };
          if (alive && ol) setOutline(await flatten(ol));
        } catch { /* no outline */ }
      } catch (ex) {
        if (alive) setErr((ex as Error).message || '无法解析 PDF');
      }
    })();
    return () => { alive = false; };
  }, [e.path, e.zipOf, e.zipEntry]);

  const gotoPage = (p: number) => {
    const el = scrollRef.current?.querySelector(`[data-page="${p}"]`);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    setActivePage(p);
  };

  const runSearch = async () => {
    if (!doc || !q.trim()) { setHits([]); return; }
    const query = q.trim().toLowerCase();
    const found: Hit[] = [];
    let gathered = '';
    for (let p = 1; p <= doc.numPages; p++) {
      let text = pagesText[p];
      if (text === undefined) {
        try {
          const page = await doc.getPage(p);
          const tc = await page.getTextContent();
          const lib = await loadPdfjs();
          const items = tc.items as { str: string; transform: number[]; width: number; height: number; fontName: string }[];
          text = items.map((it) => it.str).join(' ');
          setPagesText((m) => ({ ...m, [p]: text! }));
          gathered += `\n[p${p}] ${text}`;
          // item-level rects for in-page highlight
          const pageRects: Hit['rects'] = [];
          const viewport = page.getViewport({ scale: SCALE });
          for (const it of items) {
            if (it.str.toLowerCase().includes(query)) {
              const tx = lib.Util.transform(viewport.transform, it.transform);
              const fontHeight = Math.hypot(tx[2], tx[3]);
              pageRects.push({
                x: tx[4],
                y: tx[5] - fontHeight,
                w: Math.max(6, it.width * SCALE),
                h: fontHeight,
              });
            }
          }
          const idx = text.toLowerCase().indexOf(query);
          found.push({
            page: p,
            snippet: idx >= 0
              ? `…${text.slice(Math.max(0, idx - 20), idx + query.length + 40)}…`
              : '',
            rects: pageRects,
          });
        } catch { /* page text unavailable */ }
      } else {
        const idx = text.toLowerCase().indexOf(query);
        if (idx >= 0) {
          found.push({
            page: p,
            snippet: `…${text.slice(Math.max(0, idx - 20), idx + query.length + 40)}…`,
            rects: [],
          });
        }
        gathered += `\n[p${p}] ${text}`;
      }
    }
    setHits(found.filter((h) => h.snippet || h.rects.length));
    setFullText(gathered);
    if (found.length) gotoPage(found[0].page);
  };

  return (
    <div className="pdfw">
      <div className="pdfw__bar">
        <button className={`pdfw__tool ${showOutline ? 'is-active' : ''}`} onClick={() => setShowOutline((v) => !v)}>
          <Icon name="toc" size={17} />
          目录
        </button>
        <div className="pdfw__search">
          <Icon name="search" size={16} />
          <input
            placeholder="文档内搜索…"
            value={q}
            onChange={(ev) => setQ(ev.target.value)}
            onKeyDown={(ev) => { if (ev.key === 'Enter') void runSearch(); }}
          />
          <IconButton name="search" size={16} variant="plain" title="搜索" onClick={() => void runSearch()} />
        </div>
        {doc && <span className="pdfw__info">{doc.numPages} 页</span>}
        {fullText && <SpeakButton text={fullText} title="朗读已提取文本" />}
        {hits.length > 0 && <span className="pdfw__hits">{hits.length} 处命中</span>}
      </div>

      {err && (
        <div className="empty">
          <Icon name="description" size={44} />
          <p className="empty__title">PDF 渲染在此环境不可用</p>
          <p className="empty__hint">{err}</p>
        </div>
      )}

      <div className={`pdfw__main ${showOutline && outline.length ? '' : 'no-outline'}`}>
        {showOutline && outline.length > 0 && (
          <nav className="pdfw__outline">
            <OutlineTree nodes={outline} onPick={gotoPage} activePage={activePage} />
          </nav>
        )}
        <div className="pdfw__scroll" ref={scrollRef}>
          {!doc && !err && <div className="pv-loading">加载 PDF…</div>}
          {doc && Array.from({ length: doc.numPages }, (_, i) => i + 1).map((p) => (
            <PdfPage
              key={p}
              doc={doc}
              pageNum={p}
              rects={hits.filter((h) => h.page === p).flatMap((h) => h.rects)}
              onVisible={() => setActivePage(p)}
            />
          ))}
          {doc && hits.length > 0 && (
            <div className="pdfw__hitlist">
              {hits.map((h, i) => (
                <button key={i} className="pdfw__hit" onClick={() => gotoPage(h.page)}>
                  <b>p{h.page}</b>
                  <span>{h.snippet || `命中 ${h.rects.length} 处`}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function OutlineTree({ nodes, onPick, activePage }: {
  nodes: OutlineNode[]; onPick: (p: number) => void; activePage: number;
}) {
  return (
    <ul className="pdfw__olist">
      {nodes.map((n, i) => (
        <li key={i}>
          <button
            className={`pdfw__onode ${n.page === activePage ? 'is-active' : ''}`}
            onClick={() => n.page && onPick(n.page)}
            disabled={!n.page}
          >
            {n.title}
          </button>
          {n.items.length > 0 && <OutlineTree nodes={n.items} onPick={onPick} activePage={activePage} />}
        </li>
      ))}
    </ul>
  );
}

function PdfPage({ doc, pageNum, rects, onVisible }: {
  doc: PdfDoc; pageNum: number; rects: Hit['rects']; onVisible: () => void;
}) {
  const holder = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [rendered, setRendered] = useState(false);

  // lazy render via IntersectionObserver (eager fallback otherwise)
  useEffect(() => {
    const el = holder.current;
    if (!el) return;
    const render = () => {
      setRendered(true);
      onVisible();
    };
    if (typeof IntersectionObserver === 'undefined') {
      if (pageNum <= 2) render();
      return;
    }
    const io = new IntersectionObserver((entries) => {
      for (const en of entries) {
        if (en.isIntersecting) render();
      }
    }, { rootMargin: '600px 0px' });
    io.observe(el);
    return () => io.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pageNum]);

  useEffect(() => {
    if (!rendered || !canvasRef.current) return;
    let alive = true;
    (async () => {
      try {
        const page = await doc.getPage(pageNum);
        if (!alive || !canvasRef.current) return;
        const viewport = page.getViewport({ scale: SCALE });
        const canvas = canvasRef.current;
        canvas.width = viewport.width;
        canvas.height = viewport.height;
        await page.render({ canvasContext: canvas.getContext('2d') as never, viewport }).promise;
      } catch { /* canvas unavailable in this environment */ }
    })();
    return () => { alive = false; };
  }, [rendered, doc, pageNum]);

  return (
    <div className="pdfw__page" data-page={pageNum} ref={holder}>
      <div className="pdfw__paper" style={{ aspectRatio: '1 / 1.414' }}>
        {rendered ? (
          <>
            <canvas ref={canvasRef} className="pdfw__canvas" />
            {/* in-page highlight — kept on the paper surface */}
            {rects.map((r, i) => (
              <span
                key={i}
                className="pdfw__hl"
                style={{ left: (r.x / (canvasRef.current?.width || 1)) * 100 + '%', top: (r.y / (canvasRef.current?.height || 1)) * 100 + '%', width: (r.w / (canvasRef.current?.width || 1)) * 100 + '%', height: (r.h / (canvasRef.current?.height || 1)) * 100 + '%' }}
              />
            ))}
          </>
        ) : (
          <span className="pdfw__ph">{pageNum}</span>
        )}
      </div>
      <div className="pdfw__pageno">{pageNum}</div>
    </div>
  );
}
