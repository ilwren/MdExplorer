import React, { useEffect, useMemo, useRef, useState } from 'react';
import { api, type FsEntry } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, Dialog, IconButton } from '../ui/widgets';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/common';

/**
 * 文本轻编辑 (concept §5.2)：预览默认只读；进入编辑后才出现
 * 「已修改」点、保存与格式化；保存进入任务队列，落盘前显示差异与外部修改冲突。
 */

const LANG_BY_EXT: Record<string, string> = {
  js: 'javascript', mjs: 'javascript', ts: 'typescript', tsx: 'typescript', jsx: 'javascript',
  py: 'python', cs: 'csharp', java: 'java', go: 'go', rs: 'rust', rb: 'ruby', sh: 'bash',
  json: 'json', yaml: 'yaml', yml: 'yaml', xml: 'xml', html: 'xml', css: 'css', scss: 'css',
  sql: 'sql', lua: 'lua', c: 'cpp', cpp: 'cpp', h: 'cpp', hpp: 'cpp', md: 'markdown',
};

/** Minimal line diff (LCS) for the save-review dialog. */
export function diffLines(a: string, b: string): { type: '+' | '-' | ' '; text: string }[] {
  const A = a.split('\n');
  const B = b.split('\n');
  const n = A.length;
  const m = B.length;
  // bound the DP table for very large files
  if (n * m > 1_500_000) {
    return [
      ...A.slice(0, 200).map((text) => ({ type: '-' as const, text })),
      ...B.slice(0, 200).map((text) => ({ type: '+' as const, text })),
    ];
  }
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = A[i] === B[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }
  const out: { type: '+' | '-' | ' '; text: string }[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (A[i] === B[j]) { out.push({ type: ' ', text: A[i] }); i++; j++; }
    else if (dp[i + 1][j] >= dp[i][j + 1]) { out.push({ type: '-', text: A[i] }); i++; }
    else { out.push({ type: '+', text: B[j] }); j++; }
  }
  while (i < n) { out.push({ type: '-', text: A[i++] }); }
  while (j < m) { out.push({ type: '+', text: B[j++] }); }
  return out;
}

/** OS-native TTS entry (accessibility capability, not a local model). */
export function SpeakButton({ text, title = '朗读' }: { text: string; title?: string }) {
  const supported = typeof window !== 'undefined' && 'speechSynthesis' in window;
  if (!supported) return null;
  return (
    <IconButton
      name="volume_up"
      variant="outline"
      title={title}
      onClick={() => {
        window.speechSynthesis.cancel();
        const u = new SpeechSynthesisUtterance(text.slice(0, 20000));
        window.speechSynthesis.speak(u);
      }}
    />
  );
}

function formatDraft(e: FsEntry, text: string): string {
  if (e.ext === 'json') {
    try { return JSON.stringify(JSON.parse(text), null, 2) + '\n'; } catch { return text; }
  }
  // generic: strip trailing spaces, ensure single trailing newline
  return text.split('\n').map((l) => l.replace(/\s+$/, '')).join('\n').replace(/\n+$/, '') + '\n';
}

