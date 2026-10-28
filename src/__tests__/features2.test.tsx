import React from 'react';
import fs from 'node:fs';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../App';

const DEMO = '/home/user/EidoriaDemo';
const DATA = path.join(process.cwd(), 'data');

beforeAll(async () => {
  fs.mkdirSync(path.join(DATA, 'trash'), { recursive: true });
  fs.writeFileSync(path.join(DATA, 'recent.json'), '[]');
  fs.writeFileSync(path.join(DATA, 'favorites.json'), '[]');
  fs.writeFileSync(
    path.join(DATA, 'settings.json'),
    JSON.stringify({ theme: 'light', viewMode: 'grid', sortBy: 'name', sortDir: 'asc', showHidden: false, reduceMotion: false, confirmDelete: true }),
  );
  fs.writeFileSync(path.join(DATA, 'trash', 'manifest.json'), '[]');
  // deterministic fixtures
  fs.rmSync(path.join(DEMO, 'README.md'), { force: true });
  fs.rmSync(path.join(DEMO, 'pane_move.txt'), { force: true });
  fs.writeFileSync(path.join(DEMO, 'pane_move.txt'), 'move me across panes\n');
  fs.writeFileSync(path.join(DEMO, 'Documents', 'financial_report.txt'), 'Eidoria 本地账目摘要\n一季度支出 120\n');
  // index state persists across runs — reset it to a known good state
  const post = (url: string, body: unknown) => fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  await post('http://localhost:5173/api/index/settings', { paused: false, contentEnabled: true });
  await post('http://localhost:5173/api/index/rebuild', {});
});

afterAll(async () => {
  fs.rmSync(path.join(DEMO, 'README.md'), { force: true });
  fs.rmSync(path.join(DEMO, 'pane_move.txt'), { force: true });
  fs.rmSync(path.join(DEMO, 'Documents', 'pane_move.txt'), { force: true });
  // leave the index usable for the next run / the live demo
  const post = (url: string, body: unknown) => fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  await post('http://localhost:5173/api/index/settings', { paused: false, contentEnabled: true });
  await post('http://localhost:5173/api/index/rebuild', {});
});

async function renderApp() {
  const user = userEvent.setup();
  const utils = render(<App />);
  await screen.findAllByText('Eidoria', {}, { timeout: 10000 });
  await screen.findAllByText('user', {}, { timeout: 10000 });
  return { user, ...utils };
}

async function openCard(user: ReturnType<typeof userEvent.setup>, name: string) {
  const el = await screen.findByTitle(name, {}, { timeout: 10000 });
  await user.dblClick(el.closest('.filecard') ?? el);
}

async function gotoDocuments(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getAllByText('user')[0]);
  await openCard(user, 'EidoriaDemo');
  await openCard(user, 'Documents');
  await screen.findByTitle('README.md', {}, { timeout: 10000 });
}

describe('Eidoria v1.6 — search & index center (concept §4.1)', () => {
  it('searches across index roots with parent paths and snippets', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('搜索中心')[0]);
    const input = await screen.findByPlaceholderText('跨索引根搜索文件名或正文…');
    // same screen: results + index health + privacy card
    expect(screen.getByText('索引状态')).toBeInTheDocument();
    expect(screen.getByText('隐私卡')).toBeInTheDocument();
    expect(screen.getByText('语义 · 后续')).toBeInTheDocument();
    await user.type(input, 'financial');
    await waitFor(() => {
      expect(screen.getAllByText('financial_report.txt').length).toBeGreaterThan(0);
    }, { timeout: 10000 });
    // parent path visible on results
    expect(screen.getAllByText(/Documents/).length).toBeGreaterThan(0);
  });

  it('filters by type and time, and runs content search with snippets', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('搜索中心')[0]);
    const input = await screen.findByPlaceholderText('跨索引根搜索文件名或正文…');
    await user.type(input, 'report');
    await waitFor(() => {
      expect(screen.getAllByText(/financial_report/).length).toBeGreaterThan(0);
    }, { timeout: 10000 });
    // type filter: PDF keeps only the pdf
    const filters = document.querySelector('.sc-filters') as HTMLElement;
    await user.click(within(filters).getByText('PDF'));
    await waitFor(() => {
      expect(screen.getAllByText('financial_report.pdf').length).toBeGreaterThan(0);
      expect(screen.queryByText('financial_report.txt')).toBeNull();
    }, { timeout: 10000 });
    await user.click(within(filters).getByText('全部'));
    // content mode
    await user.click(screen.getByText('正文'));
    fireEvent.change(input, { target: { value: '账目摘要' } });
    await waitFor(() => {
      expect(document.querySelector('.searchrow__snippet')).toBeTruthy();
    }, { timeout: 15000 });
  });

  it('manages the index: rebuild, pause, content switch, clear', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('搜索中心')[0]);
    await screen.findByText('索引状态');
    // rebuild
    await user.click(screen.getByText('重建索引'));
    await waitFor(() => {
      expect(screen.getAllByText('索引已重建').length).toBeGreaterThan(0);
    }, { timeout: 30000 });
    // pause maintenance
    const privacy = screen.getByText('隐私卡').closest('.setcard') as HTMLElement;
    await user.click(within(privacy).getByRole('switch'));
    await waitFor(() => {
      expect(screen.getAllByText('维护已暂停').length).toBeGreaterThan(0);
    }, { timeout: 10000 });
    // clear index with confirm
    await user.click(within(privacy).getByText('清空索引'));
    await user.click(within(privacy).getByText('确认清空'));
    await waitFor(() => {
      expect(screen.getAllByText('尚未建立').length).toBeGreaterThan(0);
    }, { timeout: 10000 });
  });
});

