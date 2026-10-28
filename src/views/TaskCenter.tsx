import React from 'react';
import { useApp, type Task, type TaskItem } from '../state/app';
import { Icon } from '../ui/icons';
import { Button, IconButton } from '../ui/widgets';

const ITEM_META: Record<TaskItem['status'], { label: string; cls: string }> = {
  pending: { label: '排队', cls: 'is-pending' },
  running: { label: '进行中', cls: 'is-running' },
  done: { label: '完成', cls: 'is-done' },
  failed: { label: '失败', cls: 'is-failed' },
  skipped: { label: '跳过', cls: 'is-skipped' },
  kept: { label: '保留两者', cls: 'is-done' },
};

function progressOf(t: Task) {
  const total = t.items.length || 1;
  const finished = t.items.filter((i) => i.status !== 'pending' && i.status !== 'running').length;
  return Math.round((finished / total) * 100);
}

/** Mini task bar — sits above the status bar / bottom nav while work runs. */
export function MiniTaskBar() {
  const app = useApp();
  const running = app.tasks.find((t) => t.status === 'running');
  const recentFailed = app.tasks.find((t) => t.status === 'failed');
  const t = running ?? recentFailed;
  if (!t || app.taskPanelOpen) return null;
  const pct = progressOf(t);
  return (
    <div className="minitask minitask--wide" onClick={() => app.setTaskPanelOpen(true)}>
      <Icon name={t.icon} size={18} />
      <span className="minitask__label">{t.label}</span>
      {running ? (
        <span className="minitask__bar"><span style={{ width: `${Math.max(4, pct)}%` }} /></span>
      ) : (
        <span className="minitask__err">有失败项</span>
      )}
      <span className="minitask__pct">{pct}%</span>
      {running && (
        <button
          className="minitask__x"
          title="取消任务"
          onClick={(e) => { e.stopPropagation(); app.cancelTask(t.id); }}
        >
          <Icon name="close" size={16} />
        </button>
      )}
    </div>
  );
}

/** Task center panel — right sheet on desktop, bottom sheet feel on compact. */
export function TaskPanel() {
  const app = useApp();
  if (!app.taskPanelOpen) return null;
  const groups: { key: string; title: string; items: Task[] }[] = [
    { key: 'running', title: '进行中', items: app.tasks.filter((t) => t.status === 'running') },
    { key: 'failed', title: '失败', items: app.tasks.filter((t) => t.status === 'failed') },
    { key: 'done', title: '完成', items: app.tasks.filter((t) => t.status === 'done' || t.status === 'canceled') },
  ];
  return (
    <>
      <div className="scrim scrim--side" onMouseDown={(e) => e.target === e.currentTarget && app.setTaskPanelOpen(false)} />
      <aside className="taskpanel" role="dialog" aria-label="任务中心">
        <div className="taskpanel__head">
          <h2>任务中心</h2>
          <div className="taskpanel__head-actions">
            <Button variant="text" onClick={app.clearFinishedTasks}>清除记录</Button>
            <IconButton name="close" title="关闭" onClick={() => app.setTaskPanelOpen(false)} />
          </div>
        </div>
        {app.tasks.length === 0 && (
          <div className="empty">
            <Icon name="inventory_2" size={48} />
            <p className="empty__title">暂无任务</p>
            <p className="empty__desc">复制、移动、解压等操作会在这里显示进度</p>
          </div>
        )}
        {groups.map((g) => g.items.length > 0 && (
          <section key={g.key} className="taskpanel__group">
            <h3 className="taskpanel__group-title">{g.title}</h3>
            {g.items.map((t) => (
              <div key={t.id} className="taskcard">
                <div className="taskcard__head">
                  <Icon name={t.icon} size={18} />
                  <span className="taskcard__label">{t.label}</span>
                  <span className="taskcard__pct">{progressOf(t)}%</span>
                  {t.status === 'running' && (
                    <IconButton name="close" title="取消" variant="plain" onClick={() => app.cancelTask(t.id)} />
                  )}
                </div>
                <div className="taskcard__bar"><span style={{ width: `${Math.max(3, progressOf(t))}%` }} /></div>
                <div className="taskcard__items">
                  {t.items.slice(0, 8).map((it, i) => (
                    <div key={i} className={`taskitem ${ITEM_META[it.status].cls}`}>
                      <span className="taskitem__dot" />
                      <span className="taskitem__name">{it.name}</span>
                      <span className="taskitem__status">{it.detail ?? ITEM_META[it.status].label}</span>
                    </div>
                  ))}
                  {t.items.length > 8 && <div className="taskitem taskitem--more">…共 {t.items.length} 项</div>}
                </div>
              </div>
            ))}
          </section>
        ))}
      </aside>
    </>
  );
}
