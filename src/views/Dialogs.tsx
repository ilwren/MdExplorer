import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { useApp } from '../state/app';
import { Button, Dialog, TextField, Toggle } from '../ui/widgets';
import { Icon } from '../ui/icons';
import { formatSize, formatTime } from '../ui/kinds';

export function DialogHost() {
  const app = useApp();
  const d = app.dialog;
  const [text, setText] = useState('');

  useEffect(() => {
    if (!d) return;
    setText(d.name ?? '');
  }, [d]);

  if (app.conflict) return <ConflictDialog />;
  if (!d) return null;
  const close = () => app.setDialog(null);

  if (d.kind === 'rename') {
    return (
      <Dialog
        title="重命名"
        onClose={close}
        actions={
          <>
            <Button variant="text" onClick={close}>取消</Button>
            <Button
              onClick={async () => {
                if (!text.trim()) return;
                await api.rename(d.path!, text.trim()).catch((ex) => app.toast(ex.message));
                app.refresh();
                close();
              }}
            >
              确定
            </Button>
          </>
        }
      >
        <TextField label="新名称" value={text} onChange={setText} autoFocus selectAllOnFocus
          onEnter={() => { if (text.trim()) { api.rename(d.path!, text.trim()).then(() => app.refresh()); close(); } }} />
      </Dialog>
    );
  }

  if (d.kind === 'newfolder' || d.kind === 'newfile') {
    const isFolder = d.kind === 'newfolder';
    return (
      <Dialog
        title={isFolder ? '新建文件夹' : '新建文件'}
        onClose={close}
        actions={
          <>
            <Button variant="text" onClick={close}>取消</Button>
            <Button
              onClick={async () => {
                if (!text.trim()) return;
                const fn = isFolder ? api.mkdir : api.newfile;
                await fn(d.dir!, text.trim(), '').catch((ex) => app.toast(ex.message));
                app.refresh();
                close();
              }}
            >
              创建
            </Button>
          </>
        }
      >
        <TextField
          label="名称"
          value={text}
          onChange={setText}
          autoFocus
          placeholder={isFolder ? '新建文件夹' : 'untitled.txt'}
          onEnter={() => { if (text.trim()) { (isFolder ? api.mkdir(d.dir!, text.trim()) : api.newfile(d.dir!, text.trim(), '')).then(() => app.refresh()); close(); } }}
        />
      </Dialog>
    );
  }

  if (d.kind === 'confirm-delete') {
    const paths = d.paths ?? [];
    const names = paths.map((p) => p.split('/').pop()).slice(0, 4).join('、');
    return (
      <Dialog
        title="删除到回收站？"
        onClose={close}
        actions={
          <>
            <Button variant="text" onClick={close}>取消</Button>
            <Button
              variant="danger"
              onClick={async () => {
                const r = await api.del(paths).catch((ex) => { app.toast(ex.message); return null; });
                if (r) {
                  app.toast(`已删除 ${r.deleted.length} 项`);
                  app.clearSelection();
                  app.setPreviewTarget(null);
                  app.refresh();
                }
                close();
              }}
            >
              删除
            </Button>
          </>
        }
      >
        <p className="confirm__lead">
          将把 <b>{paths.length}</b> 个项目移入回收站：{names}{paths.length > 4 ? ' 等' : ''}。
        </p>
        <p className="confirm__hint">回收站中的项目可以随时还原；清空回收站后无法恢复。</p>
      </Dialog>
    );
  }

  if (d.kind === 'confirm-empty-trash') {
    return (
      <Dialog
        title="清空回收站？"
        onClose={close}
        actions={
          <>
            <Button variant="text" onClick={close}>取消</Button>
            <Button
              variant="danger"
              onClick={async () => {
                await api.trashEmpty().catch(() => {});
                app.toast('回收站已清空');
                app.refresh();
                close();
              }}
            >
              清空
            </Button>
          </>
        }
      >
        <p className="confirm__lead">回收站中的所有项目将被永久删除。</p>
        <p className="confirm__hint">此操作无法撤销。</p>
      </Dialog>
    );
  }

  if (d.kind === 'extract') {
    return <ExtractDialog onClose={close} />;
  }

  return null;
}

