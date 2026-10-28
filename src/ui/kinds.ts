// File-kind display metadata: icon wells follow the concept art (type color only in small wells).
export type Kind =
  | 'folder' | 'image' | 'video' | 'audio' | 'pdf' | 'archive'
  | 'markdown' | 'code' | 'text' | 'doc' | 'font' | 'package' | 'other';

export interface KindMeta {
  label: string;
  icon: string;        // icon name in icons.tsx
  wellBg: string;      // solid well color (type color only in small wells)
  wellFg: string;      // glyph color
}

export const KINDS: Record<Kind, KindMeta> = {
  folder:   { label: '文件夹', icon: 'folder',            wellBg: 'var(--well-folder-bg)', wellFg: 'var(--well-folder-fg)' },
  image:    { label: '图片',   icon: 'image',             wellBg: '#5B8FE0', wellFg: '#FFFFFF' },
  video:    { label: '视频',   icon: 'videocam',          wellBg: '#8D6BC4', wellFg: '#FFFFFF' },
  audio:    { label: '音频',   icon: 'audio_file',        wellBg: '#2FA39B', wellFg: '#FFFFFF' },
  pdf:      { label: 'PDF',    icon: 'picture_as_pdf',    wellBg: '#D95F59', wellFg: '#FFFFFF' },
  archive:  { label: '压缩包', icon: 'folder_zip',        wellBg: '#C79A3E', wellFg: '#FFFFFF' },
  markdown: { label: 'Markdown', icon: 'code',            wellBg: '#8D6BC4', wellFg: '#FFFFFF' },
  code:     { label: '代码',   icon: 'code',              wellBg: '#6FA86B', wellFg: '#FFFFFF' },
  text:     { label: '文本',   icon: 'description',       wellBg: '#7A8A84', wellFg: '#FFFFFF' },
  doc:      { label: '文档',   icon: 'description',       wellBg: '#7A8A84', wellFg: '#FFFFFF' },
  font:     { label: '字体',   icon: 'insert_drive_file', wellBg: '#7A8A84', wellFg: '#FFFFFF' },
  package:  { label: '安装包', icon: 'inventory_2',       wellBg: '#7A8A84', wellFg: '#FFFFFF' },
  other:    { label: '文件',   icon: 'insert_drive_file', wellBg: '#7A8A84', wellFg: '#FFFFFF' },
};

export function kindMeta(kind: string): KindMeta {
  return KINDS[kind as Kind] ?? KINDS.other;
}

/** Categories used by the home page aggregation (per concept art). */
export const CATEGORIES = [
  { id: 'image', label: '图片', icon: 'image', kinds: ['image'] },
  { id: 'video', label: '视频', icon: 'movie', kinds: ['video'] },
  { id: 'audio', label: '音频', icon: 'audiotrack', kinds: ['audio'] },
  { id: 'doc', label: '文档', icon: 'description', kinds: ['pdf', 'doc', 'markdown', 'text'] },
  { id: 'archive', label: '压缩包', icon: 'folder_zip', kinds: ['archive'] },
] as const;

export function formatSize(n: number | undefined): string {
  if (n === undefined || n === null) return '—';
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v >= 100 ? Math.round(v) : v.toFixed(1)} ${units[i]}`;
}

export function formatCount(n: number | undefined): string {
  if (n === undefined) return '—';
  return `${n} 个项目`;
}

export function formatTime(ms: number | undefined): string {
  if (!ms) return '—';
  const d = new Date(ms);
  const p = (x: number) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
