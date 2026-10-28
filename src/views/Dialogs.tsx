import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { useApp } from '../state/app';
import { Button, Dialog, TextField } from '../ui/widgets';
import { formatSize } from '../ui/kinds';

export function DialogHost() {
  const app = useApp();
  const d = app.dialog;
  const [text, setText] = useState('');

  useEffect(() => {
    if (!d) return;
    setText(d.name ?? '');
  }, [d]);

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

  return null;
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
