/* ==========================================================================
   Eidoria 规划场景（概念图 ai-v3/06–15）
   --------------------------------------------------------------------------
   这些是 docs/CONCEPT-DESIGN.md §4–§6 的「规划预演」，不是已实现功能。
   本模块只负责渲染与场景内交互；颜色、几何、图标全部复用主应用的 MD3 Token。
   接口：EidoriaScenes.list / render(id, C) / handle(act, id, el, C)
   ========================================================================== */
(() => {
'use strict';

const LIST = [
  { id:'browser',    label:'基线 · 浏览器 01–05',        bp:'large',   theme:'light', group:'当前基线' },
  { id:'search',     label:'① 搜索与索引中心 06',        bp:'large',   theme:'dark',  group:'近期核心' },
  { id:'storage',    label:'② 存储洞察 08',              bp:'large',   theme:'dark',  group:'近期核心' },
  { id:'android',    label:'② Android 存储与回收站 15',  bp:'compact', theme:'dark',  group:'近期核心' },
  { id:'archive',    label:'③ 压缩包工作台 09',          bp:'large',   theme:'light', group:'近期核心' },
  { id:'conflict',   label:'③ 冲突与任务详情 10',        bp:'large',   theme:'light', group:'近期核心' },
  { id:'dual',       label:'④ ExtraLarge 双窗格 07',     bp:'xlarge',  theme:'light', group:'Phase 2' },
  { id:'editor',     label:'④ 编辑与 PDF 工作台 11',     bp:'large',   theme:'dark',  group:'Phase 2' },
  { id:'media',      label:'⑤ 内置媒体播放 12',          bp:'large',   theme:'dark',  group:'远期' },
  { id:'extensions', label:'⑥ 扩展与 Lua 13',            bp:'large',   theme:'dark',  group:'最后轨道' },
  { id:'ai',         label:'⑥ 本地 AI 语义搜索 14',      bp:'large',   theme:'light', group:'最后轨道' },
];

/* 场景内部状态 */
const S = {
  search:{ q:'海岸 日落', mode:'文件名', chip:'图片', progress:86, paused:false },
  storage:{ selected:null, review:[] },
  android:{ grants:[
    { name:'Download',  state:'ok',   scope:'读写' },
    { name:'DCIM',      state:'ok',   scope:'读写' },
    { name:'Documents', state:'lost', scope:'失效' }
  ], trash:[
    { name:'IMG_0098.jpg', size:'3.1 MB',  type:'image' },
    { name:'旧报价单.pdf',  size:'840 KB', type:'pdf' },
    { name:'backup-old.zip',size:'420 MB', type:'zip' }
  ]},
  archive:{ picked:'IMG_20261001.jpg', progress:64, running:false, keepTree:true, askOverwrite:true, openAfter:false },
  conflict:{ strategy:'保留两者', applyRest:false, resolved:false },
  dual:{ active:'A' },
  editor:{ dirty:false, page:3, zoom:100, query:'响应式', saved:false },
  media:{ playing:true, pos:64, current:0, volume:true, subtitles:true },
  extensions:{ on:{ Rename:true, Hash:true, OCR:true }, plan:null, confirmed:false },
  ai:{ q:'海边日落', tag:null, model:'downloaded' },
};

/* ---------------------------------------------------------------- 通用片段 */
function drawer(C, active, extra = '') {
  const nav = [
    ['主页','home'],['收藏','star'],['最近','clock'],['图片','image'],
    ['视频','video'],['文档','doc'],['回收站','trash'], ...extra
  ];
  return `<aside class="sc-drawer">
    <button class="new-btn" data-sc="sc-new">${C.icon('plus')}<span>新建</span></button>
    <nav class="drawer__nav">` + nav.map(([id, ic]) => `
      <button class="nav-item ${active === id ? 'is-active' : ''}" data-sc="sc-nav" data-id="${id}" title="${id}">
        ${C.icon(ic)}<span>${id}</span></button>`).join('') + `</nav>
    <div class="drawer__spacer"></div>
    <div class="sc-volumes">` + [['C: Drive','44%','drive'],['D: Drive','31%','drive'],['SD Card','12%','sd']].map(([n,p,i]) => `
      <div class="vol">${C.icon(i)}<span class="vol__name">${n}</span><span class="vol__pct">${p}</span>
        <span class="vol__bar"><i style="width:${p}"></i></span></div>`).join('') + `</div>
    <div class="sc-drawer__foot"><button class="nav-item" data-sc="sc-nav" data-id="设置" title="设置">${C.icon('settings')}<span>设置</span></button></div>
  </aside>`;
}
function rail(C, active) {
  return `<aside class="sc-rail">` + [
    ['主页','home'],['收藏','star'],['最近','clock'],['文档','doc'],['图片','image'],['设置','settings']
  ].map(([id, ic]) => `<button class="nav-item ${active===id?'is-active':''}" data-sc="sc-nav" data-id="${id}" title="${id}">${C.icon(ic)}</button>`).join('') + `</aside>`;
}
function topbar(C, crumbs, actions = '') {
  return `<div class="sc-topbar">
    <div class="navbuttons">
      <button class="icon-btn" data-sc="sc-noop" title="后退">${C.icon('back')}</button>
      <button class="icon-btn" data-sc="sc-noop" title="前进">${C.icon('forward')}</button>
      <button class="icon-btn" data-sc="sc-noop" title="上一级">${C.icon('up')}</button>
    </div>
    <div class="crumbs">` + crumbs.map((c,i) =>
      (i ? `<span class="crumb-sep">${C.icon('chevron-right')}</span>` : '') +
      `<button class="crumb" data-sc="sc-crumb" data-id="${C.esc(c)}">${C.esc(c)}</button>`).join('') +
      `<span class="crumbs__blank"></span></div>
    <div class="search-island" data-sc="sc-search">${C.icon('search')}<span class="search-island__text">搜索文件</span></div>
    ${actions}
  </div>`;
}
function panel(title, body, cls = '') {
  return `<section class="sc-panel ${cls}">${title ? `<div class="sc-panel__t">${title}</div>` : ''}${body}</section>`;
}
function fact(ic, v) { return `<span class="fact">${ic}${v}</span>`; }
function ring(pct, size = 150, label = '') {
  const r = 54, c = 2 * Math.PI * r;
  return `<div class="sc-ring" style="--ring-size:${size}px">
    <svg viewBox="0 0 130 130" aria-hidden="true">
      <circle cx="65" cy="65" r="${r}" class="sc-ring__track"/>
      <circle cx="65" cy="65" r="${r}" class="sc-ring__fill"
        stroke-dasharray="${c}" stroke-dashoffset="${c * (1 - pct/100)}"/>
    </svg>
    <div class="sc-ring__label"><b>${pct}%</b>${label ? `<small>${label}</small>` : ''}</div>
  </div>`;
}

/* ---------------------------------------------------------------- 06 搜索中心 */
function searchScene(C) {
  const s = S.search;
  const chips = [['图片','image'],['视频','video'],['音频','music'],['文档','doc'],['压缩包','archive']];
  const results = [
    ['海岸日落',248,'folder',1],['img_coast_su','3.2 MB','image',1],['beach_scene_','3.2 MB','image',2],
    ['coastline_C3','1.4 MB','image',3],['cove_aerial_','3.3 MB','image',4],['rocky_beach','3.4 MB','image',3],
    ['sunset_close','3.2 MB','image',2],['beach_sunset','1.4 MB','image',1],['coastal_road','3.2 MB','image',4],
    ['sand_dune_l9','1.4 MB','image',5],['海岸日落',248,'folder',2],['IMG_2026101','1.4 MB','pdf',1],
    ['coastal_road','3.2 MB','code',3],['sand_dune_l9','1.4 MB','image',5],['Beach-001','3.3 MB','image',3]
  ];
  const card = ([name, size, type, seed]) => `
    <button class="sc-file" data-sc="sc-result" data-id="${C.esc(name)}">
      <span class="sc-file__thumb">${type === 'image'
        ? `<img src="${C.photo(seed)}" alt="">`
        : `<span class="type-well" data-t="${type}">${C.icon(type==='folder'?'folder':type==='pdf'?'doc':type==='code'?'code':'archive')}</span>`}</span>
      <span class="sc-file__name">${C.esc(name)}</span>
      <span class="sc-file__meta">${type==='folder' ? size+' 个项目' : size}</span>
    </button>`;
  return drawer(C, '搜索中心', [['搜索中心','search']]) + `
    <section class="sc-main sc-search">
      <div class="sc-search__bar">
        <div class="sc-searchbox">${C.icon('search')}
          <input id="scQuery" value="${C.esc(s.q)}" placeholder="搜索文件名或正文" autocomplete="off" data-sc="sc-noop">
          <span class="sc-searchbox__count">15 项</span></div>
        <div class="sc-modes">` + ['文件名','正文','语义·后续'].map(m =>
          `<button class="chip ${s.mode===m?'is-active':''}" data-sc="sc-mode" data-id="${m}" ${m==='语义·后续'?'disabled':''}>${m}</button>`).join('') + `</div>
      </div>
      <div class="sc-chips">` + chips.map(([l,i]) =>
        `<button class="chip ${s.chip===l?'is-active':''}" data-sc="sc-chip" data-id="${l}">${C.icon(i)}${l}</button>`).join('') +
        `<button class="chip" data-sc="sc-noop">${C.icon('filter')}更多筛选</button></div>
      <div class="sc-result-head"><span>15 个结果</span><span class="sc-result-head__r">索引根：Pictures · 2 个</span></div>
      <div class="sc-grid">${results.map(card).join('')}</div>
    </section>
    <aside class="sc-side sc-side--index">
      <div class="sc-index-count">${C.icon('database')}<b>128,480</b><small>已索引条目</small></div>
      ${ring(s.progress, 150, `${s.paused?'已暂停':'索引健康'}`)}
      <div class="sc-vol-list">` + [['C: Drive','44%'],['D: Drive','31%']].map(([n,p]) =>
        `<div class="vol">${C.icon('drive')}<span class="vol__name">${n}</span><span class="vol__pct">${p}</span>
          <span class="vol__bar"><i style="width:${p}"></i></span></div>`).join('') + `</div>
      <div class="sc-side__actions">
        <button class="tonal-btn" data-sc="sc-pause">${C.icon(s.paused?'play':'pause')}${s.paused?'继续':'暂停'}</button>
        <button class="tonal-btn" data-sc="sc-reindex">${C.icon('refresh')}重建</button>
      </div>
      <div class="sc-privacy">
        <div class="sc-privacy__t">${C.icon('shield')}隐私边界</div>
        <p>索引只存本机。清空后不可恢复，根目录可随时移除。</p>
        <button class="text-btn sc-privacy__clear" data-sc="sc-clear-index">清空索引</button>
        <button class="text-btn" data-sc="sc-remove-root">移除根目录</button>
      </div>
    </aside>`;
}

/* ---------------------------------------------------------------- 07 双窗格 */
function dualScene(C) {
  const a = [
    ['Camera',248,'folder'],['IMG_20261001.jpg','3.2 MB','image',1],['海边落日.jpg','3.2 MB','image',2],
    ['设计规范 v3.pdf','8.7 MB','pdf'],['Program.cs','12 KB','code'],['backup-2026.zip','1.4 GB','zip'],
    ['声音 001.m4a','22 MB','audio'],['script.sh','12 KB','code'],['icon.svg','5 KB','image',3]
  ];
  const b = [
    ['Received',1204,'folder'],['Design V2.pdf','8.7 MB','pdf'],['Main.cs','12 KB','code'],['Plan.pdf','4 KB','pdf'],
    ['Archive.zip','1.4 GB','zip'],['Music.m4a','222 MB','audio'],['Memo.txt','18 KB','text'],['Docs',36,'folder'],['icon.svg','5 KB','image',4]
  ];
  const pane = (key, title, crumb, items) => `
    <section class="sc-pane ${S.dual.active===key?'is-active':''}" data-sc="sc-pane" data-id="${key}">
      <div class="sc-pane__head">
        <div class="sc-pane__nav">${C.icon('back')}${C.icon('forward')}${C.icon('up')}${C.icon('refresh')}</div>
        <div class="sc-pane__crumb">${C.esc(crumb)} ${C.icon('chevron-right')}</div>
        <div class="sc-pane__tools">${C.icon('newfolder')}${C.icon('cut')}${C.icon('copy')}${C.icon('pencil')}${C.icon('trash')}${C.icon('sort')}${C.icon('eye')}</div>
        ${S.dual.active===key?'<span class="sc-pane__badge">活动</span>':''}
      </div>
      <div class="sc-pane__grid">` + items.map(([name,size,type,seed]) => `
        <button class="sc-file" data-sc="sc-pane-item" data-id="${C.esc(name)}">
          <span class="sc-file__thumb">${type==='image'
            ? `<img src="${C.photo(seed||1)}" alt="">`
            : `<span class="type-well" data-t="${type}">${C.icon(type==='folder'?'folder':type==='pdf'||type==='text'?'doc':type==='code'?'code':type==='zip'?'archive':type==='audio'?'music':'file')}</span>`}</span>
          <span class="sc-file__name">${C.esc(name)}</span>
          <span class="sc-file__meta">${type==='folder'?size+' 个项目':size}</span>
        </button>`).join('') + `</div>
    </section>`;
  return drawer(C, '图片') + `
    <section class="sc-main sc-dual">
      ${pane('A','A','Pictures',a)}${pane('B','B','Backup',b)}
      <div class="sc-drag-hint">${C.icon('image')}<div><b>海边落日.jpg</b><small>3.2 MB</small></div>
        <span>Ctrl = 复制</span></div>
    </section>`;
}

/* ---------------------------------------------------------------- 08 存储洞察 */
function storageScene(C) {
  const types = [['视频','36 GB','video',100],['图片','21 GB','image',58],['文档','9 GB','doc',25],['音乐','8 GB','music',22]];
  const blocks = [
    ['36 GB','video','b1'],['21 GB','image','b2'],['15 GB','doc','b3'],['8 GB','audio','b4'],
    ['8 GB','video','b5'],['9 GB','doc','b6'],['8 GB','doc','b7'],['4 GB','doc','b8'],
    ['2 GB','doc','b9'],['8 GB','doc','b10'],['','','b11']
  ];
  const bigFiles = [['backup-2026.zip','1.4 GB','zip'],['Eidoria-2024.dmg','1.2 GB','code'],['install.sh','450 KB','code']];
  return drawer(C, '主页') + `
    <section class="sc-main sc-storage">
      <div class="sc-storage__top">
        ${panel('', `<div class="sc-donut">${ring(68,168,'87 / 128 GB')}<div class="sc-donut__cap">已用空间</div></div>`, 'sc-storage__donut')}
        ${panel('', `<div class="sc-typelist">` + types.map(([l,v,i,p]) => `
          <div class="sc-typelist__row">${C.icon(i)}<span class="sc-typelist__bar"><i style="width:${p}%"></i></span><b>${v}</b></div>`).join('') + `</div>`, 'sc-storage__types')}
        ${panel('', `<div class="sc-section-t">重复文件 · 可复核</div>
          <div class="sc-dupes">` + [1,2,3,4].map((n,i) => `
            <div class="sc-dupe"><img src="${C.photo(i%2?2:3)}" alt="">${i%2?C.icon('link'):''}</div>`).join('') + `</div>
          <div class="sc-dupes__foot">2 组 · 3.2 GB <button class="chip" data-sc="sc-review">加入复核</button></div>`, 'sc-storage__dupes')}
      </div>
      <div class="sc-storage__bottom">
        ${panel('', `<div class="sc-section-t">空间地图 · 点击只定位</div><div class="sc-map">` + blocks.map(([v,i,c]) => `
          <button class="sc-map__b ${c} ${S.storage.selected===c?'is-selected':''}" data-sc="sc-map" data-id="${c}">
            ${i?C.icon(i):''}<span>${v}</span></button>`).join('') + `</div>`, 'sc-storage__map')}
        ${panel('', `<div class="sc-section-t">重复组 2 · 保留一份</div>
          <div class="sc-dupes sc-dupes--sm">` + [3,1,4,2].map((n,i) => `<div class="sc-dupe"><img src="${C.photo(n)}" alt="">${i%2?C.icon('link'):''}</div>`).join('') + `</div>
          <button class="filled-btn sc-review-btn" data-sc="sc-review">${C.icon('check')}18.4 GB 加入复核</button>`, 'sc-storage__dupes2')}
      </div>
      <div class="sc-storage__foot">` + bigFiles.map(([n,s,t]) => `
        <button class="sc-bigfile" data-sc="sc-bigfile" data-id="${C.esc(n)}">
          <span class="type-well" data-t="${t}">${C.icon(t==='zip'?'archive':'code')}</span>
          <span><b>${n}</b><small>${s}</small></span>${C.icon('chevron-right')}</button>`).join('') + `
        <span class="sc-storage__note">${C.icon('info')}复核而非自动清理 · 删除入口留在文件上下文</span>
      </div>
    </section>`;
}

/* ---------------------------------------------------------------- 09 压缩包 */
function archiveScene(C) {
  const s = S.archive;
  const entries = [
    ['文件',1380,'folder'],['IMG_2026101','3.2 MB','image',1],['海边落日.jpg','4.1 MB','image',2],
    ['海边落日.jpg','4.1 MB','pdf'],['旅行记录.mp4','380 MB','video',3],['IMG_202610','1.4 MB','pdf'],
    ['行程单.pdf','240 KB','audio'],['code-markdo','12 KB','code'],['metadata.json','12 MB','code'],['环绕声.m4a','22 MB','music']
  ];
  const tree = ['文件','  Images','  Videos','  Docs','  Audio','metadata.json'];
  return drawer(C, '文档') + `
    <section class="sc-main sc-archive">
      ${topbar(C, ['backup-2026.zip'], `<button class="icon-btn" data-sc="sc-noop">${C.icon('more')}</button>`)}
      <div class="sc-archive__tools">` + ['newfolder','cut','copy','paste','pencil','trash','sort','eye'].map(i =>
        `<button class="icon-btn" data-sc="sc-noop" title="${i}">${C.icon(i)}</button>`).join('') +
        `<span class="sc-archive__tools-label">1380 个条目</span></div>
      <div class="sc-archive__body">
        <aside class="sc-tree"><div class="sc-section-t">包内目录</div>` + tree.map((t,i) => `
          <button class="sc-tree__i ${i===0?'is-active':''}" data-sc="sc-tree" data-id="${C.esc(t.trim())}">
            ${i===0||i===5?C.icon('file'):C.icon('folder')}${C.esc(t)}</button>`).join('') + `</aside>
        <div class="sc-archive__grid">` + entries.map(([name,size,type,seed]) => `
          <button class="sc-file ${s.picked===name?'is-selected':''}" data-sc="sc-entry" data-id="${C.esc(name)}">
            <span class="sc-file__thumb">${type==='image'||type==='video'
              ? `<img src="${C.photo(seed||1)}" alt="">`
              : `<span class="type-well" data-t="${type==='folder'?'folder':type}">${C.icon(type==='folder'?'folder':type==='pdf'||type==='audio'||type==='music'?'doc':type==='code'?'code':'file')}</span>`}</span>
            <span class="sc-file__name">${C.esc(name)}</span>
            <span class="sc-file__meta">${type==='folder'?size+' 个项目':size}</span>
          </button>`).join('') + `</div>
      </div>
    </section>
    <aside class="sc-side sc-side--archive">
      <div class="sc-side__title">${C.esc(s.picked)}</div>
      <div class="sc-archive__preview"><img src="${C.photo(2)}" alt=""></div>
      <div class="preview__facts">
        ${fact(C.icon('image'),'JPG')}${fact(C.icon('expand'),'4032 × 3024')}${fact(C.icon('drive'),'3.2 MB')}
      </div>
      <div class="sc-side__actions">
        <button class="filled-btn" data-sc="sc-extract">${C.icon('download')}解压</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('share')}</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('more')}</button>
      </div>
      <div class="sc-extract-opts">
        <div class="sc-section-t">解压选项</div>
        <label class="sc-opt"><span>目标：当前文件夹</span>${C.icon('chevron-right')}</label>
        <label class="sc-opt" data-sc="sc-opt" data-id="keepTree"><input type="checkbox" ${s.keepTree?'checked':''}><span>保留目录结构</span></label>
        <label class="sc-opt" data-sc="sc-opt" data-id="askOverwrite"><input type="checkbox" ${s.askOverwrite?'checked':''}><span>覆盖前询问</span></label>
        <label class="sc-opt" data-sc="sc-opt" data-id="openAfter"><input type="checkbox" ${s.openAfter?'checked':''}><span>完成后打开文件夹</span></label>
      </div>
      <div class="sc-extract-progress">${ring(s.progress,96,'')}
        <div><b>${s.running?'正在解压':'解压任务'}</b><small>${s.progress}% · ${s.running?'可取消':'等待中'}</small></div>
        <button class="icon-btn" data-sc="sc-extract-toggle">${C.icon(s.running?'pause':'play')}</button>
      </div>
    </aside>`;
}

/* ---------------------------------------------------------------- 10 冲突与任务 */
function conflictScene(C) {
  const s = S.conflict;
  const tasks = [
    ['等待确认','IMG_20261001.jpg','同名冲突','clock','wait'],
    ['进行中','Beach-00261.jpg','3.4 MB · 64%','refresh','run'],
    ['已完成','Eidoria-2024.dmg','1.2 GB','checkcircle','done'],
    ['失败','install.sh','450 KB · 权限不足','error','fail']
  ];
  return drawer(C, '图片') + `
    <section class="sc-main sc-conflict">
      ${topbar(C, ['Pictures','Camera'])}
      <div class="sc-conflict__bg">` + [
        ['Camera',248,'folder'],['IMG_20261001.jpg','3.2 MB','image',1],['Beach-00261...','3.4 MB','image',3],
        ['设计规范 v3.pdf','8.7 MB','pdf'],['script.sh','12 KB','code'],['备份.zip','22 MB','zip']
      ].map(([name,size,type,seed]) => `<button class="sc-file" data-sc="sc-noop">
        <span class="sc-file__thumb">${type==='image'?`<img src="${C.photo(seed)}" alt="">`:
          `<span class="type-well" data-t="${type}">${C.icon(type==='folder'?'folder':type==='pdf'?'doc':type==='code'?'code':'archive')}</span>`}</span>
        <span class="sc-file__name">${name}</span><span class="sc-file__meta">${type==='folder'?size+' 个项目':size}</span>
      </button>`).join('') + `</div>
      ${s.resolved ? `<div class="sc-conflict__done">${C.icon('checkcircle')}<b>冲突已处理</b>
        <p>已选择「${s.strategy}」${s.applyRest?'并应用到其余 12 项':''}。</p>
        <button class="tonal-btn" data-sc="sc-conflict-reset">重新演示</button></div>` : `
      <div class="sc-conflict__dialog" role="dialog" aria-label="文件冲突">
        <h3>目标已由同名文件占用</h3>
        <div class="sc-compare">
          <div class="sc-compare__card"><img src="${C.photo(3)}" alt="">
            <b>IMG_20261001.jpg</b><span>${C.icon('drive')}3.2 MB</span>
            <span>${C.icon('calendar')}2026-10-01 14:22</span>
            <span>${C.icon('shield')}SHA-256 71BD…03C8</span></div>
          <div class="sc-compare__arrow">${C.icon('forward')}</div>
          <div class="sc-compare__card"><img src="${C.photo(3)}" alt="">
            <b>IMG_20261001.jpg</b><span>${C.icon('drive')}4.1 MB</span>
            <span>${C.icon('calendar')}2026-09-18 09:04</span>
            <span>${C.icon('shield')}SHA-256 9F2C…A41E</span></div>
        </div>
        <div class="sc-strategies">` + [
          ['保留两者','layers','check'],['替换','refresh','replace'],['跳过','block','skip']
        ].map(([l,i]) => `<button class="sc-strategy ${s.strategy===l?'is-active':''}" data-sc="sc-strategy" data-id="${l}">
          ${C.icon(i)}<span>${l}</span></button>`).join('') + `</div>
        <div class="sc-conflict__note">${s.strategy==='替换'?C.icon('warning')+'替换将覆盖目标文件':C.icon('info')+'推荐保留两者，可在任务中心取消'}</div>
        <label class="sc-checkbox" data-sc="sc-apply-rest"><input type="checkbox" ${s.applyRest?'checked':''}><span>应用到剩余 12 项同类冲突</span></label>
        <div class="sc-conflict__actions">
          <button class="text-btn" data-sc="sc-conflict-cancel">取消</button>
          <button class="filled-btn" data-sc="sc-conflict-continue">继续</button>
        </div>
      </div>`}
    </section>
    <aside class="sc-side sc-side--tasks">
      <div class="sc-side__title">任务中心</div>
      <div class="sc-task-states">` + tasks.map(([state,name,meta,ic,cls]) => `
        <div class="sc-task sc-task--${cls}">
          <span class="sc-task__ic">${C.icon(ic)}</span>
          <span class="sc-task__main"><b>${name}</b><small>${meta}</small></span>
          <span class="sc-task__state">${state}</span>
        </div>`).join('') + `</div>
      <div class="sc-section-t">队列进度</div>
      ${ring(64,120,'4 项任务')}
      <div class="sc-side__actions">
        <button class="text-btn" data-sc="sc-noop">取消任务</button>
        <button class="tonal-btn" data-sc="sc-noop">继续任务</button>
      </div>
    </aside>`;
}

/* ---------------------------------------------------------------- 11 编辑与 PDF */
function editorScene(C) {
  const s = S.editor;
  const code = [
    ['1',''],['2','<span class="k">using</span> MdExplorer.UI;'],['3',''],
    ['4','<span class="k">public async</span> Task LoadAsync()'],['5','{'],
    ['6','  <span class="k">await</span> Browser.RefreshAsync();'],
    ['7','  Preview.Open(Browser.SelectedItem);'],['8','  StatusText = <span class="s">"Loaded"</span>;'],['9','}'],
    ['10',''],['11','<span class="c">// 保存操作进入既有任务队列</span>'],
    ['12','<span class="k">await</span> Jobs.EnqueueAsync(savePlan);'],['13',''],
    ['14','<span class="c">// 保存前先显示差异与外部修改冲突</span>'],['15',''],['16',''],
    ['17','<span class="c">// DepthText</span>']
  ];
  const toc = [['','',''],['','',''],['','',''],['','',''],['','',''],['','','']];
  return rail(C, '文档') + `
    <section class="sc-main sc-editor">
      <div class="sc-editor__pane">
        <div class="sc-editor__bar">
          <span class="sc-editor__file">${C.icon('code')}<b>BrowserViewModel.cs</b>${s.dirty?'<i class="sc-dirty" title="已修改"></i>':''}</span>
          <span class="sc-editor__acts">
            <button class="icon-btn ${s.saved?'is-active':''}" data-sc="sc-save" title="保存（进入任务队列）">${C.icon('paste')}</button>
            <button class="icon-btn" data-sc="sc-undo" title="撤销">${C.icon('undo')}</button>
            <button class="icon-btn" data-sc="sc-noop" title="重做">${C.icon('refresh')}</button>
            <button class="icon-btn" data-sc="sc-tts" title="朗读（无障碍）">${C.icon('volume')}</button>
          </span>
        </div>
        <div class="sc-code" id="scCode" contenteditable="true" spellcheck="false" data-sc="sc-code-edit">` +
        code.map(([n,l]) => `<div class="sc-code__l"><span class="sc-code__n">${n}</span><span>${l}</span></div>`).join('') + `</div>
        <div class="sc-editor__status">${s.dirty?'● 已修改 · 保存前显示差异':'只读预览 · 点击代码进入编辑'} · UTF-8 · LF</div>
      </div>
      <div class="sc-editor__pane sc-editor__pane--pdf">
        <div class="sc-editor__bar">
          <span class="sc-editor__file"><span class="sc-pdf-badge">PDF</span><b>设计规范.pdf</b></span>
          <span class="sc-editor__acts">
            <button class="icon-btn" data-sc="sc-page" data-id="-1" title="上一页">${C.icon('back')}</button>
            <button class="icon-btn" data-sc="sc-page" data-id="1" title="下一页">${C.icon('forward')}</button>
            <button class="icon-btn" data-sc="sc-noop" title="更多">${C.icon('more')}</button>
          </span>
        </div>
        <div class="sc-pdfsearch">${C.icon('search')}<input value="${C.esc(s.query)}" autocomplete="off" data-sc="sc-pdf-query" placeholder="文档内搜索">
          <button class="icon-btn" data-sc="sc-pdf-nav" data-id="-1">${C.icon('back')}</button>
          <button class="icon-btn" data-sc="sc-pdf-nav" data-id="1">${C.icon('forward')}</button></div>
        <div class="sc-pdfbody">
          <div class="sc-pdftoc">` + toc.map((_,i) => `<div class="sc-pdftoc__i ${i===1?'is-active':''}">${C.icon('list')}<i style="width:${50+(i*13)%40}%"></i></div>`).join('') + `</div>
          <div class="sc-pdfpage">
            <i class="sc-line" style="width:56%"></i><i class="sc-line" style="width:92%"></i>
            <i class="sc-line" style="width:84%"></i><i class="sc-line" style="width:88%"></i>
            <div class="sc-line sc-line--hit">${C.esc(s.query)} · 语义与响应式的统一</div>
            <i class="sc-line" style="width:78%"></i><i class="sc-line" style="width:90%"></i>
            <i class="sc-line" style="width:64%"></i><i class="sc-line" style="width:86%"></i>
            <i class="sc-line" style="width:92%"></i><i class="sc-line" style="width:70%"></i>
            <i class="sc-line" style="width:88%"></i>
          </div>
        </div>
        <div class="sc-pdffoot"><span>${s.page} / 12</span><span>${s.zoom}%</span>
          <button class="icon-btn" data-sc="sc-zoom" data-id="-10">${C.icon('win-min')}</button>
          <button class="icon-btn" data-sc="sc-zoom" data-id="10">${C.icon('plus')}</button></div>
      </div>
    </section>`;
}

/* ---------------------------------------------------------------- 12 媒体播放 */
function mediaScene(C) {
  const s = S.media;
  const queue = [
    ['海边浪声.mp4','03:45',1],['山顶日出.mp4','12:10',2],['城市夜景.mov','07:22',3],['雨中森林.m4a','18:01',0]
  ];
  const chapters = ['02:15','05:00','08:24','11:40','15:00','18:30','22:00'];
  return drawer(C, '视频') + `
    <section class="sc-main sc-media">
      <aside class="sc-queue"><div class="sc-section-t">播放队列</div>` + queue.map(([n,d,seed],i) => `
        <button class="sc-queue__i ${s.current===i?'is-active':''}" data-sc="sc-queue" data-id="${i}">
          ${seed?`<img src="${C.photo(seed)}" alt="">`:`<span class="sc-queue__audio">${C.icon('music')}</span>`}
          <span><b>${n}</b><small>${d}</small></span></button>`).join('') + `</aside>
      <div class="sc-stage">
        <div class="sc-stage__screen"><img src="${C.photo(5)}" alt="">
          <div class="sc-stage__subtitle">"海边浪声"</div></div>
        <div class="sc-stage__timeline"><div class="sc-stage__track"><i style="width:${s.pos}%"></i><b style="left:${s.pos}%"></b></div>
          <div class="sc-stage__times"><span>08:25</span><span>08:24 / 24:10</span></div></div>
        <div class="sc-stage__controls">
          <button class="icon-btn" data-sc="sc-prev" title="上一个">${C.icon('prev')}</button>
          <button class="sc-play" data-sc="sc-play" title="播放/暂停">${C.icon(s.playing?'pause':'play')}</button>
          <button class="icon-btn" data-sc="sc-next" title="下一个">${C.icon('next')}</button>
          <button class="icon-btn" data-sc="sc-volume" title="音量">${C.icon('volume')}</button>
          <span class="sc-stage__vol"><i style="width:62%"></i></span>
          <button class="icon-btn ${s.subtitles?'is-active':''}" data-sc="sc-subs" title="字幕">${C.icon('subtitles')}</button>
          <button class="icon-btn" data-sc="sc-noop" title="循环">${C.icon('refresh')}</button>
          <button class="icon-btn" data-sc="sc-noop" title="全屏">${C.icon('fullscreen')}</button>
        </div>
        <div class="sc-chapters">` + chapters.map((t,i) => `
          <button class="sc-chapter ${i===2?'is-active':''}" data-sc="sc-chapter" data-id="${i}">
            <img src="${C.photo(i%4+1)}" alt=""><small>${t}</small></button>`).join('') + `</div>
      </div>
    </section>
    <aside class="sc-side sc-side--media">
      <div class="sc-side__title">IMG_20261001.jpg</div>
      <div class="preview__facts">${fact(C.icon('expand'),'3840×2160')}${fact(C.icon('drive'),'380 MB')}</div>
      <div class="sc-side__actions">
        <button class="filled-btn" data-sc="sc-noop">${C.icon('open')}打开</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('share')}</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('more')}</button>
      </div>
    </aside>`;
}

/* ---------------------------------------------------------------- 13 扩展与 Lua */
function extensionsScene(C) {
  const s = S.extensions;
  const exts = [['Rename','pencil'],['Hash','shield'],['OCR','image']];
  const perms = [['folder','check','check','dot'],['pencil','pencil','check','off'],['image','check','check','doc'],['block','off','check','check']];
  const plan = [['IMG_0101.jpg','20261001_IMG_0101.jpg'],['IMG_0103.jpg','20261001_IMG_0103.jpg'],['IMG_0105.jpg','20261001_IMG_0105.jpg']];
  return drawer(C, '主页') + `
    <section class="sc-main sc-ext">
      <div class="sc-ext__col sc-ext__installed">
        <div class="sc-section-t">已安装与权限</div>` + exts.map(([n,i]) => `
        <div class="sc-extcard ${s.on[n]?'is-on':''}" data-sc="sc-ext" data-id="${n}">
          <span class="sc-extcard__ic">${C.icon(i)}</span><b>${n}${n==='Rename'?'+':''}</b>
          <button class="sc-switch" data-sc="sc-ext-toggle" data-id="${n}" aria-label="启用 ${n}"><i></i></button>
        </div>`).join('') + `
        <div class="sc-ext__note">${C.icon('info')}扩展只能提议操作，宿主负责执行。</div>
      </div>
      <div class="sc-ext__col sc-ext__code">
        <div class="sc-ext__codebar">
          <span>${C.icon('script')}<b>rename.lua</b></span>
          <span class="sc-editor__acts">
            <button class="icon-btn" data-sc="sc-run" title="运行（只生成计划）">${C.icon('play')}</button>
            <button class="icon-btn" data-sc="sc-noop" title="设置">${C.icon('settings')}</button>
          </span>
        </div>
        <div class="sc-code sc-code--lua">` + [
          '1|<span class="k">local</span> items = eidoria.selection()',
          '2|<span class="k">local</span> plan = eidoria.plan.new()','3|',
          '4|<span class="k">for</span> _, item <span class="k">in</span> ipairs(items) <span class="k">do</span>',
          '5|  <span class="k">local</span> date = item.taken_at <span class="k">or</span> item.modified',
          '6|  <span class="k">local</span> name = date .. <span class="s">"_"</span> .. item.name',
          '7|  plan:rename(item.path, name)','8|<span class="k">end</span>','9|',
          '10|<span class="k">return</span> plan'
        ].map(l => { const [n,...r] = l.split('|'); return `<div class="sc-code__l"><span class="sc-code__n">${n}</span><span>${r.join('|')}</span></div>`; }).join('') + `</div>
        <div class="sc-ext__output ${s.plan?'is-ready':''}">
          ${s.plan ? (s.confirmed ? `${C.icon('checkcircle')} 计划已确认，3 项重命名进入任务中心`
            : `${C.icon('warning')} 等待确认，尚未写入磁盘`) : `${C.icon('info')} 点击 ▶ 运行脚本，输出操作计划`}
        </div>
      </div>
      <div class="sc-ext__col sc-ext__right">
        <div class="sc-section-t">权限矩阵</div>
        <div class="sc-perm">` + perms.map(row => `<div class="sc-perm__row">` + row.map((v,i) =>
          `<span class="sc-perm__c sc-perm__c--${v}">${v==='off'?C.icon('block'):v==='dot'?'●':v==='check'?C.icon('check'):C.icon(v)}</span>`).join('') + `</div>`).join('') + `</div>
        <div class="sc-section-t">操作计划确认</div>
        <div class="sc-plan">` + plan.map(([a,b],i) => `
          <div class="sc-plan__i"><b>${a}</b><span>${C.icon('forward')}</span><b>${b}</b></div>`).join('') + `</div>
        <button class="filled-btn sc-plan__confirm ${s.plan&&!s.confirmed?'':'is-disabled'}" data-sc="sc-plan-confirm">
          ${C.icon('check')}${s.confirmed?'已确认':'确认并加入任务中心'}</button>
        <div class="sc-android-warn">${C.icon('warning')}
          <span>Android 商店版禁用用户下载脚本；脚本只能手动放入本地脚本文件夹。</span></div>
      </div>
    </section>`;
}

/* ---------------------------------------------------------------- 14 本地 AI */
function aiScene(C) {
  const s = S.ai;
  const names = ['Sunset_001.jpg','Sunset_002.jpg','Sunset_003.jpg','Sunset_004.jpg','Sunset_005.jpg'];
  const results = Array.from({length:15}, (_,i) => [names[i%5].replace('_','_')+(i>4?String(i+1).padStart(3,'0'):''), i%2?'3.2 MB':'1.4 MB', i+1]);
  const tags = ['海边','日落','山脉','剪影','人物'];
  return drawer(C, '图片') + `
    <section class="sc-main sc-ai">
      <div class="sc-ai__bar"><div class="sc-searchbox sc-searchbox--ai">${C.icon('search')}
        <input id="scAiQuery" value="${C.esc(s.q)}" data-sc="sc-ai-query" placeholder="用自然语言描述你要找的照片" autocomplete="off">
        ${C.icon('sparkle')}${C.icon('shield')}</div></div>
      <div class="sc-ai__resulthead"><span>15 个结果 · 语义相关度排序</span>
        <span class="sc-ai__score">${C.icon('info')}徽标只表达相对相关度</span></div>
      <div class="sc-grid sc-grid--ai">` + results.map(([n,sz,seed],i) => `
        <button class="sc-file ${s.tag && !tags.slice(0,3).includes(tags[i%5])?'is-dim':''}" data-sc="sc-ai-result" data-id="${C.esc(n)}">
          <span class="sc-file__thumb"><img src="${C.photo(seed)}" alt=""><i class="sc-ai__dot"></i></span>
          <span class="sc-file__name">${C.esc(n)}</span><span class="sc-file__meta">${sz}</span>
        </button>`).join('') + `</div>
      <div class="sc-modelcard">
        <div class="sc-modelcard__h">${C.icon('database')}<b>SigLIP 2 Base · int8</b></div>
        <div class="sc-modelcard__row"><span>${C.icon('check')} 612 MB</span><span>${C.icon('database')} 12,480</span></div>
        <div class="sc-modelcard__row"><span>${S.ai.model==='downloaded'?'已下载 · 可离线使用':'未下载'}</span></div>
        <div class="sc-modelcard__acts">
          <button class="icon-btn" data-sc="sc-model-folder" title="索引根">${C.icon('folder')}</button>
          <button class="icon-btn" data-sc="sc-model" title="下载/删除模型">${C.icon('trash')}</button>
        </div>
      </div>
    </section>
    <aside class="sc-side sc-side--ai">
      <div class="sc-side__title">IMG_20261001.jpg</div>
      <div class="sc-tabs"><button class="ptab is-active" data-sc="sc-noop">预览</button><button class="ptab" data-sc="sc-noop">属性</button></div>
      <div class="sc-ai__preview"><img src="${C.photo(1)}" alt=""></div>
      <div class="sc-section-t">AI 识别</div>
      <div class="sc-ai__tags">` + tags.map(t => `<button class="chip ${s.tag===t?'is-active':''}" data-sc="sc-ai-tag" data-id="${t}">${C.icon('tag')}${t}</button>`).join('') + `</div>
      <div class="preview__facts">${fact(C.icon('expand'),'4032×3024')}${fact(C.icon('drive'),'3.2 MB')}</div>
      <div class="sc-side__actions">
        <button class="filled-btn" data-sc="sc-noop">${C.icon('open')}打开</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('share')}</button>
        <button class="tonal-btn" data-sc="sc-noop">${C.icon('more')}</button>
      </div>
      <div class="sc-ai__note">${C.icon('shield')}标签只进本地索引，不修改 EXIF 或原文件。</div>
    </aside>`;
}

/* ---------------------------------------------------------------- 15 Android 存储 */
function androidScene(C) {
  const s = S.android;
  const scopeIco = g => g.state === 'ok'
    ? `${C.icon('eye')}${C.icon('pencil')}<span class="sc-grant__ok">${C.icon('check')}</span>`
    : `${C.icon('eye')}${C.icon('pencil')}<span class="sc-grant__warn">${C.icon('warning')}</span><button class="sc-grant__re" data-sc="sc-regrant" data-id="${g.name}" title="重新授权">${C.icon('refresh')}</button>`;
  return `<section class="sc-main sc-android">
    <div class="sc-android__card sc-android__grants">
      <div class="sc-android__head"><span class="sc-android__folder">${C.icon('folder-open')}</span>
        <button class="sc-android__add" data-sc="sc-grant-new" title="授权新文件夹">${C.icon('plus')}</button></div>
      <div class="sc-section-t">已授权文件夹 · 逐项显示读写范围</div>
      ${s.grants.map(g => `<div class="sc-grant">
        <span class="type-well" data-t="folder">${C.icon('folder')}</span>
        <b>${g.name}</b><span class="sc-grant__scope">${g.scope}</span>${scopeIco(g)}</div>`).join('')}
      ${s.grants.length===0?'<div class="sc-empty">暂无授权文件夹</div>':''}
    </div>
    <div class="sc-android__card sc-android__trash">
      <div class="sc-trash__head">${C.icon('trash')}${ring(72,76,'')}<b>1.8 GB</b></div>
      <div class="sc-section-t">应用内回收站 · 30 天自动清理</div>
      ${s.trash.map((t,i) => `<div class="sc-trash__row">
        <span class="${t.type==='image'?'sc-trash__thumb':'type-well'}" data-t="${t.type}">
          ${t.type==='image'?`<img src="${C.photo(i+1)}" alt="">`:C.icon(t.type==='pdf'?'doc':'archive')}</span>
        <span><b>${t.name}</b><small>${t.size}</small></span>
        <button class="sc-trash__undo" data-sc="sc-restore" data-id="${i}" title="还原">${C.icon('undo')}</button>
      </div>`).join('')}
      ${s.trash.length===0?'<div class="sc-empty">回收站是空的</div>':''}
      <button class="sc-trash__clear" data-sc="sc-trash-clear" title="清空回收站">${C.icon('trash')}</button>
    </div>
  </section>`;
}

/* ---------------------------------------------------------------- 分发 */
const RENDER = {
  search:searchScene, dual:dualScene, storage:storageScene, archive:archiveScene,
  conflict:conflictScene, editor:editorScene, media:mediaScene,
  extensions:extensionsScene, ai:aiScene, android:androidScene
};

function render(id, C) {
  return RENDER[id] ? RENDER[id](C) : '';
}

/* 场景内交互：返回 true 表示已消费事件 */
function handle(act, id, el, C) {
  const toast = m => C.showSnack(m);
  switch (act) {
    case 'sc-noop': toast('规划场景 · 该动作尚未实现'); return true;
    case 'sc-new': toast('新建文件 / 文件夹'); return true;
    case 'sc-nav': toast(`导航到「${id}」`); return true;
    case 'sc-crumb': toast(`面包屑分段：${id}`); return true;
    case 'sc-search': toast('搜索岛 · 展开悬浮搜索面板'); return true;

    /* 06 搜索中心 */
    case 'sc-mode': S.search.mode = id; return true;
    case 'sc-chip': S.search.chip = id; return true;
    case 'sc-result': toast(`打开所在文件夹：${id}`); return true;
    case 'sc-pause': S.search.paused = !S.search.paused; toast(S.search.paused?'索引维护已暂停':'索引维护已继续'); return true;
    case 'sc-reindex': S.search.progress = 12; toast('重建索引 · 任务已进入队列'); return true;
    case 'sc-clear-index': C.dialog({
      title:'清空本地索引', body:'将删除 128,480 条索引记录与全部正文数据。索引体积归零，可随时重新建立。',
      ok:'清空索引', danger:true, onOk:() => { S.search.progress = 0; toast('索引已清空'); }
    }); return true;
    case 'sc-remove-root': toast('已从索引根移除：Pictures'); return true;

    /* 07 双窗格 */
    case 'sc-pane': S.dual.active = id; return true;
    case 'sc-pane-item': toast(`窗格 ${S.dual.active} · ${id}`); return true;

    /* 08 存储洞察 */
    case 'sc-map': S.storage.selected = id; toast('已定位到该文件夹（不执行删除）'); return true;
    case 'sc-review': toast('已加入复核 · 需经任务确认，不做一键清理'); return true;
    case 'sc-bigfile': toast(`定位大文件：${id}`); return true;

    /* 09 压缩包 */
    case 'sc-tree': toast(`包内目录：${id}`); return true;
    case 'sc-entry': S.archive.picked = id; return true;
    case 'sc-opt': {
      const k = id; S.archive[k] = !S.archive[k]; return true;
    }
    case 'sc-extract': S.archive.running = true; S.archive.progress = 0;
      toast('解压任务已开始 · 可在任务中心取消'); return true;
    case 'sc-extract-toggle': S.archive.running = !S.archive.running; return true;

    /* 10 冲突 */
    case 'sc-strategy': S.conflict.strategy = id; return true;
    case 'sc-apply-rest': S.conflict.applyRest = !S.conflict.applyRest; return true;
    case 'sc-conflict-continue': S.conflict.resolved = true;
      toast(`已选择「${S.conflict.strategy}」· 任务继续执行`); return true;
    case 'sc-conflict-cancel': toast('已取消当前冲突任务'); return true;
    case 'sc-conflict-reset': S.conflict.resolved = false; return true;

    /* 11 编辑 / PDF */
    case 'sc-code-edit': S.editor.dirty = true; S.editor.saved = false; return true;
    case 'sc-save': S.editor.saved = true; S.editor.dirty = false;
      toast('保存已进入任务队列 · 落盘前显示差异'); return true;
    case 'sc-undo': toast('撤销（演示）'); return true;
    case 'sc-tts': toast('文本朗读 · OS 原生 TTS（无障碍能力）'); return true;
    case 'sc-page': S.editor.page = Math.max(1, Math.min(12, S.editor.page + (+id))); return true;
    case 'sc-zoom': S.editor.zoom = Math.max(50, Math.min(200, S.editor.zoom + (+id))); return true;
    case 'sc-pdf-query': return true;
    case 'sc-pdf-nav': toast(`文档内命中：${C.esc(S.editor.query)}`); return true;

    /* 12 媒体 */
    case 'sc-play': S.media.playing = !S.media.playing; return true;
    case 'sc-prev': S.media.current = (S.media.current + 3) % 4; return true;
    case 'sc-next': S.media.current = (S.media.current + 1) % 4; return true;
    case 'sc-queue': S.media.current = +id; return true;
    case 'sc-chapter': S.media.pos = 12 + (+id) * 12; return true;
    case 'sc-volume': S.media.volume = !S.media.volume; return true;
    case 'sc-subs': S.media.subtitles = !S.media.subtitles; return true;

    /* 13 扩展 / Lua */
    case 'sc-ext': case 'sc-ext-toggle': {
      const k = id; S.extensions.on[k] = !S.extensions.on[k]; return true;
    }
    case 'sc-run': S.extensions.plan = true; S.extensions.confirmed = false;
      toast('脚本已返回操作计划 · 等待确认，尚未写入磁盘'); return true;
    case 'sc-plan-confirm':
      if (!S.extensions.plan) { toast('请先运行脚本生成操作计划'); return true; }
      S.extensions.confirmed = true; toast('3 项重命名已加入任务中心'); return true;

    /* 14 本地 AI */
    case 'sc-ai-query': return true;
    case 'sc-ai-result': toast(`语义结果：${id}`); return true;
    case 'sc-ai-tag': S.ai.tag = S.ai.tag === id ? null : id; return true;
    case 'sc-model': S.ai.model = S.ai.model === 'downloaded' ? 'none' : 'downloaded';
      toast(S.ai.model === 'downloaded' ? '模型已下载 · 可离线使用' : '模型已删除 · 释放 612 MB'); return true;
    case 'sc-model-folder': toast('索引根管理：Pictures、Camera'); return true;

    /* 15 Android */
    case 'sc-grant-new': {
      const n = 'NewFolder' + (S.android.grants.length + 1);
      S.android.grants.push({ name:n, state:'ok', scope:'读写' });
      toast('已通过 SAF 授权新文件夹'); return true;
    }
    case 'sc-regrant': {
      const g = S.android.grants.find(x => x.name === id);
      if (g) { g.state = 'ok'; g.scope = '读写'; }
      toast('已重新授权：' + id); return true;
    }
    case 'sc-restore': {
      const n = S.android.trash[+id]; if (n) S.android.trash.splice(+id, 1);
      toast('已还原到原位置'); return true;
    }
    case 'sc-trash-clear': C.dialog({
      title:'清空回收站', body:'将永久删除 3 个项目（1.8 GB），此操作不可恢复。',
      ok:'永久删除', danger:true, onOk:() => { S.android.trash = []; toast('回收站已清空'); }
    }); return true;
  }
  return false;
}

/* 场景进度动画（由 app.js 定时器驱动） */
function tick() {
  if (S.search.progress > 0 && S.search.progress < 100 && !S.search.paused) S.search.progress = Math.min(100, S.search.progress + 1);
  if (S.archive.running && S.archive.progress < 100) S.archive.progress = Math.min(100, S.archive.progress + 2);
  if (S.media.playing && S.media.pos < 100) S.media.pos = Math.min(100, S.media.pos + 0.25);
}

window.EidoriaScenes = { list:LIST, render, handle, tick, state:S };

})();