/* ------------------------------ conflict resolution (§4.5) ------------------------------ */

function ConflictDialog() {
  const app = useApp();
  const c = app.conflict!;
  const pair = c.pairs[0];
  const fmt = (n: number) => (n ? formatSize(n) : '—');
  const fmtT = (n: number) => (n ? formatTime(n) : '—');
  return (
    <Dialog
      title={c.title}
      onClose={() => c.onResolve('skip', false)}
      width={520}
      actions={
        <>
          <Button variant="text" onClick={() => c.onResolve('skip', c.applyToAll)}>跳过</Button>
          <Button variant="outline" onClick={() => c.onResolve('replace', c.applyToAll)}>替换</Button>
          <Button variant="filled" onClick={() => c.onResolve('keep-both', c.applyToAll)}>保留两者</Button>
        </>
      }
    >
      <div className="conflict">
        <div className="conflict__cols">
          <div className="conflict__side">
            <div className="conflict__tag">来源</div>
            <div className="conflict__name">{pair.name}</div>
            <div className="conflict__meta">{fmt(pair.srcSize)}</div>
            <div className="conflict__meta">{fmtT(pair.srcTime)}</div>
          </div>
          <div className="conflict__vs"><Icon name="swap_vert" size={20} /></div>
          <div className="conflict__side">
            <div className="conflict__tag conflict__tag--dst">目标已存在</div>
            <div className="conflict__name">{pair.name}</div>
            <div className="conflict__meta">{fmt(pair.dstSize)}</div>
            <div className="conflict__meta">{fmtT(pair.dstTime)}</div>
          </div>
        </div>
        <label className="conflict__apply">
          <input
            type="checkbox"
            checked={c.applyToAll}
            onChange={(e) => app.setConflict({ ...c, applyToAll: e.target.checked })}
          />
          <span>应用到剩余同类冲突（默认推荐保留两者）</span>
        </label>
      </div>
    </Dialog>
  );
}

/* ------------------------------ extract dialog (§4.4) ------------------------------ */

export function ExtractDialog({ onClose }: { onClose: () => void }) {
  const app = useApp();
  const d = app.dialog!;
  const zipPath = d.path!;
  const entries = d.entries ?? null;
  const defaultDest = zipPath.replace(/\.zip$/i, '');
  const [dest, setDest] = useState(defaultDest);
  const [keepStructure, setKeepStructure] = useState(true);
  return (
    <Dialog
      title={entries ? `解压所选（${entries.filter((e) => !e.isDir).length} 项）` : '解压全部'}
      onClose={onClose}
      width={480}
      actions={
        <>
          <Button variant="text" onClick={onClose}>取消</Button>
          <Button
            variant="filled"
            icon="folder_zip"
            onClick={() => {
              onClose();
              void app.runExtract(zipPath, entries, dest, keepStructure).then(() => app.setTaskPanelOpen(true));
            }}
          >
            开始解压
          </Button>
        </>
      }
    >
      <TextField label="目标位置" value={dest} onChange={setDest} autoFocus />
      <Toggle label="保留目录结构" desc="关闭后所有文件平铺到目标目录" checked={keepStructure} onChange={setKeepStructure} />
      <p className="confirm__hint">遇到同名文件时会询问保留两者 / 替换 / 跳过；进度可在任务中心查看。</p>
    </Dialog>
  );
}

export function Toasts() {
  const app = useApp();
  return (
    <div className="toasts">
      {app.toasts.map((t) => (
        <div key={t.id} className="toast">{t.text}</div>
      ))}
    </div>
  );
}
