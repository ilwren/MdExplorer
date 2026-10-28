import React from 'react';
import { beforeAll, describe, expect, it } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import fs from 'node:fs';
import path from 'node:path';
import App from '../App';

const DEMO = '/home/user/EidoriaDemo';
const DATA = path.join(process.cwd(), 'data');

// Reset server-side state so tests are hermetic across runs.
beforeAll(() => {
  fs.mkdirSync(path.join(DATA, 'trash'), { recursive: true });
  fs.writeFileSync(path.join(DATA, 'recent.json'), '[]');
  fs.writeFileSync(path.join(DATA, 'favorites.json'), '[]');
  fs.writeFileSync(
    path.join(DATA, 'settings.json'),
    JSON.stringify({ theme: 'light', viewMode: 'grid', sortBy: 'name', sortDir: 'asc', showHidden: false, reduceMotion: false, confirmDelete: true }),
  );
  fs.writeFileSync(path.join(DATA, 'trash', 'manifest.json'), '[]');
});

async function renderApp() {
  const user = userEvent.setup();
  const utils = render(<App />);
  await screen.findAllByText('Eidoria', {}, { timeout: 10000 });
  await screen.findAllByText('user', {}, { timeout: 10000 });
  return { user, ...utils };
}

/** Open a folder card by its exact name (title attr unique to grid cards). */
async function openCard(user: ReturnType<typeof userEvent.setup>, name: string) {
  const el = await screen.findByTitle(name, {}, { timeout: 10000 });
  await user.dblClick(el.closest('.filecard') ?? el);
}

/** Navigate home -> volume -> EidoriaDemo -> Pictures -> Camera */
async function gotoCamera(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getAllByText('user')[0]);
  await openCard(user, 'EidoriaDemo');
  await openCard(user, 'Pictures');
  await openCard(user, 'Camera');
  await screen.findByTitle('IMG_20261001.jpg', {}, { timeout: 10000 });
}

describe('Eidoria web v1 smoke', () => {
  it('renders the shell with brand and drawer navigation', async () => {
    await renderApp();
    expect(screen.getAllByText('Eidoria').length).toBeGreaterThan(0);
    expect(screen.getByText('万象屿')).toBeInTheDocument();
    expect(screen.getAllByText('主页').length).toBeGreaterThan(0);
    expect(screen.getAllByText('回收站').length).toBeGreaterThan(0);
    expect(screen.getByText('设置')).toBeInTheDocument();
    expect(screen.getByText('存储区')).toBeInTheDocument();
    expect(screen.getAllByText('user').length).toBeGreaterThan(0);
  });

  it('home page shows storage card and categories', async () => {
    await renderApp();
    expect(await screen.findByText(/已用/)).toBeInTheDocument();
    expect(screen.getAllByText('图片').length).toBeGreaterThan(0);
    expect(screen.getAllByText('视频').length).toBeGreaterThan(0);
    expect(screen.getAllByText('文档').length).toBeGreaterThan(0);
  });

  it('navigates into folders and shows file cards', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('user')[0]);
    await openCard(user, 'EidoriaDemo');
    await openCard(user, 'Pictures');
    await openCard(user, 'Camera');
    await screen.findByTitle('IMG_20261001.jpg', {}, { timeout: 10000 });
    expect(screen.getByText('海边落日.jpg')).toBeInTheDocument();
  });

  it('single click selects and shows the preview pane with 预览/属性 tabs', async () => {
    const { user } = await renderApp();
    await gotoCamera(user);
    await user.click(screen.getByText('IMG_20261001.jpg'));
    const pane = document.querySelector('.pv-pane')! as HTMLElement;
    expect(within(pane).getByText('IMG_20261001.jpg')).toBeInTheDocument();
    expect(within(pane).getByText('预览')).toBeInTheDocument();
    expect(within(pane).getByText('属性')).toBeInTheDocument();
    await waitFor(() => {
      expect(pane.querySelector('.pv-image img')).toBeTruthy();
    });
  });

  it('properties tab shows metadata', async () => {
    const { user } = await renderApp();
    await gotoCamera(user);
    await user.click(screen.getByText('IMG_20261001.jpg'));
    const pane = document.querySelector('.pv-pane') as HTMLElement;
    await user.click(within(pane).getByText('属性'));
    expect(within(pane).getByText('修改时间')).toBeInTheDocument();
    expect(within(pane).getByText('位置')).toBeInTheDocument();
    await user.click(within(pane).getByText('预览'));
    await waitFor(() => {
      expect(pane.querySelector('.pv-facts')).toBeTruthy();
    });
  });

  it('search island finds files recursively', async () => {
    const { user } = await renderApp();
    await user.keyboard('{Control>}k{/Control}');
    const input = await screen.findByPlaceholderText('搜索文件名…');
    await user.type(input, 'beach');
    await screen.findByText('beach_vacation_2026.jpg', {}, { timeout: 15000 });
    expect(screen.getByText('Beach-00261.mp4')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() => {
      expect(screen.queryByPlaceholderText('搜索文件名…')).toBeNull();
    });
  });

  it('settings page toggles dark theme', async () => {
    const { user } = await renderApp();
    await user.click(screen.getByText('设置'));
    await screen.findByText('减少动态效果', {}, { timeout: 10000 });
    await user.click(screen.getByRole('tab', { name: /深色/ }));
    await waitFor(() => {
      expect(document.documentElement.dataset.theme).toBe('dark');
    });
    await user.click(screen.getByRole('tab', { name: /浅色/ }));
    await waitFor(() => {
      expect(document.documentElement.dataset.theme).toBe('light');
    });
  });

  it('delete flow shows impact dialog and moves to trash', async () => {
    const { user } = await renderApp();
    await fetch('/api/ops/newfile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: DEMO, name: 'disposable_test.txt', content: 'bye' }),
    });
    await user.click(screen.getAllByText('user')[0]);
    await openCard(user, 'EidoriaDemo');
    await user.click(await screen.findByTitle('disposable_test.txt'));
    await user.keyboard('{Delete}');
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('删除到回收站？')).toBeInTheDocument();
    expect(within(dialog).getByText(/移入回收站/)).toBeInTheDocument();
    await user.click(within(dialog).getByText('删除'));
    await waitFor(() => {
      expect(screen.queryByTitle('disposable_test.txt')).toBeNull();
    });
    await user.click(screen.getAllByText('回收站')[0]);
    await screen.findByText('disposable_test.txt', {}, { timeout: 10000 });
    await user.click(screen.getAllByText('还原')[0]);
    await waitFor(() => {
      expect(screen.queryByText('disposable_test.txt')).toBeNull();
    });
  });

  it('markdown preview renders rich content', async () => {
    const { user } = await renderApp();
    await user.click(screen.getAllByText('user')[0]);
    await openCard(user, 'EidoriaDemo');
    await openCard(user, 'Documents');
    await user.click(await screen.findByTitle('README.md'));
    const pane = document.querySelector('.pv-pane') as HTMLElement;
    await waitFor(() => {
      expect(pane.querySelector('.markdown-body')).toBeTruthy();
    }, { timeout: 10000 });
    expect(pane.querySelector('.markdown-body h1')?.textContent).toContain('Eidoria');
  });
});