export function TextWorkbench({ e }: { e: FsEntry }) {
  const app = useApp();
  const [text, setText] = useState<string | null>(null);
  const [origMtime, setOrigMtime] = useState(e.mtime);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const [saveOpen, setSaveOpen] = useState(false);
  const [external, setExternal] = useState(false);
  const [checkedExt, setCheckedExt] = useState(false);
  const taRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    let alive = true;
    setText(null);
    setEditing(false);
    api.text(e.path).then((r) => {
      if (!alive) return;
      setText(r.text);
      setDraft(r.text);
      setOrigMtime(e.mtime);
      setExternal(false);
      setCheckedExt(false);
    }).catch(() => alive && setText('（无法读取文件内容）'));
    return () => { alive = false; };
  }, [e.path, e.mtime]);

  const dirty = editing && draft !== text;

  const openSave = async () => {
    // external-modification check before showing the diff (concept §5.2)
    try {
      const st = await api.stat(e.path);
      if (Math.abs(st.mtime - origMtime) > 1000) setExternal(true);
    } catch { /* file may be gone; write will fail with a task error */ }
    setCheckedExt(true);
    setSaveOpen(true);
  };

  const diff = useMemo(() => (text !== null ? diffLines(text, draft) : []), [text, draft]);

  if (text === null) return <div className="pv-loading">读取中…</div>;

  if (!editing) {
    return (
      <div className="ed-wrap">
        <div className="ed-bar">
          <Button variant="tonal" icon="edit" onClick={() => { setDraft(text); setEditing(true); }}>
            编辑
          </Button>
          <SpeakButton text={text} title="朗读全文" />
          <span className="ed-bar__hint">预览为只读；进入编辑后可保存与格式化</span>
        </div>
        <ReadOnlyBody e={e} text={text} />
      </div>
    );
  }

  return (
    <div className="ed-wrap is-editing">
      <div className="ed-bar">
        <span className="ed-modified">
          <span className={`ed-dot ${dirty ? 'is-on' : ''}`} />
          {dirty ? '已修改' : '编辑中'}
        </span>
        <Button
          variant="filled"
          icon="save"
          disabled={!dirty}
          onClick={() => void openSave()}
        >
          保存
        </Button>
        <Button variant="tonal" icon="tune" onClick={() => setDraft(formatDraft(e, draft))}>
          格式化
        </Button>
        <Button
          variant="text"
          onClick={() => {
            if (dirty && !window.confirm('放弃未保存的修改？')) return;
            setDraft(text!);
            setEditing(false);
          }}
        >
          退出编辑
        </Button>
        <SpeakButton text={draft} title="朗读草稿" />
      </div>
      <textarea
        ref={taRef}
        className="ed-area"
        spellCheck={false}
        value={draft}
        onChange={(ev) => setDraft(ev.target.value)}
      />
      {saveOpen && (
        <Dialog
          title="保存前复核"
          width={560}
          onClose={() => setSaveOpen(false)}
          actions={
            <>
              <Button variant="text" onClick={() => setSaveOpen(false)}>取消</Button>
              <Button
                variant="filled"
                icon="save"
                onClick={() => {
                  setSaveOpen(false);
                  setText(draft);
                  setEditing(false);
                  void app.runSave(e.path, draft);
                }}
              >
                写入磁盘
              </Button>
            </>
          }
        >
          {checkedExt && external && (
            <p className="ed-warn">
              <Icon name="policy" size={16} />
              文件在预览后被外部修改过。差异可能不完整，确认无误后再写入。
            </p>
          )}
          <div className="ed-diff" role="table" aria-label="修改差异">
            {diff.filter((d) => d.type !== ' ').slice(0, 200).map((d, i) => (
              <div key={i} className={`ed-diff__line is-${d.type === '+' ? 'add' : 'del'}`}>
                <span className="ed-diff__sign">{d.type}</span>
                {d.text || ' '}
              </div>
            ))}
            {diff.every((d) => d.type === ' ') && <div className="ed-diff__line">（无差异）</div>}
          </div>
          <p className="confirm__hint">保存会进入任务队列；同名外部冲突以上方提示为准。</p>
        </Dialog>
      )}
    </div>
  );
}

function ReadOnlyBody({ e, text }: { e: FsEntry; text: string }) {
  if (e.kind === 'markdown') {
    const raw = marked.parse(text, { async: false }) as string;
    return <div className="pv-markdown markdown-body" dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(raw) }} />;
  }
  const lang = LANG_BY_EXT[e.ext] ?? 'plaintext';
  let codeHtml: string;
  try {
    codeHtml = hljs.highlight(text, { language: lang, ignoreIllegals: true }).value;
  } catch {
    codeHtml = text.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  }
  return <pre className="pv-code"><code dangerouslySetInnerHTML={{ __html: codeHtml }} /></pre>;
}
