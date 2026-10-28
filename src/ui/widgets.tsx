import React, { useEffect, useRef, useState } from 'react';
import { Icon } from './icons';

/* --------------------------------- buttons --------------------------------- */

export function IconButton(props: {
  name: string; title?: string; onClick?: (e: React.MouseEvent) => void;
  size?: number; variant?: 'tonal' | 'plain' | 'outline' | 'primary' | 'danger' | 'search';
  disabled?: boolean; active?: boolean; className?: string; badge?: number;
}) {
  const { name, title, onClick, size = 20, variant = 'tonal', disabled, active, className = '', badge } = props;
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={onClick}
      className={`iconbtn iconbtn--${variant} ${active ? 'is-active' : ''} ${className}`}
    >
      <Icon name={name} size={size} />
      {badge !== undefined && badge > 0 && <span className="iconbtn__badge">{badge}</span>}
    </button>
  );
}

export function Button(props: {
  children?: React.ReactNode; icon?: string; onClick?: () => void;
  variant?: 'filled' | 'tonal' | 'outline' | 'text' | 'danger'; className?: string; disabled?: boolean;
}) {
  const { children, icon, onClick, variant = 'filled', className = '', disabled } = props;
  return (
    <button type="button" className={`btn btn--${variant} ${className}`} onClick={onClick} disabled={disabled}>
      {icon && <Icon name={icon} size={18} />}
      {children && <span>{children}</span>}
    </button>
  );
}

export function Chip(props: { icon?: string; children: React.ReactNode; className?: string; title?: string }) {
  return (
    <span className={`chip ${props.className ?? ''}`} title={props.title}>
      {props.icon && <Icon name={props.icon} size={15} />}
      {props.children}
    </span>
  );
}

export function Toggle(props: { checked: boolean; onChange: (b: boolean) => void; label: string; desc?: string }) {
  return (
    <label className="toggle-row">
      <span className="toggle-row__text">
        <span className="toggle-row__label">{props.label}</span>
        {props.desc && <span className="toggle-row__desc">{props.desc}</span>}
      </span>
      <button
        type="button"
        role="switch"
        aria-checked={props.checked}
        className={`switch ${props.checked ? 'is-on' : ''}`}
        onClick={() => props.onChange(!props.checked)}
      >
        <span className="switch__thumb" />
      </button>
    </label>
  );
}

export function Segmented<T extends string>(props: { value: T; options: { id: T; label: string; icon?: string }[]; onChange: (v: T) => void }) {
  return (
    <div className="segmented" role="tablist">
      {props.options.map((o) => (
        <button
          key={o.id}
          role="tab"
          aria-selected={props.value === o.id}
          className={`segmented__item ${props.value === o.id ? 'is-active' : ''}`}
          onClick={() => props.onChange(o.id)}
        >
          {o.icon && <Icon name={o.icon} size={16} />}
          <span>{o.label}</span>
        </button>
      ))}
    </div>
  );
}

/* --------------------------------- menu --------------------------------- */

export interface MenuItem {
  id: string;
  label?: string;
  icon?: string;
  danger?: boolean;
  disabled?: boolean;
  separator?: boolean;
}

export function Menu(props: {
  items: MenuItem[];
  x: number; y: number;
  onClose: () => void;
  onPick: (id: string) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) props.onClose();
    };
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && props.onClose();
    document.addEventListener('mousedown', close, true);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', close, true);
      document.removeEventListener('keydown', esc);
    };
  }, [props]);

  const w = 200, h = props.items.filter((i) => !i.separator).length * 36 + 8;
  const x = Math.min(props.x, window.innerWidth - w - 8);
  const y = Math.min(props.y, window.innerHeight - h - 8);
  return (
    <div className="menu" ref={ref} style={{ left: x, top: y }} role="menu">
      {props.items.map((it, i) =>
        it.separator ? (
          <div key={i} className="menu__sep" />
        ) : (
          <button
            key={it.id}
            role="menuitem"
            className={`menu__item ${it.danger ? 'is-danger' : ''}`}
            disabled={it.disabled}
            onClick={() => { props.onPick(it.id); props.onClose(); }}
          >
            {it.icon && <Icon name={it.icon} size={18} />}
            <span>{it.label}</span>
          </button>
        ),
      )}
    </div>
  );
}

/* --------------------------------- dialog --------------------------------- */

export function Dialog(props: {
  title: string;
  children: React.ReactNode;
  onClose: () => void;
  actions?: React.ReactNode;
  width?: number;
}) {
  useEffect(() => {
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && props.onClose();
    document.addEventListener('keydown', esc);
    return () => document.removeEventListener('keydown', esc);
  }, [props]);
  return (
    <div className="scrim" onMouseDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="dialog" style={{ width: props.width ?? 420 }} role="dialog" aria-modal="true" aria-label={props.title}>
        <h2 className="dialog__title">{props.title}</h2>
        <div className="dialog__body">{props.children}</div>
        {props.actions && <div className="dialog__actions">{props.actions}</div>}
      </div>
    </div>
  );
}

export function TextField(props: {
  value: string; onChange: (v: string) => void; label?: string;
  onEnter?: () => void; autoFocus?: boolean; placeholder?: string; selectAllOnFocus?: boolean;
}) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (props.autoFocus) {
      ref.current?.focus();
      if (props.selectAllOnFocus) ref.current?.select();
    }
  }, [props.autoFocus, props.selectAllOnFocus]);
  return (
    <label className="textfield">
      {props.label && <span className="textfield__label">{props.label}</span>}
      <input
        ref={ref}
        value={props.value}
        placeholder={props.placeholder}
        onChange={(e) => props.onChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key !== 'Escape') e.stopPropagation();
          if (e.key === 'Enter') props.onEnter?.();
        }}
      />
    </label>
  );
}

/* --------------------------------- toggleable icon-well --------------------------------- */

export function IconWell(props: { bg: string; fg: string; icon: string; size?: number; radius?: number }) {
  const { bg, fg, icon, size = 40, radius } = props;
  return (
    <span
      className="iconwell"
      style={{
        width: size, height: size, background: bg, color: fg,
        borderRadius: radius ?? Math.round(size * 0.32),
      }}
    >
      <Icon name={icon} size={Math.round(size * 0.56)} />
    </span>
  );
}

export function useDrag(onMove: (dx: number, dy: number) => void) {
  const start = useRef<{ x: number; y: number } | null>(null);
  return {
    onMouseDown: (e: React.MouseEvent) => {
      if (e.button !== 0) return;
      start.current = { x: e.clientX, y: e.clientY };
      const move = (ev: MouseEvent) => {
        if (!start.current) return;
        onMove(ev.clientX - start.current.x, ev.clientY - start.current.y);
        start.current = { x: ev.clientX, y: ev.clientY };
      };
      const up = () => {
        start.current = null;
        document.removeEventListener('mousemove', move);
        document.removeEventListener('mouseup', up);
      };
      document.addEventListener('mousemove', move);
      document.addEventListener('mouseup', up);
    },
  };
}