describe('Eidoria v1.6 — dual pane (concept §5.1)', () => {
  it('opens A/B panes and sends files across panes', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByTitle('双窗格'));
    const panes = document.querySelectorAll('.pane');
    expect(panes.length).toBe(2);
    // active badge on the focused pane
    expect(document.querySelectorAll('.pane__badge.is-active').length).toBe(1);
    // move pane B up to EidoriaDemo via its own nav row
    const paneB = document.querySelectorAll('.pane')[1] as HTMLElement;
    await user.click(within(paneB).getByTitle('上一级'));
    await waitFor(() => {
      expect(within(paneB).getByTitle('pane_move.txt')).toBeInTheDocument();
    }, { timeout: 10000 });
    // pane B is now active (we clicked it)
    expect(paneB.classList.contains('is-active')).toBe(true);
    // select file in pane A and copy across
    const paneA = document.querySelectorAll('.pane')[0] as HTMLElement;
    await user.click(within(paneA).getByText('README.md'));
    // preview sidebar shows the cross-pane zone
    await user.click(screen.getByText('复制到对侧（1）'));
    await waitFor(() => {
      expect(fs.existsSync(path.join(DEMO, 'README.md'))).toBe(true);
    }, { timeout: 10000 });
    expect(fs.readFileSync(path.join(DEMO, 'README.md'), 'utf8')).toContain('万象屿');
  });

  it('moves files across panes (default move semantics)', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByTitle('双窗格'));
    const paneB = document.querySelectorAll('.pane')[1] as HTMLElement;
    await user.click(within(paneB).getByTitle('上一级'));
    await waitFor(() => {
      expect(within(paneB).getByTitle('pane_move.txt')).toBeInTheDocument();
    }, { timeout: 10000 });
    // select pane_move.txt in B and move to A (Documents)
    await user.click(within(paneB).getByText('pane_move.txt'));
    await user.click(screen.getByText('移动到对侧'));
    await waitFor(() => {
      expect(fs.existsSync(path.join(DEMO, 'Documents', 'pane_move.txt'))).toBe(true);
      expect(fs.existsSync(path.join(DEMO, 'pane_move.txt'))).toBe(false);
    }, { timeout: 10000 });
  });
});

describe('Eidoria v1.6 — text light editing & save review (concept §5.2)', () => {
  it('edits a text file with diff review and saves through the task queue', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByText('financial_report.txt'));
    // read-only first — edit entry appears
    const editBtn = await screen.findByText('编辑', {}, { timeout: 10000 });
    await user.click(editBtn);
    const area = document.querySelector('.ed-area') as HTMLTextAreaElement;
    expect(area).toBeTruthy();
    fireEvent.change(area, { target: { value: 'Eidoria 本地账目摘要\n修订后的二季度预算 340\n' } });
    expect(await screen.findByText('已修改')).toBeInTheDocument();
    await user.click(screen.getByText('保存'));
    const dlg = await screen.findByRole('dialog', { name: '保存前复核' });
    expect(within(dlg).getByText(/340/)).toBeInTheDocument();
    expect(dlg.textContent).toContain('+');
    await user.click(within(dlg).getByText('写入磁盘'));
    await waitFor(() => {
      expect(fs.readFileSync(path.join(DEMO, 'Documents', 'financial_report.txt'), 'utf8')).toContain('340');
    }, { timeout: 10000 });
    // save went through the task center
    await user.click(screen.getByTitle('任务中心'));
    const panel = await screen.findByRole('dialog', { name: '任务中心' });
    expect(within(panel).getAllByText(/保存 financial_report\.txt/).length).toBeGreaterThan(0);
  });

  it('warns about external modification before writing', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByText('financial_report.txt'));
    await user.click(await screen.findByText('编辑', {}, { timeout: 10000 }));
    const area = document.querySelector('.ed-area') as HTMLTextAreaElement;
    fireEvent.change(area, { target: { value: 'editor draft\n' } });
    // external write after preview
    const p = path.join(DEMO, 'Documents', 'financial_report.txt');
    fs.writeFileSync(p, 'external change\n');
    const future = new Date(Date.now() + 5000);
    fs.utimesSync(p, future, future);
    await user.click(screen.getByText('保存'));
    const dlg = await screen.findByRole('dialog', { name: '保存前复核' });
    expect(within(dlg).getByText(/被外部修改过/)).toBeInTheDocument();
    // still allowed to overwrite deliberately
    await user.click(within(dlg).getByText('写入磁盘'));
    await waitFor(() => {
      expect(fs.readFileSync(p, 'utf8')).toContain('editor draft');
    }, { timeout: 10000 });
  });
});

describe('Eidoria v1.6 — PDF workbench chrome (concept §5.2)', () => {
  it('opens the PDF workbench with outline and in-document search toolbar', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByText('financial_report.pdf'));
    await waitFor(() => {
      expect(document.querySelector('.pdfw__bar')).toBeTruthy();
    }, { timeout: 10000 });
    expect(screen.getByText('目录')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('文档内搜索…')).toBeInTheDocument();
  });
});

describe('Eidoria v1.6 — settings index admin', () => {
  it('exposes index management from settings', async () => {
    const { user } = await renderApp();
    await user.click(screen.getByText('设置'));
    expect(await screen.findByText('索引管理', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.getAllByText('重建索引').length).toBeGreaterThan(0);
    expect(screen.getAllByText('清空索引').length).toBeGreaterThan(0);
  });
});
