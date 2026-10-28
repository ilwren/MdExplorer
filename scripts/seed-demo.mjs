// Seeds ~/EidoriaDemo with sample data mirroring the concept mockups.
// Photos are copied from /home/user/concept-preview/demo-*.jpg when present;
// otherwise gradient SVG placeholders are generated.
import fsp from 'node:fs/promises';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import AdmZip from 'adm-zip';

const ROOT = process.env.EIDORIA_DEMO || path.join(os.homedir(), 'EidoriaDemo');
const ASSETS = '/home/user/concept-preview';

async function write(rel, content) {
  const p = path.join(ROOT, rel);
  await fsp.mkdir(path.dirname(p), { recursive: true });
  await fsp.writeFile(p, content);
}

function placeholderSvg(label, from, to) {
  return Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
    <stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/>
  </linearGradient></defs>
  <rect width="1200" height="800" fill="url(#g)"/>
  <text x="60" y="740" font-family="sans-serif" font-size="48" fill="rgba(255,255,255,0.85)">${label}</text>
</svg>`,
  );
}

function miniPdf(title) {
  // A tiny but valid one-page PDF.
  const content = `BT /F1 24 Tf 72 720 Td (${title}) Tj ET`;
  const objs = [
    '1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj',
    '2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj',
    '3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj',
    `4 0 obj << /Length ${content.length} >> stream\n${content}\nendstream endobj`,
    '5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets = [0];
  for (const o of objs) {
    offsets.push(pdf.length);
    pdf += o + '\n';
  }
  const xrefPos = pdf.length;
  pdf += `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n`;
  for (let i = 1; i <= objs.length; i++) pdf += String(offsets[i]).padStart(10, '0') + ' 00000 n \n';
  pdf += `trailer << /Size ${objs.length + 1} /Root 1 0 R >>\nstartxref\n${xrefPos}\n%%EOF`;
  return Buffer.from(pdf, 'latin1');
}

async function copyPhoto(src, rel, label, from, to) {
  const p = path.join(ASSETS, src);
  if (fs.existsSync(p)) {
    const buf = await fsp.readFile(p);
    await write(rel, buf);
  } else {
    await write(rel, placeholderSvg(label, from, to));
  }
}

async function main() {
  await fsp.rm(ROOT, { recursive: true, force: true });
  await fsp.mkdir(ROOT, { recursive: true });

  // Pictures — matches the concept art camera roll
  await copyPhoto('demo-IMG_20261001.jpg', 'Pictures/Camera/IMG_20261001.jpg', 'IMG_20261001', '#FF9F2E', '#D95F59');
  await copyPhoto('demo-beach_sunset.jpg', 'Pictures/Camera/海边落日.jpg', '海边落日', '#8D6BC4', '#2FA39B');
  await copyPhoto('demo-shore_view.jpg', 'Pictures/Camera/shore_view_01.jpg', 'shore_view', '#5B8FE0', '#2FA39B');
  await copyPhoto('demo-shore_view.jpg', 'Pictures/beach_vacation_2026.jpg', 'beach_vacation', '#2FA39B', '#5B8FE0');

  // Documents
  await write('Documents/README.md', `# Eidoria 万象屿

> 纯本地 · 跨平台文件资源管理器 · Material Design 3

Eidoria 由希腊语 *eidos*（形相）与 *-oria*（境域）拼合而成，意为「万象栖居之地」。

## 设计原则

- **本地文件优先**：导航与空态只出现本地卷与已知文件夹
- **工作面分层**：柔和底板承载白色工作区与预览卡片
- **搜索岛是全局手势**：迷你入口 + 悬浮搜索面板
- **预览永远双页签**：预览 / 属性

## 快捷键

| 快捷键 | 动作 |
|---|---|
| \`Ctrl + K\` | 打开搜索 |
| \`Ctrl + A\` | 全选 |
| \`Delete\` | 删除到回收站 |

\`\`\`ts
// 每个标签页拥有独立的会话状态
interface BrowserSession {
  path: string;
  selection: Set<string>;
  history: string[];
}
\`\`\`
`);
  await write('Documents/financial_report.txt', 'Q3 2026 财务摘要\n收入 1,240,000\n支出 890,000\n结余 350,000\n');
  await write('Documents/financial_report.pdf', miniPdf('Financial Report Q3 2026'));
  await write('Documents/project_propsal.pdf', miniPdf('Project Proposal - Eidoria v1'));
  await write('Documents/database_backup.zip', '');
  // build a real zip with entries
  const zip = new AdmZip();
  zip.addFile('dump/schema.sql', Buffer.from('CREATE TABLE files(id INTEGER PRIMARY KEY, name TEXT);'));
  zip.addFile('dump/data/users.csv', Buffer.from('id,name\n1,alice\n2,bob\n'));
  zip.addFile('dump/README.txt', Buffer.from('database backup bundle'));
  await write('Documents/database_backup.zip', zip.toBuffer());

  // Downloads
  await write('Downloads/setup_notes.txt', '安装说明：\n1. 下载安装包\n2. 校验 SHA-256\n3. 运行安装程序\n');
  await write('Downloads/website_asset.zip', '');
  const zip2 = new AdmZip();
  zip2.addFile('site/index.html', Buffer.from('<h1>Eidoria</h1>'));
  zip2.addFile('site/style.css', Buffer.from('body { font-family: sans-serif; }'));
  await write('Downloads/website_asset.zip', zip2.toBuffer());
  await write('Downloads/report_final.pdf', miniPdf('Final Report'));

  // Code
  await write('code/script.sh', `#!/usr/bin/env bash
# Eidoria demo script
set -euo pipefail

echo "scanning volumes..."
for d in /home/*; do
  echo "  -> $d"
done
echo "done."
`);
  await write('code/main_logo.svg', placeholderSvg('main_logo', '#3B97F6', '#175FC0'));
  await write('code/icon_set_v2.svg', placeholderSvg('icon_set', '#23C48D', '#079A72'));
  await write('code/app.config.json', JSON.stringify({ name: 'eidoria', theme: 'teal', breakpoints: [600, 840, 1200, 1600] }, null, 2));

  // Media (small placeholders — metadata preview only)
  await write('Music/opening_track.mp3', Buffer.from('ID3demo-audio-placeholder'));
  await write('Videos/project_demo.mp4', Buffer.from('demo-video-placeholder'));
  await write('Pictures/Camera/Beach-00261.mp4', Buffer.from('demo-video-placeholder'));

  // A hidden file to exercise the show-hidden toggle
  await write('.ds_store_note.txt', 'hidden system-ish file');

  console.log(`demo data seeded at ${ROOT}`);
}

main().catch((e) => { console.error(e); process.exit(1); });
