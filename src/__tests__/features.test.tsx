import React from 'react';
import fs from 'node:fs';
import path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../App';

const DEMO = '/home/user/EidoriaDemo';
const DATA = path.join(process.cwd(), 'data');
const CONFLICT_DIR = path.join(DEMO, 'conflict_test');

beforeAll(() => {
  fs.mkdirSync(path.join(DATA, 'trash'), { recursive: true });
  fs.writeFileSync(path.join(DATA, 'recent.json'), '[]');
  fs.writeFileSync(path.join(DATA, 'favorites.json'), '[]');
  fs.writeFileSync(
    path.join(DATA, 'settings.json'),
    JSON.stringify({ theme: 'light', viewMode: 'grid', sortBy: 'name', sortDir: 'asc', showHidden: false, reduceMotion: false, confirmDelete: true }),
  );
  fs.writeFileSync(path.join(DATA, 'trash', 'manifest.json'), '[]');
  // deterministic fixtures
  fs.rmSync(path.join(DEMO, 'Documents', 'database_backup'), { recursive: true, force: true });
  fs.rmSync(CONFLICT_DIR, { recursive: true, force: true });
  fs.mkdirSync(CONFLICT_DIR, { recursive: true });
  fs.writeFileSync(path.join(CONFLICT_DIR, 'README.md'), 'existing readme for conflict\n');
});

afterAll(() => {
  fs.rmSync(path.join(DEMO, 'Documents', 'database_backup'), { recursive: true, force: true });
  fs.rmSync(CONFLICT_DIR, { recursive: true, force: true });
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

/** home -> user -> EidoriaDemo -> Documents */
async function gotoDocuments(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getAllByText('user')[0]);
  await openCard(user, 'EidoriaDemo');
  await openCard(user, 'Documents');
  await screen.findByTitle('README.md', {}, { timeout: 10000 });
}

/** Enter the zip workbench for Documents/database_backup.zip */
async function openZipWorkbench(user: ReturnType<typeof userEvent.setup>) {
  await gotoDocuments(user);
  await openCard(user, 'database_backup.zip');
  await screen.findByTitle('解压全部', {}, { timeout: 10000 });
}

/** Drill into a folder row inside the zip entry table (tree has its own copy of the name). */
async function openZipFolder(user: ReturnType<typeof userEvent.setup>, name: string) {
  const table = document.querySelector('.ztable') as HTMLElement;
  const row = within(table).getByText(name).closest('.ztable__row') as HTMLElement;
  await user.dblClick(row);
}

describe('Eidoria v1.5 features (tasks / zip / insights / content search)', () => {
  it('opens a zip archive as a workbench with tree, table and extract actions', async () => {
    const { user } = await renderApp();
    await openZipWorkbench(user);
    // root of the virtual folder shows the top-level folder
    expect(within(document.querySelector('.ztable') as HTMLElement).getByText('dump')).toBeInTheDocument();
    // folder tree at left
    expect(screen.getByText('根目录')).toBeInTheDocument();
    // drill into the folder to see entries (seed zip: dump/README.txt, dump/data/users.csv, dump/schema.sql)
    await openZipFolder(user, 'dump');
    await screen.findByText('schema.sql', {}, { timeout: 10000 });
    expect(screen.getByText('README.txt')).toBeInTheDocument();
    // zip-mode toolbar
    expect(screen.getByTitle('解压所选')).toBeInTheDocument();
    expect(screen.getByTitle('解压全部')).toBeInTheDocument();
    expect(screen.getByTitle('任务中心')).toBeInTheDocument();
  });

  it('extracts an entry through the extract dialog and records it as a task', async () => {
    const { user } = await renderApp();
    await openZipWorkbench(user);
    await openZipFolder(user, 'dump');
    await screen.findByText('README.txt', {}, { timeout: 10000 });
    await user.click(screen.getByText('README.txt'));
    await user.click(screen.getByTitle('解压所选'));
    const dlg = await screen.findByRole('dialog', { name: '解压所选（1 项）' });
    expect(within(dlg).getByText('开始解压')).toBeInTheDocument();
    await user.click(within(dlg).getByText('开始解压'));
    await waitFor(() => {
      expect(fs.existsSync(path.join(DEMO, 'Documents', 'database_backup', 'dump', 'README.txt'))).toBe(true);
    }, { timeout: 10000 });
    // task finished and is reachable in the task center
    await user.click(screen.getByTitle('任务中心'));
    const panel = await screen.findByRole('dialog', { name: '任务中心' });
    expect(within(panel).getAllByText('完成').length).toBeGreaterThan(0);
    expect(within(panel).getAllByText(/解压 1 项/).length).toBeGreaterThan(0);
  });

  it('resolves a paste conflict via the conflict dialog (keep both)', async () => {
    const { user } = await renderApp();
    await gotoDocuments(user);
    await user.click(screen.getByText('README.md'));
    await user.click(screen.getByTitle('复制'));
    // back to EidoriaDemo, then into conflict_test
    await user.click(screen.getByTitle('后退'));
    await openCard(user, 'conflict_test');
    await user.click(screen.getByTitle('粘贴'));
    const dlg = await screen.findByRole('dialog', { name: '发现同名项目' });
    expect(within(dlg).getByText('保留两者')).toBeInTheDocument();
    expect(within(dlg).getByText('替换')).toBeInTheDocument();
    expect(within(dlg).getByText('跳过')).toBeInTheDocument();
    await user.click(within(dlg).getByText('保留两者'));
    await waitFor(() => {
      expect(fs.existsSync(path.join(CONFLICT_DIR, 'README (2).md'))).toBe(true);
      expect(fs.existsSync(path.join(CONFLICT_DIR, 'README.md'))).toBe(true);
      expect(fs.readFileSync(path.join(CONFLICT_DIR, 'README.md'), 'utf8')).toContain('existing readme');
    }, { timeout: 10000 });
  });

  it('shows storage insights with stats, type share and folder map', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('存储洞察')[0]);
    expect(await screen.findByText('总用量', {}, { timeout: 10000 })).toBeInTheDocument();
    expect(screen.getByText('类型占比')).toBeInTheDocument();
    expect(screen.getByText('空间地图')).toBeInTheDocument();
    expect(screen.getByText(/大文件（前/)).toBeInTheDocument();
    expect(screen.getByText(/最近增长/)).toBeInTheDocument();
    expect(document.querySelector('.typebar')).toBeTruthy();
    expect(document.querySelectorAll('.treemap__block').length).toBeGreaterThan(0);
  });

  it('content search returns file bodies with context snippets', async () => {
    const { user } = await renderApp();
    await user.keyboard('{Control>}k{/Control}');
    const input = await screen.findByPlaceholderText('搜索文件名…');
    await user.click(screen.getByText('正文'));
    await user.type(input, '万象屿');
    await waitFor(() => {
      expect(document.querySelector('.searchrow__snippet')).toBeTruthy();
    }, { timeout: 15000 });
    expect(screen.getAllByText('README.md').length).toBeGreaterThan(0);
  });
});
