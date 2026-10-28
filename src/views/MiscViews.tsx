import React, { useEffect, useState } from 'react';
import { api, type TrashItem } from '../api';
import { useApp } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, Segmented, Toggle, IconWell } from '../ui/widgets';
import { formatSize, formatTime, kindMeta } from '../ui/kinds';
import { IndexAdmin } from './SearchCenter';

/* ------------------------------ trash ------------------------------ */

export function TrashView() {
  const app = useApp();
  const [items, setItems] = useState<TrashItem[] | null>(null);
  const load = () => api.trash().then((r) => setItems(r.items)).catch(() => setItems([]));
  useEffect(() => { load(); }, []);

  return (
    <div className="trash">
      <div className="trash__head">
        <div>
          <h2 className="page-title">回收站</h2>
          <p className="page-desc">删除的项目先进入回收站，可随时还原；清空后无法恢复。</p>
        </div>
        <Button variant="danger" icon="delete_sweep" onClick={() => app.setDialog({ kind: 'confirm-empty-trash' })}>
          清空回收站
        </Button>
      </div>
      {items === null && <p className="pv-loading">读取中…</p>}
      {items?.length === 0 && (
        <div className="empty">
          <Icon name="delete" size={56} />
          <p className="empty__title">回收站是空的</p>
        </div>
      )}
      <div className="rows">
        {items?.map((it) => {
          const meta = kindMeta(it.kind);
          return (
            <div key={it.id} className="filerow">
              <IconWell bg={meta.wellBg} fg={meta.wellFg} icon={meta.icon} size={38} radius={10} />
              <div className="filerow__name">
                {it.name}
                <div className="filerow__sub">{it.originalPath} · {formatTime(it.deletedAt)}{it.isDir ? '' : ` · ${formatSize(it.size)}`}</div>
              </div>
              <Button
                variant="tonal"
                icon="restore"
                onClick={() => {
                  api.trashRestore([it.id]).then((r) => {
                    app.toast(`${r.restored.join('、') || it.name} 已还原`);
                    app.refresh();
                    load();
                  });
                }}
              >
                还原
              </Button>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ------------------------------ settings ------------------------------ */

export function SettingsView() {
  const app = useApp();
  const s = app.settings;
  return (
    <div className="settings">
      <h2 className="page-title">设置</h2>

      <section className="setcard">
        <h3 className="setcard__title">外观</h3>
        <div className="setcard__row">
          <span className="setcard__label">主题</span>
          <Segmented
            value={s.theme}
            options={[
              { id: 'light', label: '浅色', icon: 'light_mode' },
              { id: 'dark', label: '深色', icon: 'dark_mode' },
              { id: 'system', label: '跟随系统', icon: 'brightness_6' },
            ]}
            onChange={(v) => app.patchSettings({ theme: v })}
          />
        </div>
        <Toggle
          label="减少动态效果"
          desc="关闭页面切换与浮窗的过渡动画"
          checked={s.reduceMotion}
          onChange={(b) => app.patchSettings({ reduceMotion: b })}
        />
      </section>

      <section className="setcard">
        <h3 className="setcard__title">浏览</h3>
        <div className="setcard__row">
          <span className="setcard__label">默认视图</span>
          <Segmented
            value={s.viewMode}
            options={[
              { id: 'grid', label: '网格', icon: 'grid_view' },
              { id: 'list', label: '列表', icon: 'view_list' },
            ]}
            onChange={(v) => app.patchSettings({ viewMode: v })}
          />
        </div>
        <div className="setcard__row">
          <span className="setcard__label">排序方式</span>
          <Segmented
            value={s.sortBy}
            options={[
              { id: 'name', label: '名称' },
              { id: 'size', label: '大小' },
              { id: 'mtime', label: '时间' },
              { id: 'kind', label: '类型' },
            ]}
            onChange={(v) => app.patchSettings({ sortBy: v })}
          />
        </div>
        <Toggle label="显示系统文件" desc="显示以点开头的隐藏文件" checked={s.showHidden} onChange={(b) => app.patchSettings({ showHidden: b })} />
        <Toggle label="删除前确认" desc="删除到回收站前显示影响范围" checked={s.confirmDelete} onChange={(b) => app.patchSettings({ confirmDelete: b })} />
      </section>

      <section className="setcard">
        <h3 className="setcard__title">索引管理</h3>
        <p className="confirm__hint">索引只保存在本机；Compact 下的索引管理入口。</p>
        <IndexAdmin />
      </section>

      <section className="setcard">
        <h3 className="setcard__title">关于</h3>
        <div className="about">
          <img src="/eidoria-icon.png" alt="" className="about__icon" />
          <div>
            <div className="about__name">Eidoria 万象屿</div>
            <div className="about__ver">Web 第一版 · v0.1 · 本地文件资源管理器</div>
            <div className="about__desc">
              纯本地、无账号、无遥测。设计遵循概念设计 v3（Material 3 · 青色种子 #006A6A）：
              工作面分层、搜索岛、预览 / 属性双页签、五档断点自适应。
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
