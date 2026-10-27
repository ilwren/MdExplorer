/* ==========================================================================
   Eidoria（万象屿）· 概念第一版
   按 docs/CONCEPT-DESIGN.md v3 + docs/RESPONSIVE-LAYOUT.md 实现的可交互原型。
   无构建步骤、无外部依赖：数据为固定虚构样例，不触碰真实文件系统。
   ========================================================================== */
(() => {
'use strict';

/* --------------------------------------------------------------------------
   0. 工具
   -------------------------------------------------------------------------- */
const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
const icon = (n, cls = '') => `<svg class="ico ${cls}" aria-hidden="true"><use href="#i-${n}"/></svg>`;
const esc = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const KB = 1024, MB = KB * 1024, GB = MB * 1024;
function fmtSize(n) {
  if (n == null) return '—';
  if (n >= GB) return (n / GB).toFixed(1).replace(/\.0$/, '') + ' GB';
  if (n >= MB) { const v = n / MB; return (v >= 10 ? v.toFixed(0) : v.toFixed(1).replace(/\.0$/, '')) + ' MB'; }
  if (n >= KB) return Math.round(n / KB) + ' KB';
  return n + ' B';
}
const TYPE_META = {
  folder:{ ico:'folder', label:'文件夹' }, image:{ ico:'image',  label:'JPEG 图像' },
  pdf:{ ico:'doc',       label:'PDF 文档' }, code:{ ico:'code',  label:'C# 源文件' },
  zip:{ ico:'archive',   label:'ZIP 压缩包' }, audio:{ ico:'music', label:'M4A 音频' },
  video:{ ico:'video',   label:'MP4 视频' }, text:{ ico:'text',  label:'纯文本' },
  md:{ ico:'doc',        label:'Markdown' }, doc:{ ico:'doc',    label:'文档' }
};
const TYPE_EXT = { image:'JPG', pdf:'PDF', code:'CS', zip:'ZIP', audio:'M4A', video:'MP4', text:'TXT', md:'MD', doc:'DOC' };

/* 风景缩略图：纯 SVG 生成，模拟概念图中的海岸/日落照片，不含任何外部资源 */
const PALETTES = [
  { sky:['#F6B183','#E97C86','#7C5E96'], sun:'#FFE3A6', sea:['#4A6C8C','#223F63'], hill:'#213A4C', haze:'#FFD9B0' },
  { sky:['#BFE7E3','#DFF0DC','#F4EBD3'], sun:'#FFF3C4', sea:['#3FA79C','#1B6E80'], hill:'#2E6B57', haze:'#D9F0E5' },
  { sky:['#8FA6D2','#C9A2C0','#F0B79C'], sun:'#FFDCB0', sea:['#54729C','#2A4270'], hill:'#2C3F5C', haze:'#E6BCD0' },
  { sky:['#DCEEF2','#AFD5E4','#8FC0D8'], sun:'#FFF6D8', sea:['#5A9FBF','#2E6F99'], hill:'#3F7A66', haze:'#E4F2F6' },
  { sky:['#FFD49A','#EC9276','#A96C82'], sun:'#FFF0BE', sea:['#5A6E88','#2B3F60'], hill:'#26354C', haze:'#FFC9A0' },
];
function photo(seed = 0, w = 400, h = 300) {
  const p = PALETTES[Math.abs(seed) % PALETTES.length];
  const sx = 90 + (seed * 53 % 220), sy = 96 + (seed * 29 % 46), sr = 24 + (seed % 3) * 7;
  const ridge = 168 + (seed * 13 % 26);
  const hill = 196 + (seed * 11 % 22);
  const uid = 'p' + Math.abs(seed) + '_' + w;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="${w}" height="${h}">
  <defs>
    <linearGradient id="${uid}s" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${p.sky[0]}"/><stop offset=".55" stop-color="${p.sky[1]}"/><stop offset="1" stop-color="${p.sky[2]}"/></linearGradient>
    <linearGradient id="${uid}e" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${p.sea[0]}"/><stop offset="1" stop-color="${p.sea[1]}"/></linearGradient>
    <radialGradient id="${uid}g"><stop offset="0" stop-color="${p.sun}" stop-opacity=".85"/><stop offset="1" stop-color="${p.sun}" stop-opacity="0"/></radialGradient>
  </defs>
  <rect width="400" height="300" fill="url(#${uid}s)"/>
  <circle cx="${sx}" cy="${sy + 22}" r="95" fill="url(#${uid}g)"/>
  <circle cx="${sx}" cy="${sy}" r="${sr}" fill="${p.sun}" opacity=".92"/>
  <path d="M0 ${ridge} Q70 ${ridge-18} 140 ${ridge-4} T280 ${ridge-10} T400 ${ridge-2} V210 H0 Z" fill="${p.haze}" opacity=".22"/>
  <rect y="${ridge + 14}" width="400" height="${300-ridge-14}" fill="url(#${uid}e)"/>
  <path d="M0 ${ridge+18} H400" stroke="${p.sun}" stroke-width="2" opacity=".28"/>
  <path d="M0 ${ridge+34} q60 -6 120 0 t120 0 t160 0" fill="none" stroke="${p.sun}" stroke-width="1.4" opacity=".16"/>
  <path d="M0 ${hill+14} Q60 ${hill-14} 118 ${hill+6} T238 ${hill-2} L280 ${hill+18} L400 ${hill+2} V300 H0 Z" fill="${p.hill}" opacity=".82"/>
  <path d="M0 ${hill+40} Q90 ${hill+22} 180 ${hill+44} T400 ${hill+34} V300 H0 Z" fill="${p.hill}"/>
  <path d="M0 ${ridge+16} H400 V${ridge+22} H0 Z" fill="${p.sun}" opacity=".12"/>
  </svg>`;
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg.replace(/\n\s*/g, ' '));
}

/* --------------------------------------------------------------------------
   1. 虚拟文件系统（固定样例数据，与 IMAGE-PROMPTS.md 的固定数据一致）
   -------------------------------------------------------------------------- */
const NODES = [];
function folder(path, parent, name, items, mtime) {
  NODES.push({ path, parent, name, kind:'folder', type:'folder', items, mtime, hidden:false });
}
function file(path, parent, name, type, size, mtime, extra = {}) {
  NODES.push({ path, parent, name, kind:'file', type, size, mtime, hidden:false, ...extra });
}
const T = '2026-10-2';
folder('root', null, '内部存储', 8, T+'5 09:12');
folder('Pictures', 'root', 'Pictures', 248, T+'4 18:40');
folder('Pictures/Camera', 'Pictures', 'Camera', 248, T+'4 18:40');
folder('Documents', 'root', 'Documents', 12, T+'2 11:05');
folder('Documents/Reports', 'Documents', 'Reports', 6, T+'1 16:20');
folder('下载', 'root', '下载', 8, T+'6 08:31');
folder('Backup', 'root', 'Backup', 36, '2026-09-28 22:10');
folder('Received', 'root', 'Received', 1204, '2026-09-14 10:02');

file('Pictures/Camera/IMG_20261001.jpg','Pictures/Camera','IMG_20261001.jpg','image',3.2*MB,T+'1 14:32',{thumb:1,w:4032,h:3024});
file('Pictures/Camera/IMG_20261011.jpg','Pictures/Camera','IMG_20261011.jpg','image',3.2*MB,T+'2 07:18',{thumb:2,w:4032,h:3024});
file('Pictures/Camera/海边落日.jpg','Pictures/Camera','海边落日.jpg','image',4.1*MB,T+'3 19:04',{thumb:3,w:3840,h:2160});
file('Pictures/Camera/Beach-00261.jpg','Pictures/Camera','Beach-00261.jpg','image',3.4*MB,T+'1 15:12',{thumb:4,w:4032,h:3024});
file('Pictures/Camera/IMG_20261001.pdf','Pictures/Camera','IMG_20261001.pdf','pdf',1.4*GB,'2026-08-11 12:00',{pages:24});
file('Pictures/Camera/code-markdown.png','Pictures/Camera','code-markdown.png','image',12*KB,T+'0 09:00',{thumb:5,w:800,h:600});
file('Pictures/Camera/source-0026.zip','Pictures/Camera','source-0026.zip','zip',22*MB,'2026-08-02 17:44',{entries:186});
file('Pictures/Camera/script.sh','Pictures/Camera','script.sh','code',12*KB,T+'0 09:00');
file('Pictures/Camera/icon.svg','Pictures/Camera','icon.svg','image',5*KB,T+'0 09:00',{thumb:2,w:512,h:512});
file('Pictures/Camera/音频 001.m4a','Pictures/Camera','音频 001.m4a','audio',22*MB,T+'0 09:00',{duration:'3:42'});

file('Pictures/IMG_20261001.jpg','Pictures','IMG_20261001.jpg','image',3.2*MB,T+'1 14:32',{thumb:1,w:4032,h:3024});
file('Pictures/设计规范 v3.pdf','Pictures','设计规范 v3.pdf','pdf',8.7*MB,T+'3 10:22',{pages:18});
file('Pictures/financial_report.pdf','Pictures','financial_report.pdf','pdf',1.4*GB,'2026-07-30 08:40',{pages:112});
file('Pictures/README.md','Pictures','README.md','md',12*KB,T+'2 13:00');
file('Pictures/database_backup.zip','Pictures','database_backup.zip','zip',22*MB,'2026-09-02 03:15',{entries:42});
file('Pictures/opening_track.m4a','Pictures','opening_track.m4a','audio',22*MB,'2026-06-18 20:12',{duration:'4:08'});
file('Pictures/shore_view.jpg','Pictures','shore_view.jpg','image',3.4*MB,T+'1 14:32',{thumb:4,w:4032,h:3024});
file('Pictures/IMG_20261011.jpg','Pictures','IMG_20261011.jpg','image',3.2*MB,T+'2 07:18',{thumb:2,w:4032,h:3024});
file('Pictures/海边落日.jpg','Pictures','海边落日.jpg','image',4.1*MB,T+'3 19:04',{thumb:3,w:3840,h:2160});
file('Pictures/Beach-00261.jpg','Pictures','Beach-00261.jpg','image',3.4*MB,T+'1 15:12',{thumb:4,w:4032,h:3024});
file('Pictures/code-markdown.png','Pictures','code-markdown.png','image',12*KB,T+'0 09:00',{thumb:5,w:800,h:600});
file('Pictures/main_logo.svg','Pictures','main_logo.svg','image',5*KB,T+'0 09:00',{thumb:2,w:512,h:512});
file('Pictures/icon_set_v2.svg','Pictures','icon_set_v2.svg','image',5*KB,'2026-09-30 09:00',{thumb:3,w:512,h:512});
file('Pictures/setup.sh','Pictures','setup.sh','code',12*KB,T+'0 09:00');
file('Pictures/project_demo.mp4','Pictures','project_demo.mp4','video',380*MB,'2026-09-08 21:33',{duration:'12:24'});

file('Documents/Program.cs','Documents','Program.cs','code',12*KB,T+'2 09:14');
file('Documents/meeting-notes.txt','Documents','meeting-notes.txt','text',18*KB,T+'2 15:48');
file('Documents/设计规范 v3.pdf','Documents','设计规范 v3.pdf','pdf',8.7*MB,T+'3 10:22',{pages:18});
file('Documents/Plan.pdf','Documents','Plan.pdf','pdf',4*KB,'2026-09-20 11:00',{pages:2});
file('Documents/Main.cs','Documents','Main.cs','code',12*KB,'2026-09-20 11:00');
file('Documents/Archive.zip','Documents','Archive.zip','zip',1.4*GB,'2026-09-19 16:30',{entries:1204});
file('Documents/Music.m4a','Documents','Music.m4a','audio',222*MB,'2026-09-18 08:00',{duration:'38:10'});
file('Documents/Memo.txt','Documents','Memo.txt','text',18*KB,'2026-09-17 14:00');
file('Documents/README.md','Documents','README.md','md',4*KB,'2026-09-16 10:00');

file('下载/IMG_20261001.jpg','下载','IMG_20261001.jpg','image',3.2*MB,T+'6 08:31',{thumb:1,w:4032,h:3024});
file('下载/report_final.pdf','下载','report_final.pdf','pdf',890*KB,T+'6 08:20',{pages:6});
file('下载/report_final.zip','下载','report_final.zip','zip',890*KB,T+'6 08:18',{entries:14});
file('下载/audio_player','下载','audio_player','audio',3*MB,T+'5 22:10',{duration:'1:04'});
file('下载/setup.sh','下载','setup.sh','code',12*KB,T+'4 07:00');
file('下载/main_logo.svg','下载','main_logo.svg','image',5*KB,T+'3 19:00',{thumb:3,w:512,h:512});
file('下载/演示视频.mp4','下载','演示视频.mp4','video',380*MB,T+'2 20:00',{duration:'12:24'});

file('root/backup-2026.zip','root','backup-2026.zip','zip',1.4*GB,'2026-09-28 22:10',{entries:1204});
file('root/Voice 001.m4a','root','Voice 001.m4a','audio',22*MB,'2026-09-26 18:40',{duration:'3:42'});
file('root/notes.txt','root','notes.txt','text',2*KB,'2026-09-25 09:00');
file('root/.hidden-config','root','.hidden-config','text',1*KB,'2026-09-24 09:00',{hidden:true});

const NODE_BY_PATH = new Map(NODES.map(n => [n.path, n]));
const VOLUMES = [
  { name:'C:', pct:44, ico:'drive' }, { name:'D:', pct:31, ico:'drive' }, { name:'SD 卡', pct:12, ico:'sd' }
];
const NAV = [
  { id:'主页',   ico:'home' }, { id:'收藏', ico:'star' }, { id:'最近', ico:'clock' },
  { id:'图片',   ico:'image' }, { id:'视频', ico:'video' }, { id:'文档', ico:'doc' },
  { id:'回收站', ico:'trash' }
];
const DEST_CWD = { '图片':'Pictures', '文档':'Documents' };
const STARRED = ['Pictures/Camera/IMG_20261001.jpg','Pictures/Camera/海边落日.jpg','Documents/Program.cs','下载/report_final.pdf'];
const RECENT = ['Pictures/Camera/IMG_20261001.jpg','Pictures/设计规范 v3.pdf','Documents/Program.cs','下载/report_final.pdf','Pictures/README.md','Pictures/shore_view.jpg'];

/* --------------------------------------------------------------------------
   2. 状态
   -------------------------------------------------------------------------- */
const BP = {
  compact : { w:390,  h:844,  label:'Compact · 390 × 844' },
  medium  : { w:768,  h:1024, label:'Medium · 768 × 1024' },
  expanded: { w:1100, h:760,  label:'Expanded · 1100 × 760' },
  large   : { w:1440, h:900,  label:'Large · 1440 × 900' },
  xlarge  : { w:1920, h:1080, label:'ExtraLarge · 1920 × 1080' },
};
let uid = 0;
const nextId = () => 'x' + (++uid);
function makeTab(title, cwd) {
  return { id:nextId(), title, cwd, history:[cwd], hi:0, virtual:null };
}
const state = {
  bp:'large', theme:'light', view:'grid', reduceMotion:false, zoom:1,
  tabs:[ makeTab('文档','Documents'), makeTab('下载','下载'), makeTab('图片','Pictures') ],
  activeTab:2,
  selected:new Set(), activePath:'Pictures/IMG_20261001.jpg',
  sort:{ key:'name', dir:1 }, showHidden:false,
  filter:'', searchMode:'filter', searchOpen:false,
  previewTab:'preview', previewVisible:true,
  floatingOpen:false, floatPos:{ x:null, y:null },
  tasks:[], clipboard:null,
  popover:null, dialog:null, snack:null, modalDrawer:false,
  homeMode:false,
};
const tab = () => state.tabs[state.activeTab];
const app = $('#app');
const stage = $('#stage');

/* --------------------------------------------------------------------------
   3. 取数据
   -------------------------------------------------------------------------- */
function pathChain(cwd) {
  const chain = []; let cur = cwd;
  while (cur && NODE_BY_PATH.has(cur)) { chain.unshift(NODE_BY_PATH.get(cur)); cur = NODE_BY_PATH.get(cur).parent; }
  return [{ path:'root', name:'内部存储' }, ...chain.filter(n => n.path !== 'root')];
}
function currentItems() {
  const t = tab();
  let list;
  if (t.virtual === '收藏')       list = STARRED.map(p => NODE_BY_PATH.get(p)).filter(Boolean);
  else if (t.virtual === '最近')  list = RECENT.map(p => NODE_BY_PATH.get(p)).filter(Boolean);
  else if (t.virtual === '视频')  list = NODES.filter(n => n.type === 'video');
  else if (t.virtual === '回收站')list = state.trashed || [];
  else                            list = NODES.filter(n => n.parent === t.cwd);
  if (!state.showHidden) list = list.filter(n => !n.hidden);
  if (state.filter) {
    const q = state.filter.toLowerCase();
    if (state.searchMode === 'recursive') list = NODES.filter(n => !n.hidden && n.name.toLowerCase().includes(q));
    else list = list.filter(n => n.name.toLowerCase().includes(q));
  }
  const dir = state.sort.dir;
  list = list.slice().sort((a, b) => {
    if (a.kind !== b.kind) return a.kind === 'folder' ? -1 : 1;
    let r = 0;
    switch (state.sort.key) {
      case 'size':  r = (a.size || a.items || 0) - (b.size || b.items || 0); break;
      case 'mtime': r = String(a.mtime).localeCompare(String(b.mtime)); break;
      case 'type':  r = a.type.localeCompare(b.type); break;
      default:      r = a.name.localeCompare(b.name, 'zh-CN', { numeric:true, sensitivity:'base' });
    }
    return r * dir;
  });
  return list;
}
function activeNode() { return NODE_BY_PATH.get(state.activePath) || null; }

/* --------------------------------------------------------------------------
   4. 渲染：标题栏 / 抽屉 / 导航行 / 工具栏
   -------------------------------------------------------------------------- */
function renderTabs() {
  $('#tabstrip').innerHTML = state.tabs.map((t, i) => `
    <div class="tab ${i === state.activeTab ? 'is-active' : ''}" data-act="tab" data-id="${t.id}" title="${esc(t.title)}">
      <svg class="tab__icon" aria-hidden="true"><use href="#i-${DEST_CWD[t.title] ? 'folder' : 'doc'}"/></svg>
      <span class="tab__name">${esc(t.title)}</span>
      <span class="tab__close" data-act="tab-close" data-id="${t.id}" title="关闭">${icon('close')}</span>
    </div>`).join('') + `<button class="tab-add" data-act="tab-add" title="新建标签页（Ctrl+T）">${icon('plus')}</button>`;
  $('#tabsBadge').textContent = state.tabs.length;
}
function renderDrawer() {
  const t = tab();
  const activeId = t.virtual || (t.cwd === 'Pictures' ? '图片' : t.cwd === 'Documents' ? '文档' : '主页');
  $('#drawerNav').innerHTML = NAV.map(n => `
    <button class="nav-item ${activeId === n.id ? 'is-active' : ''}" data-act="nav" data-id="${n.id}" title="${n.id}">
      ${icon(n.ico)}<span>${n.id}</span></button>`).join('');
  $('#volumes').innerHTML = VOLUMES.map(v => `
    <div class="vol" title="${esc(v.name)} 已用 ${v.pct}%">
      ${icon(v.ico)}<span class="vol__name">${esc(v.name)}</span><span class="vol__pct">${v.pct}%</span>
      <span class="vol__bar"><i style="width:${v.pct}%"></i></span>
    </div>`).join('');
  $('#drawerFooter').innerHTML = `
    <button class="nav-item" data-act="settings" title="设置">${icon('settings')}<span>设置</span></button>`;
  $('#bottomNav').innerHTML = `<div class="bottom-nav__inner">` +
    [['主页','home'],['最近','clock'],['收藏','star'],['设置','settings']].map(([id, ic]) => `
      <button class="bnav-item ${(tab().virtual || '主页') === id ? 'is-active' : ''}" data-act="${id === '设置' ? 'settings' : 'nav'}" data-id="${id}">
        <span class="bnav-item__pill">${icon(ic)}</span><span>${id}</span></button>`).join('') + `</div>`;
}
function renderNavButtons() {
  const t = tab();
  $('#navButtons').innerHTML = `
    <button class="icon-btn" data-act="back"    title="后退（Alt+←）" ${t.hi <= 0 ? 'disabled' : ''}>${icon('back')}</button>
    <button class="icon-btn" data-act="forward" title="前进（Alt+→）" ${t.hi >= t.history.length - 1 ? 'disabled' : ''}>${icon('forward')}</button>
    <button class="icon-btn" data-act="up"      title="上一级（Alt+↑）" ${!NODE_BY_PATH.get(t.cwd)?.parent ? 'disabled' : ''}>${icon('up')}</button>
    <button class="icon-btn" data-act="refresh" title="刷新（Ctrl+R）">${icon('refresh')}</button>`;
}
function renderCrumbs(edit = false) {
  const t = tab();
  if (edit) {
    $('#crumbs').innerHTML = `<input class="crumbs__edit" id="crumbEdit" value="${esc(t.cwd === 'root' ? '内部存储' : t.cwd)}" autocomplete="off">
      <span class="crumbs__blank"></span>`;
    const inp = $('#crumbEdit'); inp.focus(); inp.select();
  } else {
    const chain = t.virtual ? [{ path:'root', name:t.virtual }] : pathChain(t.cwd);
    $('#crumbs').innerHTML = chain.map((c, i) =>
      (i ? `<span class="crumb-sep">${icon('chevron-right')}</span>` : '') +
      `<button class="crumb" data-act="crumb" data-id="${esc(c.path)}">${esc(c.name)}</button>`
    ).join('') + `<span class="crumbs__blank" data-act="crumb-edit" title="输入路径（Ctrl+L）"></span>`;
  }
}
function renderToolbar() {
  const sel = state.selected.size;
  const views = [['grid','列表/网格/详细信息','grid'],['list','列表','list'],['details','详细信息','details']];
  const viewIco = { grid:'grid', list:'list', details:'details' }[state.view];
  $('#toolbar').innerHTML = `
    <div class="toolbar-group">
      <button class="icon-btn" data-act="new-folder" title="新建文件夹">${icon('newfolder')}</button>
      <button class="icon-btn" data-act="cut"    title="剪切（Ctrl+X）">${icon('cut')}</button>
      <button class="icon-btn" data-act="copy"   title="复制（Ctrl+C）">${icon('copy')}</button>
      <button class="icon-btn" data-act="paste"  title="粘贴（Ctrl+V）">${icon('paste')}</button>
      <button class="icon-btn" data-act="rename" title="重命名（F2）">${icon('pencil')}</button>
      <button class="icon-btn" data-act="delete" title="移入回收站（Delete）">${icon('trash')}</button>
      <span class="toolbar-sep"></span>
      <button class="icon-btn" data-act="sort" title="排序">${icon('sort')}</button>
      <button class="icon-btn" data-act="view" title="切换视图（Ctrl+1/2/3）">${icon(viewIco)}</button>
      <button class="icon-btn" data-act="hidden" title="显示隐藏文件">${icon(state.showHidden ? 'eye' : 'eyeoff')}</button>
      <button class="icon-btn ${state.previewVisible ? 'is-active' : ''}" data-act="preview-toggle" title="预览侧栏">${icon('eye')}</button>
    </div>
    <span class="toolbar-spacer"></span>
    <div class="toolbar-group">
      ${sel ? `<span class="chip is-active">${sel} 项已选</span>` : ''}
      <button class="icon-btn" data-act="share" title="共享">${icon('share')}</button>
    </div>`;
}
function renderSearchIsland() {
  $('#islandText').textContent = state.filter
    ? `${state.filter} · ${currentItems().length} 项`
    : (state.bp === 'large' || state.bp === 'xlarge' ? '搜索文件' : '');
}

/* --------------------------------------------------------------------------
   5. 渲染：文件视图
   -------------------------------------------------------------------------- */
function thumbHTML(n) {
  if (n.type === 'image' && n.thumb != null) return `<img src="${photo(n.thumb)}" alt="">`;
  return `<div class="type-well" data-t="${n.type}">${icon(TYPE_META[n.type].ico)}</div>`;
}
function metaOf(n) { return n.kind === 'folder' ? `${n.items} 个项目` : fmtSize(n.size); }
function renderFileView(direction) {
  const items = currentItems();
  const view = $('#fileView');
  $('#emptyState').hidden = items.length > 0;
  if (!items.length) {
    view.innerHTML = '';
    $('#emptyState').innerHTML = `${icon(state.filter ? 'search' : 'folder')}
      <h3>${state.filter ? '没有匹配的文件' : '这个文件夹是空的'}</h3>
      <p>${state.filter ? '试试其他关键词，或切换到递归搜索。' : '拖入文件，或从「新建」开始。'}</p>`;
  } else if (state.view === 'grid') {
    view.className = 'file-view';
    view.innerHTML = `<div class="file-grid">` + items.map((n, i) => `
      <button class="file-card ${state.selected.has(n.path) ? 'is-selected' : ''}" data-act="item" data-id="${esc(n.path)}"
              style="animation-delay:${Math.min(i * 12, 180)}ms" title="${esc(n.name)}">
        <div class="file-card__thumb">${thumbHTML(n)}<span class="file-card__check">${icon('check')}</span></div>
        <div class="file-card__name">${esc(n.name)}</div>
        <div class="file-card__meta">${esc(metaOf(n))}</div>
      </button>`).join('') + `</div>`;
  } else if (state.view === 'list') {
    view.className = 'file-view';
    view.innerHTML = `<div class="file-list">` + items.map((n, i) => `
      <div class="file-row ${state.selected.has(n.path) ? 'is-selected' : ''}" data-act="item" data-id="${esc(n.path)}"
           style="animation-delay:${Math.min(i * 8, 150)}ms">
        <div class="file-row__icon">${thumbHTML(n)}</div>
        <div class="file-row__name">${esc(n.name)}</div>
        <div class="file-row__meta">${esc(metaOf(n))}</div>
        <div class="file-row__date">${esc(n.mtime || '')}</div>
      </div>`).join('') + `</div>`;
  } else {
    view.className = 'file-view';
    const cols = [['name','名称'],['size','大小'],['type','类型'],['mtime','修改时间']];
    view.innerHTML = `<div class="file-details">
      <div class="details-head">` + cols.map(([k, l]) =>
        `<span data-act="sort-col" data-id="${k}">${l}${state.sort.key === k ? icon(state.sort.dir > 0 ? 'chevron-down' : 'up') : ''}</span>`).join('') + `</div>` +
      items.map((n, i) => `
        <div class="file-row file-row--details ${state.selected.has(n.path) ? 'is-selected' : ''}" data-act="item" data-id="${esc(n.path)}"
             style="animation-delay:${Math.min(i * 6, 130)}ms">
          <div class="file-row__name"><span class="file-row__icon">${thumbHTML(n)}</span>${esc(n.name)}</div>
          <div class="dcell">${esc(metaOf(n))}</div>
          <div class="dcell">${n.kind === 'folder' ? '文件夹' : (TYPE_EXT[n.type] || '文件')}</div>
          <div class="dcell">${esc(n.mtime || '')}</div>
        </div>`).join('') + `</div>`;
  }
  if (direction) {
    view.classList.remove('dir-next', 'dir-prev');
    void view.offsetWidth;
    view.classList.add('dir-' + direction);
  }
  const sortLabel = { name:'名称', size:'大小', type:'类型', mtime:'修改时间' }[state.sort.key];
  $('#viewHead').innerHTML = `
    <span class="view-head__count">${items.length} 个项目${state.filter ? ' · 已筛选' : ''}</span>
    <span class="view-head__spacer"></span>
    <span class="view-head__sort" data-act="sort">${icon('sort')} ${sortLabel} ${state.sort.dir > 0 ? '↑' : '↓'}</span>`;
  renderSearchIsland();
}

/* --------------------------------------------------------------------------
   6. 渲染：主页（Compact 概念图 03）
   -------------------------------------------------------------------------- */
function renderHome() {
  const view = $('#fileView');
  view.className = 'file-view';
  $('#emptyState').hidden = true;
  const cats = [['图片','image','Pictures'],['视频','video','视频'],['音乐','music','Received'],['文档','doc','Documents']];
  view.innerHTML = `
    <div class="home">
      <div class="home-card">
        <div class="home-card__top">${icon('drive')}<span>内部存储</span><b>68%</b></div>
        <div class="home-bar"><i style="width:68%"></i></div>
        <div class="home-card__sub">已用 41 GB / 128 GB</div>
      </div>
      <div class="home-cats">` + cats.map(([label, ic, dest]) => `
        <button class="home-cat" data-act="home-cat" data-id="${dest}"><span>${icon(ic)}</span><small>${label}</small></button>`).join('') + `
      </div>
      <div class="home-section">最近使用</div>
      <div class="file-grid">` + RECENT.slice(0, 8).map((p, i) => {
        const n = NODE_BY_PATH.get(p); if (!n) return '';
        return `<button class="file-card" data-act="item" data-id="${esc(n.path)}" style="animation-delay:${i*24}ms">
          <div class="file-card__thumb">${thumbHTML(n)}</div>
          <div class="file-card__name">${esc(n.name)}</div>
          <div class="file-card__meta">${esc(metaOf(n))}</div></button>`;
      }).join('') + `</div>
    </div>`;
  $('#viewHead').innerHTML = `<span class="view-head__count">主页 · 本地存储</span><span class="view-head__spacer"></span>
    <span class="view-head__sort" data-act="refresh">${icon('refresh')} 刷新</span>`;
}

/* --------------------------------------------------------------------------
   7. 渲染：预览 / 属性
   -------------------------------------------------------------------------- */
function figureHTML(n) {
  switch (n.type) {
    case 'image':
      return `<div class="preview__figure preview__figure--photo"><img src="${photo(n.thumb == null ? 1 : n.thumb, 800, 600)}" alt=""></div>`;
    case 'pdf':
      return `<div class="preview__figure preview__figure--doc"><div class="preview__docpage">
        <h4>${esc(n.name.replace(/\.pdf$/i, ''))}</h4>
        <p>Eidoria · 本地文件预览 · 第 1 / ${n.pages || 1} 页</p>
        <div class="lines"><i style="width:96%"></i><i style="width:88%"></i><i style="width:92%"></i><i style="width:70%"></i><i style="width:84%"></i></div>
        </div></div>`;
    case 'code':
      return `<div class="preview__code"><span class="c">// ${esc(n.name)}</span>
<span class="k">using</span> System;
<span class="k">using</span> System.IO;

<span class="k">public static class</span> Program {
  <span class="k">public static void</span> Main() {
    <span class="k">var</span> root = <span class="s">"内部存储"</span>;
    Console.WriteLine(root);
  }
}</div>`;
    case 'text': case 'md':
      return `<div class="preview__text"># ${esc(n.name)}

这是一份只读预览的示例文档。预览永不执行文件内容，也不会把本地路径发送到任何网络位置。

· 浏览、选择、操作、预览构成完整闭环
· 所有写操作进入任务中心，可取消、可追溯
· 搜索岛是全断点统一的全局手势</div>`;
    case 'zip':
      return `<div class="preview__figure preview__figure--doc"><div class="preview__text" style="background:transparent;padding:0">
压缩包条目 · 共 ${n.entries || 0} 项

  Documents/            12 个项目
  Pictures/            248 个项目
  README.md               4 KB
  Program.cs             12 KB
  backup-2026.zip       1.4 GB</div></div>`;
    case 'audio': case 'video':
      return `<div class="preview__figure preview__figure--photo" style="aspect-ratio:16/9">
        <img src="${photo(n.type === 'video' ? 3 : 2, 800, 450)}" alt=""></div>`;
    default:
      return `<div class="preview__figure preview__figure--photo" style="aspect-ratio:4/3;background:var(--surface-low)">
        <div class="type-well" data-t="${n.type}" style="width:72px;height:72px">${icon(TYPE_META[n.type].ico)}</div></div>`;
  }
}
function factsHTML(n) {
  const facts = [];
  if (n.w) facts.push(['expand', `${n.w}×${n.h}`]);
  if (n.kind === 'folder') facts.push(['folder', `${n.items} 个项目`]);
  else facts.push(['drive', fmtSize(n.size)]);
  if (n.pages) facts.push(['doc', `${n.pages} 页`]);
  if (n.duration) facts.push(['play', n.duration]);
  facts.push(['calendar', String(n.mtime || '').split(' ')[0]]);
  return `<div class="preview__facts">` + facts.map(([ic, v]) =>
    `<span class="fact">${icon(ic)}${esc(v)}</span>`).join('') + `</div>`;
}
function propertiesHTML(n) {
  const rows = [
    ['名称', n.name, true],
    ['位置', n.parent === 'root' ? '内部存储' : (n.parent || '内部存储'), true],
    ['类型', n.kind === 'folder' ? '文件夹' : TYPE_META[n.type].label, false],
    ['大小', n.kind === 'folder' ? `${n.items} 个项目` : fmtSize(n.size), false],
    ['修改时间', n.mtime || '—', false],
    ['权限', '只读（本地）', false],
  ];
  return `<div class="prop-group"><h5>基本信息</h5>` + rows.map(([k, v, copy]) =>
    `<div class="prop-row"><span class="prop-row__k">${k}</span>
      <span class="prop-row__v ${copy ? 'is-copy' : ''}" ${copy ? 'data-act="copy-path" data-id="' + esc(n.path) + '"' : ''}>${esc(v)}</span></div>`).join('') +
    `</div><div class="prop-group"><h5>校验和</h5>
      <div class="checksum-card"><small>SHA-256</small>
      <code>${fakeHash(n.path)}</code></div></div>`;
}
function fakeHash(s) {
  let h1 = 0x811c9dc5, h2 = 0x9e3779b9;
  for (let i = 0; i < s.length; i++) { h1 = Math.imul(h1 ^ s.charCodeAt(i), 16777619); h2 = Math.imul(h2 + s.charCodeAt(i), 2246822519); }
  const hex = x => (x >>> 0).toString(16).padStart(8, '0');
  return hex(h1) + hex(h2) + hex(h1 ^ h2) + hex(h2 ^ (h1 >>> 3));
}
function previewInner(n, compact) {
  if (!n) return `<div class="empty-state" style="padding:38px 12px">${icon('eye')}
    <h3>选择一个文件以预览</h3><p>预览与属性始终是两个页签。</p></div>`;
  const tabsHTML = `<div class="${compact ? 'floating__tabs' : 'preview__tabs'}">
    <button class="ptab ${state.previewTab === 'preview' ? 'is-active' : ''}" data-act="ptab" data-id="preview">预览</button>
    <button class="ptab ${state.previewTab === 'properties' ? 'is-active' : ''}" data-act="ptab" data-id="properties">属性</button>
  </div>`;
  const body = state.previewTab === 'preview'
    ? (n.kind === 'folder' ? propertiesHTML(n) : figureHTML(n) + factsHTML(n))
    : propertiesHTML(n);
  const footer = `<div class="${compact ? 'floating__footer' : 'preview__footer'}">
      <button class="filled-btn" data-act="open">${icon('open')} 打开</button>
      <button class="icon-btn" data-act="share" title="共享">${icon('share')}</button>
      <button class="icon-btn" data-act="item-menu" title="更多">${icon('more')}</button>
    </div>`;
  return (compact ? '' : `<div class="preview__title" title="${esc(n.name)}">${esc(n.name)}</div>`) + tabsHTML +
    `<div class="${compact ? 'floating__body' : 'preview__body'}">${body}</div>` + footer;
}
function renderPreview() {
  const n = activeNode();
  const useFloat = (state.bp === 'compact' || state.bp === 'medium') || !state.previewVisible;
  const pane = $('#previewPane');
  pane.innerHTML = previewInner(n, false);
  pane.style.display = (useFloat || !n) ? (n ? 'none' : 'flex') : 'flex';
  if (!n && !useFloat) pane.innerHTML = previewInner(null, false);
  const fl = $('#floating');
  if (state.floatingOpen && n && useFloat) {
    fl.hidden = false;
    fl.innerHTML = `<div class="floating__handle" id="floatHandle"><i></i></div>
      <div class="floating__head"><span class="floating__title" title="${esc(n.name)}">${esc(n.name)}</span>
        <button class="icon-btn" data-act="float-close" title="关闭">${icon('close')}</button></div>` +
      previewInner(n, true);
    positionFloating();
  } else { fl.hidden = true; fl.innerHTML = ''; }
}
function positionFloating() {
  const fl = $('#floating'); if (fl.hidden) return;
  const w = BP[state.bp].w, h = BP[state.bp].h;
  const fw = state.bp === 'compact' ? 268 : 340;
  const p = state.floatPos;
  fl.style.left = (p.x != null ? p.x : (state.bp === 'compact' ? w - fw - 12 : w - fw - 26)) + 'px';
  fl.style.top  = (p.y != null ? p.y : (state.bp === 'compact' ? h * 0.30 : 116)) + 'px';
  /* 底部留出 NavigationBar + 状态条，浮窗不压住主导航 */
  const bottomGuard = state.bp === 'compact' ? 116 : 20;
  const maxTop = h - Math.min(fl.offsetHeight || 400, h * 0.76) - bottomGuard;
  if (parseFloat(fl.style.top) > maxTop) fl.style.top = Math.max(70, maxTop) + 'px';
  if (parseFloat(fl.style.left) < 8) fl.style.left = '8px';
  if (parseFloat(fl.style.left) + fw > w - 8) fl.style.left = (w - fw - 8) + 'px';
}
function renderStatusBar() {
  const items = currentItems();
  const running = state.tasks.filter(t => t.status === 'running').length;
  $('#statusBar').innerHTML = `
    <span>${items.length} 个项目</span><span class="status-dot"></span>
    <span>本地 · 无网络</span><span class="spacer"></span>
    <span class="status-task" data-act="tasks">${icon('task')} ${running ? running + ' 项进行中' : '任务中心'}
      ${running ? `<span class="badge" style="position:static;width:auto;padding:0 5px">${running}</span>` : ''}</span>`;
}

/* --------------------------------------------------------------------------
   8. 浮层：搜索 / 菜单 / 任务 / 对话框 / Snackbar
   -------------------------------------------------------------------------- */
function renderSearchResults() {
  const q = state.filter.trim().toLowerCase();
  const box = $('#searchResults');
  $('#searchCount').textContent = q ? '' : `${currentItems().length} 项`;
  if (!q) { box.innerHTML = `<div class="sr-empty">输入关键词筛选当前文件夹，回车进行递归搜索。</div>`; return; }
  const list = state.searchMode === 'recursive'
    ? NODES.filter(n => !n.hidden && n.name.toLowerCase().includes(q))
    : currentItems();
  $('#searchCount').textContent = `${list.length} 项`;
  box.innerHTML = list.length ? list.slice(0, 40).map(n => `
    <div class="sr-item" data-act="sr-item" data-id="${esc(n.path)}">
      <div class="sr-item__icon"><div class="type-well" data-t="${n.type}">${icon(TYPE_META[n.type].ico)}</div></div>
      <div class="sr-item__main"><div class="sr-item__name">${esc(n.name)}</div>
        <div class="sr-item__path">${esc(n.parent === 'root' ? '内部存储' : n.parent)}</div></div>
      <span style="font-size:11px;color:var(--on-surface-variant)">${esc(metaOf(n))}</span>
    </div>`).join('') : `<div class="sr-empty">没有找到匹配项。</div>`;
}
function openSearch() {
  state.searchOpen = true;
  $('#searchPanel').hidden = false;
  /* 关闭时保留的输入词，重新展开后恢复为当前筛选，保证岛、面板、列表三者一致 */
  state.filter = $('#searchInput').value;
  renderSearchResults(); renderBrowserContent(); renderSearchIsland();
  setTimeout(() => $('#searchInput').focus(), 20);
}
/* 关闭只收起面板、保留输入词；列表筛选随面板一起结束，避免"关掉了却还在过滤" */
function closeSearch() {
  state.searchOpen = false;
  $('#searchPanel').hidden = true;
  if (state.filter) {
    state.filter = '';
    renderBrowserContent(); renderStatusBar(); renderSearchIsland();
  }
}
function appCoords(el) {
  const r = el.getBoundingClientRect(), a = app.getBoundingClientRect(), z = state.zoom;
  return { left:(r.left - a.left)/z, top:(r.top - a.top)/z, bottom:(r.bottom - a.top)/z, right:(r.right - a.left)/z, w:r.width/z };
}
function showPopover(anchor, items, opts = {}) {
  closePopover();
  const p = $('#popover');
  const cols = opts.cols && state.bp !== 'compact' && state.bp !== 'medium';
  let html = '';
  if (cols) {
    html = `<div class="popover__cols">` + items.map(group => `<div class="popover__group">
      ${group.title ? `<div class="popover__group-title">${esc(group.title)}</div>` : ''}` +
      (group.items ? group.items : [group]).map(menuItemHTML).join('') + `</div>`).join('') + `</div>`;
  } else {
    html = items.map(group => group.items
      ? (group.title ? `<div class="popover__group-title">${esc(group.title)}</div>` : '') + group.items.map(menuItemHTML).join('')
      : menuItemHTML(group)).join('<div class="menu-sep"></div>');
  }
  p.innerHTML = html; p.hidden = false;
  const c = appCoords(anchor);
  const pw = p.offsetWidth, ph = p.offsetHeight;
  p.style.left = Math.max(8, Math.min(c.left, BP[state.bp].w - pw - 8)) + 'px';
  let top = c.bottom + 6;
  if (top + ph > BP[state.bp].h - 8) top = Math.max(8, c.top - ph - 6);
  p.style.top = top + 'px';
  state.popover = { items, opts };
}
function menuItemHTML(m) {
  if (m === '-') return `<div class="menu-sep"></div>`;
  return `<button class="menu-item ${m.danger ? 'is-danger' : ''} ${m.checked ? 'is-checked' : ''}"
      data-act="menu" data-id="${esc(m.id)}">${m.icon ? icon(m.icon) : ''}<span>${esc(m.label)}</span>
      ${m.key ? `<span class="menu-item__key">${esc(m.key)}</span>` : ''}</button>`;
}
function closePopover() { $('#popover').hidden = true; state.popover = null; }
function menuActions() { return state.popover ? state.popover.items.flatMap(g => g.items ? g.items : [g]).filter(m => m !== '-') : []; }

function renderTasks() {
  const box = $('#taskCenter');
  if (box.hidden) return;
  box.innerHTML = `<div class="task-center__head">${icon('task')}<h4>任务中心</h4>
      <button class="icon-btn" data-act="tasks-close" title="关闭">${icon('close')}</button></div>
    <div class="task-center__list">` + (state.tasks.length ? state.tasks.map(t => `
      <div class="task-row">
        <div class="task-row__icon">${icon(t.status === 'done' ? 'checkcircle' : t.status === 'failed' ? 'error' : 'refresh')}</div>
        <div class="task-row__main"><div class="task-row__name">${esc(t.name)}</div>
          <div class="task-row__meta">${esc(t.detail)} · ${t.status === 'running' ? Math.round(t.progress) + '%' : t.status === 'done' ? '已完成' : '失败'}</div>
          ${t.status === 'running' ? `<div class="task-row__bar"><i style="width:${t.progress}%"></i></div>` : ''}</div>
      </div>`).join('') : `<div class="task-empty">暂无任务。所有写操作都会在这里留下记录。</div>`) + `</div>`;
}
function addTask(name, detail, onDone) {
  const t = { id:nextId(), name, detail, status:'running', progress:0 };
  state.tasks.unshift(t); if (state.tasks.length > 20) state.tasks.pop();
  renderTasks(); renderStatusBar();
  const timer = setInterval(() => {
    t.progress += 9 + Math.random() * 16;
    if (t.progress >= 100) {
      t.progress = 100; t.status = 'done'; clearInterval(timer);
      if (onDone) onDone();
      showSnack(`${name} · 已完成`);
    }
    renderTasks(); renderStatusBar();
  }, 160);
}
function showSnack(text, actionLabel, action) {
  const el = $('#snackbar');
  el.innerHTML = `<span class="snackbar__text">${esc(text)}</span>` +
    (actionLabel ? `<button class="snackbar__action" data-act="snack-action">${esc(actionLabel)}</button>` : '') +
    `<button class="icon-btn" style="width:26px;height:26px;color:#cfe3db" data-act="snack-close">${icon('close')}</button>`;
  el.hidden = false;
  state.snack = action || null;
  clearTimeout(showSnack._t);
  showSnack._t = setTimeout(() => { el.hidden = true; state.snack = null; }, actionLabel ? 5200 : 3200);
}
function showDialog(html, onMount) {
  const bd = $('#dialogBackdrop'), d = $('#dialog');
  d.innerHTML = html; bd.hidden = false; state.dialog = true;
  if (onMount) onMount(d);
  const inp = d.querySelector('input[type=text]'); if (inp) { inp.focus(); inp.select(); }
}
function closeDialog() { $('#dialogBackdrop').hidden = true; state.dialog = false; }
function dialogActions(okLabel, act, danger) {
  return `<div class="dialog__actions">
    <button class="text-btn" data-act="dialog-cancel">取消</button>
    <button class="filled-btn" data-act="${act}" ${danger ? 'style="background:var(--error);color:#fff"' : ''}>${esc(okLabel)}</button></div>`;
}

/* --------------------------------------------------------------------------
   9. 动作
   -------------------------------------------------------------------------- */
function navigate(cwd, virtual = null, direction = 'next') {
  const t = tab();
  if (virtual) { t.virtual = virtual; }
  else { t.virtual = null; t.cwd = cwd; }
  t.history = t.history.slice(0, t.hi + 1);
  t.history.push(cwd); t.hi = t.history.length - 1;
  if (t.history.length > 30) { t.history.shift(); t.hi--; }
  state.selected.clear(); state.filter = '';
  state.floatingOpen = false;
  renderAll(direction);
}
function goHistory(delta) {
  const t = tab(); const ni = t.hi + delta;
  if (ni < 0 || ni >= t.history.length) return;
  t.hi = ni; t.cwd = t.history[ni]; t.virtual = null;
  state.selected.clear(); state.filter = '';
  renderAll(delta > 0 ? 'next' : 'prev');
}
function goUp() {
  const t = tab(); const node = NODE_BY_PATH.get(t.cwd);
  if (node && node.parent) navigate(node.parent, null, 'prev');
  else showSnack('已经在根目录');
}
function selectItem(path, additive = false) {
  if (additive) { state.selected.has(path) ? state.selected.delete(path) : state.selected.add(path); }
  else { state.selected.clear(); state.selected.add(path); }
  state.activePath = path;
  const n = NODE_BY_PATH.get(path);
  if (n && n.kind === 'folder') state.previewTab = 'properties';
  else state.previewTab = 'preview';
  if (state.bp === 'compact' || state.bp === 'medium' || !state.previewVisible) state.floatingOpen = true;
  renderBrowserContent(); renderPreview(); renderToolbar(); renderStatusBar();
}
function openItem(n) {
  if (!n) return;
  if (n.kind === 'folder') navigate(n.path, null, 'next');
  else if (state.bp === 'compact' || state.bp === 'medium' || !state.previewVisible) { state.floatingOpen = true; renderPreview(); }
  else showSnack(`已用系统默认应用打开「${n.name}」`);
}
function newItemDialog(kind) {
  const isFolder = kind === 'folder';
  showDialog(`<h3>新建${isFolder ? '文件夹' : '文件'}</h3>
    <p>名称将创建在当前文件夹。非法字符会自动过滤。</p>
    <input type="text" id="dlgName" class="dialog__input" placeholder="${isFolder ? '新建文件夹' : '未命名.txt'}" value="${isFolder ? '新建文件夹' : '未命名.txt'}">
    ${dialogActions('创建', 'dialog-new-ok')}`, d => {
    d.dataset.kind = kind;
  });
}
function createItem(kind) {
  const inp = $('#dlgName'); if (!inp) return;
  const name = inp.value.trim().replace(/[\\/:*?"<>|]/g, '');
  if (!name) { showSnack('名称不能为空'); return; }
  const t = tab(); const parent = t.virtual ? 'root' : t.cwd;
  const path = (parent === 'root' ? '' : parent + '/') + name;
  if (NODE_BY_PATH.has(path)) { showSnack('同名项目已存在'); return; }
  const n = kind === 'folder'
    ? { path, parent, name, kind:'folder', type:'folder', items:0, mtime:'刚刚', hidden:false }
    : { path, parent, name, kind:'file', type:'text', size:0, mtime:'刚刚', hidden:false };
  NODES.push(n); NODE_BY_PATH.set(path, n);
  closeDialog(); state.filter = '';
  addTask(`新建${kind === 'folder' ? '文件夹' : '文件'}`, name);
  state.selected.clear(); state.selected.add(path); state.activePath = path;
  renderAll();
}
function renameDialog() {
  const n = activeNode(); if (!n) { showSnack('请先选择一个文件'); return; }
  showDialog(`<h3>重命名</h3><p>扩展名将保留；重命名会进入任务中心。</p>
    <input type="text" id="dlgName" class="dialog__input" value="${esc(n.name)}">
    ${dialogActions('重命名', 'dialog-rename-ok')}`);
}
function doRename() {
  const inp = $('#dlgName'); if (!inp) return;
  const n = activeNode(); if (!n) return;
  const nn = inp.value.trim().replace(/[\\/:*?"<>|]/g, '');
  if (!nn || nn === n.name) { closeDialog(); return; }
  const oldPath = n.path, oldName = n.name;
  const parent = n.parent; const np = (parent === 'root' ? '' : parent + '/') + nn;
  if (NODE_BY_PATH.has(np)) { showSnack('同名项目已存在'); return; }
  n.name = nn; n.path = np; NODE_BY_PATH.delete(oldPath); NODE_BY_PATH.set(np, n);
  NODES.forEach(x => { if (x.parent === oldPath) x.parent = np; });
  state.activePath = np; state.selected.delete(oldPath); state.selected.add(np);
  closeDialog();
  addTask('重命名', `${oldName} → ${nn}`);
  renderAll();
}
function deleteConfirm() {
  const paths = [...state.selected]; if (!paths.length) { showSnack('请先选择文件'); return; }
  const names = paths.slice(0, 3).map(p => NODE_BY_PATH.get(p)?.name).filter(Boolean).join('、');
  showDialog(`<h3>移入回收站</h3>
    <div class="dialog__warning">${icon('warning')}
      <span>将移入 ${paths.length} 个项目${names ? '：' + esc(names) + (paths.length > 3 ? ' 等' : '') : ''}。可在回收站还原；Android 上删除不可恢复。</span></div>
    ${dialogActions('移入回收站', 'dialog-delete-ok', true)}`);
}
function doDelete() {
  const paths = [...state.selected];
  state.trashed = state.trashed || [];
  paths.forEach(p => { const n = NODE_BY_PATH.get(p); if (n) state.trashed.push({ ...n }); });
  paths.forEach(p => { NODE_BY_PATH.delete(p); });
  for (let i = NODES.length - 1; i >= 0; i--) if (paths.includes(NODES[i].path)) NODES.splice(i, 1);
  state.selected.clear(); state.activePath = null;
  closeDialog(); closePopover();
  addTask('移入回收站', `${paths.length} 个项目`);
  showSnack(`已移入 ${paths.length} 个项目`, '撤销', () => {
    (state.trashed || []).slice(-paths.length).forEach(n => { NODES.push(n); NODE_BY_PATH.set(n.path, n); });
    renderAll(); showSnack('已撤销删除');
  });
  renderAll();
}
function copyOrCut(mode) {
  const paths = [...state.selected];
  if (!paths.length) { showSnack('请先选择文件'); return; }
  state.clipboard = { mode, paths };
  showSnack(`${mode === 'cut' ? '剪切' : '复制'} ${paths.length} 个项目`);
}
function doPaste() {
  if (!state.clipboard) { showSnack('剪贴板为空'); return; }
  const { mode, paths } = state.clipboard;
  const dest = tab().virtual ? 'root' : tab().cwd;
  let count = 0;
  paths.forEach(p => {
    const n = NODE_BY_PATH.get(p); if (!n || n.parent === dest) return;
    const name = n.name; const np = (dest === 'root' ? '' : dest + '/') + name;
    if (NODE_BY_PATH.has(np)) return;
    if (mode === 'cut') { n.parent = dest; n.path = np; NODE_BY_PATH.delete(p); NODE_BY_PATH.set(np, n); }
    else { const c = { ...n, path:np, parent:dest, mtime:'刚刚' }; NODES.push(c); NODE_BY_PATH.set(np, c); }
    count++;
  });
  if (mode === 'cut') state.clipboard = null;
  addTask(mode === 'cut' ? '移动' : '复制', `${count} 个项目 → ${dest === 'root' ? '内部存储' : dest}`);
  renderAll();
}
function openSortMenu(anchor) {
  const opts = [['name','名称'],['size','大小'],['type','类型'],['mtime','修改时间']];
  const items = [
    { title:'排序依据', items: opts.map(([k, l]) => ({ id:'sort-' + k, label:l, icon: state.sort.key === k ? 'check' : '', checked: state.sort.key === k })) },
    { title:'方向', items: [
      { id:'sort-asc',  label:'升序', icon:'chevron-down', checked: state.sort.dir > 0 },
      { id:'sort-desc', label:'降序', icon:'up', checked: state.sort.dir < 0 },
    ]},
    { title:'视图', items: [
      { id:'view-grid',    label:'网格',       icon:'grid',    checked: state.view === 'grid' },
      { id:'view-list',    label:'列表',       icon:'list',    checked: state.view === 'list' },
      { id:'view-details', label:'详细信息',   icon:'details', checked: state.view === 'details' },
    ]},
  ];
  showPopover(anchor, items, { cols:true });
}
function openOverflowMenu(anchor) {
  showPopover(anchor, [
    { id:'new-folder', label:'新建文件夹', icon:'newfolder', key:'Ctrl+Shift+N' },
    { id:'refresh',    label:'刷新',       icon:'refresh',   key:'Ctrl+R' },
    '-',
    { id:'hidden',     label: state.showHidden ? '隐藏隐藏文件' : '显示隐藏文件', icon: state.showHidden ? 'eye' : 'eyeoff' },
    { id:'preview-toggle', label: state.previewVisible ? '关闭预览侧栏' : '打开预览侧栏', icon:'eye' },
    '-',
    { id:'tasks',      label:'任务中心',   icon:'task' },
    { id:'settings',   label:'设置',       icon:'settings' },
    { id:'help',       label:'快捷键说明', icon:'keyboard', key:'F1' },
  ]);
}
function openItemMenu(anchor) {
  showPopover(anchor, [
    { id:'open',    label:'打开',       icon:'open' },
    { id:'copy',    label:'复制',       icon:'copy', key:'Ctrl+C' },
    { id:'cut',     label:'剪切',       icon:'cut',  key:'Ctrl+X' },
    { id:'rename',  label:'重命名',     icon:'pencil', key:'F2' },
    { id:'share',   label:'共享',       icon:'share' },
    '-',
    { id:'delete',  label:'移入回收站', icon:'trash', key:'Delete', danger:true },
  ]);
}
function openTabsFlyout(anchor) {
  showPopover(anchor, [
    { title:'已打开的标签页', items: state.tabs.map((t, i) => ({ id:'tab-goto-' + i, label:t.title, icon: i === state.activeTab ? 'checkcircle' : 'doc', checked:i === state.activeTab })) },
    { items: [ { id:'tab-add', label:'新建标签页', icon:'plus', key:'Ctrl+T' } ] },
  ]);
}
function helpDialog() {
  const pairs = [
    ['搜索岛','Ctrl+F / K'],['地址栏','Ctrl+L'],['重命名','F2'],['删除','Delete'],
    ['全选','Ctrl+A'],['刷新','Ctrl+R'],['新标签','Ctrl+T'],['关标签','Ctrl+W'],
    ['后退/前进','Alt+← / →'],['上一级','Alt+↑'],['视图 1/2/3','Ctrl+1/2/3'],
    ['关闭浮层','Esc'],
  ];
  showDialog(`<h3>Eidoria · 概念第一版</h3>
    <p>这是按 <b>docs/CONCEPT-DESIGN.md v3</b> 与 <b>docs/RESPONSIVE-LAYOUT.md</b> 实现的可交互设计原型。
    数据为固定虚构样例，不会访问真实文件。浅/深主题、五档断点与"预览 / 属性"双页签均按概念图收敛。</p>
    <div class="shortcut-grid">` + pairs.map(([a, b]) => `<div><span>${a}</span><kbd>${b}</kbd></div>`).join('') +
    `</div><div class="dialog__actions"><button class="filled-btn" data-act="dialog-cancel">知道了</button></div>`);
}
function settingsDialog() {
  showDialog(`<h3>设置</h3>
    <p>外观与行为设置；本原型只演示会影响视觉的选项。</p>
    <div class="dialog__choices">
      <label class="dialog__choice"><input type="checkbox" data-set="dark" ${state.theme === 'dark' ? 'checked' : ''}> 深色主题</label>
      <label class="dialog__choice"><input type="checkbox" data-set="motion" ${state.reduceMotion ? 'checked' : ''}> 减少动态效果</label>
      <label class="dialog__choice"><input type="checkbox" data-set="hidden" ${state.showHidden ? 'checked' : ''}> 显示隐藏文件</label>
    </div>
    <div class="dialog__actions"><button class="filled-btn" data-act="dialog-cancel">完成</button></div>`);
}

/* --------------------------------------------------------------------------
   10. 总渲染 / 布局
   -------------------------------------------------------------------------- */
/* 主页在"搜索面板打开且有关键词"时切换为结果列表，关闭后自动还原主页 */
function renderBrowserContent(direction) {
  if (tab().virtual === '主页' && !(state.searchOpen && state.filter)) renderHome();
  else renderFileView(direction);
}
function renderAll(direction) {
  renderTabs(); renderDrawer(); renderNavButtons(); renderCrumbs(); renderToolbar();
  renderBrowserContent(direction);
  renderPreview(); renderStatusBar();
  if (state.searchOpen) renderSearchResults();
  renderTasks();
}
function layout() {
  const d = BP[state.bp];
  app.dataset.bp = state.bp; app.dataset.theme = state.theme;
  app.dataset.view = state.view;
  app.style.setProperty('--app-w', d.w + 'px');
  app.style.setProperty('--app-h', d.h + 'px');
  const wrap = $('#stageWrap');
  const availW = Math.max(200, wrap.clientWidth - 44);
  const availH = Math.max(200, wrap.clientHeight - 58);
  const z = Math.min(1, availW / d.w, availH / d.h);
  state.zoom = z;
  app.style.setProperty('--zoom', z);
  stage.style.width = Math.round(d.w * z) + 'px';
  stage.style.height = Math.round(d.h * z) + 'px';
  $('#stageCaption').innerHTML = `<b>${d.label}</b> · ${state.theme === 'dark' ? '深色' : '浅色'}主题 · 缩放 ${Math.round(z * 100)}% · 种子色 #006A6A`;
  if (state.bp === 'compact' || state.bp === 'medium') positionFloating();
}
function setBp(bp) {
  state.bp = bp; state.floatPos = { x:null, y:null };
  $$('#bpSwitch button').forEach(b => b.classList.toggle('is-active', b.dataset.bp === bp));
  layout(); renderAll();
  closePopover(); closeSearch();
}
function setTheme(th) {
  state.theme = th;
  $$('#themeSwitch button').forEach(b => b.classList.toggle('is-active', b.dataset.theme === th));
  layout();
}
function setReduceMotion(on) {
  state.reduceMotion = on;
  app.classList.toggle('reduce-motion', on);
}

/* --------------------------------------------------------------------------
   11. 事件（委托）
   -------------------------------------------------------------------------- */
app.addEventListener('click', e => {
  const el = e.target.closest('[data-act]');
  if (!el) { if (!e.target.closest('.popover')) closePopover(); return; }
  const act = el.dataset.act, id = el.dataset.id;
  const n = id ? NODE_BY_PATH.get(id) : null;

  switch (act) {
    /* 导航 */
    case 'nav': {
      tab().title = id;
      navigate(DEST_CWD[id] || 'root', DEST_CWD[id] ? null : (id === '主页' ? '主页' : id), 'next');
      closeDrawer(); break;
    }
    case 'home-cat': {
      const cwd = DEST_CWD[id] || (id === '视频' ? null : id);
      if (id === '视频') { tab().title = '视频'; tab().virtual = '视频'; renderAll(); }
      else { tab().title = id; navigate(cwd, null, 'next'); }
      break;
    }
    case 'settings': settingsDialog(); closeDrawer(); break;
    case 'crumb': {
      if (id !== 'root' && !NODE_BY_PATH.has(id)) break;
      tab().title = id === 'root' ? '内部存储' : NODE_BY_PATH.get(id).name;
      navigate(id, null, 'prev'); break;
    }
    case 'crumb-edit': renderCrumbs(true); break;
    case 'back': goHistory(-1); break;
    case 'forward': goHistory(1); break;
    case 'up': goUp(); break;
    case 'refresh': renderAll(); showSnack('已刷新当前文件夹'); break;

    /* 标签页 */
    case 'tab': { const i = state.tabs.findIndex(t => t.id === id); if (i >= 0) { state.activeTab = i; state.selected.clear(); state.filter = ''; renderAll(); } break; }
    case 'tab-close': {
      if (state.tabs.length <= 1) { showSnack('至少保留一个标签页'); break; }
      const i = state.tabs.findIndex(t => t.id === id);
      state.tabs.splice(i, 1); state.activeTab = Math.max(0, Math.min(state.activeTab, state.tabs.length - 1));
      renderAll(); break;
    }
    case 'tab-add': {
      if (state.tabs.length >= 8) { showSnack('最多 8 个标签页'); break; }
      state.tabs.push(makeTab('新标签', 'root')); state.activeTab = state.tabs.length - 1;
      state.selected.clear(); renderAll(); break;
    }
    case 'tabs-btn': openTabsFlyout(el); return;

    /* 文件项 */
    case 'item': {
      if (e.detail >= 2) { openItem(n); }
      else if (n) selectItem(id, e.ctrlKey || e.metaKey);
      break;
    }
    case 'sr-item': {
      if (!n) break;
      tab().virtual = null; tab().cwd = n.parent; tab().title = n.parent === 'root' ? '内部存储' : n.parent;
      state.filter = ''; closeSearch();
      state.selected.clear(); state.selected.add(n.path); state.activePath = n.path;
      state.previewTab = n.kind === 'folder' ? 'properties' : 'preview';
      if (state.bp === 'compact' || state.bp === 'medium') state.floatingOpen = true;
      renderAll(); showSnack(`已定位到 ${n.parent === 'root' ? '内部存储' : n.parent}`); break;
    }

    /* 预览 */
    case 'ptab': state.previewTab = id; renderPreview(); break;
    case 'preview-toggle': state.previewVisible = !state.previewVisible;
      if (state.previewVisible) state.floatingOpen = false;
      renderAll(); break;
    case 'float-close': state.floatingOpen = false; renderPreview(); break;
    case 'open': { const cur = activeNode(); if (cur) openItem(cur); break; }
    case 'share': showSnack('共享：已通过系统共享表单发送（演示）'); break;
    case 'item-menu': openItemMenu(el); return;
    case 'copy-path': {
      const text = n ? n.path : id;
      if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text).catch(() => {});
      showSnack('已复制路径'); break;
    }

    /* 工具栏 */
    case 'new-folder': newItemDialog('folder'); break;
    case 'cut': copyOrCut('cut'); break;
    case 'copy': copyOrCut('copy'); break;
    case 'paste': doPaste(); break;
    case 'rename': renameDialog(); break;
    case 'delete': deleteConfirm(); break;
    case 'sort': openSortMenu(el); return;
    case 'sort-col':
      if (state.sort.key === id) state.sort.dir *= -1;
      else { state.sort.key = id; state.sort.dir = 1; }
      renderBrowserContent(); break;
    case 'view': {
      const order = ['grid','list','details'];
      state.view = order[(order.indexOf(state.view) + 1) % 3];
      app.dataset.view = state.view; renderAll(); break;
    }
    case 'hidden': state.showHidden = !state.showHidden; renderAll(); break;
    case 'tasks': $('#taskCenter').hidden = !$('#taskCenter').hidden; renderTasks(); break;
    case 'tasks-close': $('#taskCenter').hidden = true; break;
    case 'menu': {
      const m = menuActions().find(x => x.id === id);
      closePopover(); if (!m) break;
      if (id.startsWith('sort-')) {
        if (id === 'sort-asc') state.sort.dir = 1;
        else if (id === 'sort-desc') state.sort.dir = -1;
        else { state.sort.key = id.slice(5); }
        renderAll();
      } else if (id.startsWith('view-')) { state.view = id.slice(5); app.dataset.view = state.view; renderAll(); }
      else if (id.startsWith('tab-goto-')) { state.activeTab = +id.slice(9); renderAll(); }
      else handleAction(id, el);
      break;
    }
    case 'help': helpDialog(); break;

    /* 搜索 */
    case 'search-open': openSearch(); break;

    /* 对话框 */
    case 'dialog-cancel': closeDialog(); break;
    case 'dialog-new-ok': createItem($('#dialog').dataset.kind || 'folder'); break;
    case 'dialog-rename-ok': doRename(); break;
    case 'dialog-delete-ok': doDelete(); break;

    /* Snackbar */
    case 'snack-close': $('#snackbar').hidden = true; break;
    case 'snack-action': { const f = state.snack; $('#snackbar').hidden = true; state.snack = null; if (f) f(); break; }

    default: handleAction(act, el);
  }
  if (!['sort','item-menu','menu','tabs-btn','overflow','help'].includes(act)) { /* popover 由各分支自行关闭 */ }
});
function handleAction(act, el) {
  switch (act) {
    case 'overflow': openOverflowMenu(el); return;
    case 'new-folder': newItemDialog('folder'); break;
    case 'refresh': renderAll(); break;
    case 'hidden': state.showHidden = !state.showHidden; renderAll(); break;
    case 'preview-toggle': state.previewVisible = !state.previewVisible; renderAll(); break;
    case 'tasks': $('#taskCenter').hidden = false; renderTasks(); break;
    case 'settings': settingsDialog(); break;
    case 'help': helpDialog(); break;
    case 'tab-add': state.tabs.push(makeTab('新标签','root')); state.activeTab = state.tabs.length - 1; renderAll(); break;
    case 'open': { const n = activeNode(); if (n) openItem(n); break; }
    case 'copy': copyOrCut('copy'); break;
    case 'cut': copyOrCut('cut'); break;
    case 'rename': renameDialog(); break;
    case 'share': showSnack('共享：已通过系统共享表单发送（演示）'); break;
    case 'delete': deleteConfirm(); break;
  }
}

/* 双击由 click 的 e.detail >= 2 处理；不另绑 dblclick，避免重渲染后重复导航 */

/* 搜索岛 / 输入 */
$('#searchIsland').addEventListener('click', openSearch);
$('#searchIsland').addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openSearch(); } });
$('#searchClose').addEventListener('click', closeSearch);
$('#searchInput').addEventListener('input', e => {
  state.filter = e.target.value;
  renderSearchResults(); renderBrowserContent(); renderSearchIsland(); renderStatusBar();
});
$('#searchInput').addEventListener('keydown', e => {
  if (e.key === 'Enter') { state.searchMode = 'recursive'; $$('#searchPanel .chip').forEach(c => c.classList.toggle('is-active', c.dataset.mode === 'recursive')); renderSearchResults(); renderBrowserContent(); }
  if (e.key === 'Escape') { e.stopPropagation(); closeSearch(); }
});
$$('#searchPanel .chip').forEach(c => c.addEventListener('click', () => {
  state.searchMode = c.dataset.mode;
  $$('#searchPanel .chip').forEach(x => x.classList.toggle('is-active', x === c));
  renderSearchResults(); renderBrowserContent();
}));

/* 地址栏编辑 */
$('#crumbs').addEventListener('keydown', e => {
  if (e.target.id !== 'crumbEdit') return;
  if (e.key === 'Enter') {
    const v = e.target.value.trim().replace(/^file:\/\//, '').replace(/^~/, '内部存储');
    let target = null;
    if (v === '内部存储' || v === '' || v === '/') target = 'root';
    else if (NODE_BY_PATH.has(v)) target = v;
    else { const hit = NODES.find(n => n.name === v); if (hit) target = hit.path; }
    if (target) { tab().title = target === 'root' ? '内部存储' : NODE_BY_PATH.get(target).name; navigate(target, null, 'next'); }
    else { showSnack('路径不存在，已保留输入'); renderCrumbs(true); }
  }
  if (e.key === 'Escape') { e.stopPropagation(); renderCrumbs(); }
});

/* 对话框：设置复选框 / 回车提交 */
$('#dialog').addEventListener('change', e => {
  const s = e.target.dataset.set; if (!s) return;
  if (s === 'dark') { setTheme(e.target.checked ? 'dark' : 'light'); }
  if (s === 'motion') { setReduceMotion(e.target.checked); $('#reduceMotion').checked = e.target.checked; }
  if (s === 'hidden') { state.showHidden = e.target.checked; renderAll(); }
});
$('#dialog').addEventListener('keydown', e => {
  if (e.key === 'Enter' && e.target.id === 'dlgName') { e.preventDefault(); e.stopPropagation(); $('#dialog').querySelector('[data-act^="dialog-"][data-act$="-ok"]')?.click(); }
  if (e.key === 'Escape') { e.stopPropagation(); closeDialog(); }
});
$('#dialogBackdrop').addEventListener('click', e => { if (e.target.id === 'dialogBackdrop') closeDialog(); });

/* 抽屉（Compact 模态） */
function openDrawer() {
  if (state.bp !== 'compact') return;
  state.modalDrawer = true;
  const d = $('#drawer'); d.classList.add('is-modal'); d.style.display = 'flex';
  const scrim = document.createElement('div'); scrim.className = 'drawer-scrim'; scrim.id = 'drawerScrim';
  app.appendChild(scrim);
  if (!d.querySelector('.modal-brand')) {
    d.insertAdjacentHTML('afterbegin', `<div class="modal-brand"><img src="assets/eidoria-icon.svg" class="brand-mark" alt=""><strong>Eidoria</strong></div>`);
  }
  scrim.addEventListener('click', closeDrawer);
}
function closeDrawer() {
  state.modalDrawer = false;
  const d = $('#drawer'); d.classList.remove('is-modal'); d.style.display = '';
  $('#drawerScrim')?.remove();
  $('.modal-brand')?.remove();
}
$('#hamburger').addEventListener('click', openDrawer);

/* 悬浮窗拖动 */
let drag = null;
$('#floating').addEventListener('pointerdown', e => {
  const h = e.target.closest('#floatHandle'); if (!h) return;
  const fl = $('#floating');
  drag = { sx:e.clientX, sy:e.clientY, x:parseFloat(fl.style.left) || 0, y:parseFloat(fl.style.top) || 0 };
  h.setPointerCapture(e.pointerId);
});
$('#floating').addEventListener('pointermove', e => {
  if (!drag) return;
  const z = state.zoom;
  state.floatPos = { x:drag.x + (e.clientX - drag.sx)/z, y:drag.y + (e.clientY - drag.sy)/z };
  const fl = $('#floating');
  fl.style.left = state.floatPos.x + 'px'; fl.style.top = state.floatPos.y + 'px';
});
$('#floating').addEventListener('pointerup', () => { if (drag) { drag = null; positionFloating(); } });

/* 溢出按钮 / 页签按钮：switch 之前绑定 */
$('#overflowBtn').addEventListener('click', e => { e.stopPropagation(); openOverflowMenu($('#overflowBtn')); });
$('#tabsBtn').addEventListener('click', e => { e.stopPropagation(); openTabsFlyout($('#tabsBtn')); });
$('#hamburger').addEventListener('keydown', e => { if (e.key === 'Enter') openDrawer(); });

/* Studio bar */
$('#bpSwitch').addEventListener('click', e => { const b = e.target.closest('[data-bp]'); if (b) setBp(b.dataset.bp); });
$('#themeSwitch').addEventListener('click', e => { const b = e.target.closest('[data-theme]'); if (b) setTheme(b.dataset.theme); });
$('#reduceMotion').addEventListener('change', e => setReduceMotion(e.target.checked));
$('#helpBtn').addEventListener('click', helpDialog);

/* 全局键盘 */
document.addEventListener('keydown', e => {
  const mod = e.ctrlKey || e.metaKey;
  const typing = /input|textarea/i.test(e.target.tagName);
  if (e.key === 'Escape') {
    if (state.dialog) closeDialog();
    else if (!$('#popover').hidden) closePopover();
    else if (state.searchOpen) closeSearch();
    else if (state.modalDrawer) closeDrawer();
    else if (state.floatingOpen) { state.floatingOpen = false; renderPreview(); }
    else if (state.selected.size) { state.selected.clear(); renderBrowserContent(); renderToolbar(); }
    return;
  }
  if (state.dialog) return;
  if (e.key === 'F1') { e.preventDefault(); helpDialog(); return; }
  if (typing) return;
  if (mod && (e.key === 'f' || e.key === 'F' || e.key === 'k' || e.key === 'K')) { e.preventDefault(); openSearch(); return; }
  if (mod && e.key === 'l') { e.preventDefault(); renderCrumbs(true); return; }
  if (mod && e.key === 'a') { e.preventDefault(); currentItems().forEach(n => state.selected.add(n.path)); renderBrowserContent(); renderToolbar(); return; }
  if (mod && e.key === 't') { e.preventDefault(); handleAction('tab-add'); return; }
  if (mod && e.key === 'w') {
    e.preventDefault();
    if (state.tabs.length > 1) { state.tabs.splice(state.activeTab, 1); state.activeTab = Math.max(0, state.activeTab - 1); state.selected.clear(); renderAll(); }
    return;
  }
  if (mod && e.key === 'r') { e.preventDefault(); renderAll(); showSnack('已刷新当前文件夹'); return; }
  if (mod && e.key === 'c') { e.preventDefault(); copyOrCut('copy'); return; }
  if (mod && e.key === 'x') { e.preventDefault(); copyOrCut('cut'); return; }
  if (mod && e.key === 'v') { e.preventDefault(); doPaste(); return; }
  if (mod && ['1','2','3'].includes(e.key)) { e.preventDefault(); state.view = ['grid','list','details'][+e.key - 1]; app.dataset.view = state.view; renderAll(); return; }
  if (e.altKey && e.key === 'ArrowLeft') { e.preventDefault(); goHistory(-1); return; }
  if (e.altKey && e.key === 'ArrowRight') { e.preventDefault(); goHistory(1); return; }
  if (e.altKey && e.key === 'ArrowUp') { e.preventDefault(); goUp(); return; }
  if (e.key === 'F2') { e.preventDefault(); renameDialog(); return; }
  if (e.key === 'Delete' && state.selected.size) { e.preventDefault(); deleteConfirm(); return; }
  if (e.key === ' ' && activeNode() && !state.selected.size) { e.preventDefault(); state.floatingOpen = true; renderPreview(); return; }
});
window.addEventListener('resize', () => { clearTimeout(layout._t); layout._t = setTimeout(layout, 80); });

/* --------------------------------------------------------------------------
   12. 启动
   -------------------------------------------------------------------------- */
state.previewTab = 'preview';
state.selected.add(state.activePath);
renderAll();
layout();
setTimeout(() => showSnack('概念第一版已就绪 · 点击文件预览，双击文件夹进入', '快捷键', helpDialog), 600);

})();
