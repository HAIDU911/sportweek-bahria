import { createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ButtonHTMLAttributes, type ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { AlertTriangle, CloudOff, Inbox, X } from 'lucide-react';
import { ApiError } from '../lib/api';
import type { FixtureStatus, Role, TeamStatus } from '../lib/types';

export const cx = (...a: (string | false | null | undefined)[]) => a.filter(Boolean).join(' ');
export function errMsg(e: unknown): string { return e instanceof Error ? e.message : 'Something went wrong.'; }

export function Btn({ loading, variant, size, block, className, children, disabled, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { loading?: boolean; variant?: 'ghost' | 'lime' | 'danger'; size?: 'sm'; block?: boolean }) {
  return (
    <button {...rest} disabled={disabled || loading} aria-busy={loading || undefined} className={cx('btn', variant, size, block && 'block', className)}>
      {loading && <span className="spin" aria-hidden />}{children}
    </button>
  );
}

const statusClass: Record<string, string> = { Live: 'live', Pending: 'pending', Ready: 'pending', Approved: 'approved', Rejected: 'rejected', Withdrawn: 'withdrawn', Completed: 'approved', Walkover: 'approved', Scheduled: '', RegistrationOpen: 'live', InProgress: 'live', Cancelled: 'withdrawn' };
const statusLabel: Record<string, string> = { RegistrationOpen: 'Registration open', RegistrationClosed: 'Registration closed', InProgress: 'In progress', DoubleForfeit: 'Double forfeit' };
export function StatusBadge({ status }: { status: FixtureStatus | TeamStatus | string }) {
  return <span className={cx('badge', statusClass[status])}>{status === 'Live' && <span className="dot" aria-hidden />}{statusLabel[status] ?? status}</span>;
}
export const RoleBadge = ({ role }: { role: Role | string }) => <span className={cx('badge role', role === 'Admin' && 'admin')}>{role}</span>;

export function StatTile({ eyebrow, value, label, icon, alert }: { eyebrow: string; value: ReactNode; label: string; icon?: ReactNode; alert?: boolean }) {
  return (
    <div className={cx('card stat lift', alert && 'alert')}>
      <div className="eyebrow"><span>{eyebrow}</span>{icon}</div>
      <div className="n">{value}</div><div className="l">{label}</div>
    </div>
  );
}

export const Skeleton = ({ h = 16, w = '100%' }: { h?: number; w?: string | number }) => <div className="skel" style={{ height: h, width: w }} aria-hidden />;
export function SkeletonList({ rows = 4, h = 56 }: { rows?: number; h?: number }) {
  return <div className="stack" role="status" aria-label="Loading">{Array.from({ length: rows }, (_, i) => <Skeleton key={i} h={h} />)}</div>;
}

export function EmptyState({ title = 'Nothing here yet', children, action }: { title?: string; children?: ReactNode; action?: ReactNode }) {
  return <div className="state"><Inbox size={28} aria-hidden /><h3>{title}</h3><p>{children}</p>{action}</div>;
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const e = error instanceof ApiError ? error : null;
  const down = e?.isUnavailable ?? false;
  return (
    <div className="state error" role="alert">
      {down ? <CloudOff size={28} aria-hidden /> : <AlertTriangle size={28} aria-hidden />}
      <h3>{down ? 'Temporarily offline' : e?.status === 403 ? 'Not allowed' : e?.status === 429 ? 'Slow down a little' : 'Something went wrong'}</h3>
      <p>{down ? 'The scoreboard is temporarily offline — results are safe. Retry in a moment.' : errMsg(error)}{e?.retryAfter ? ` Try again in ${e.retryAfter}s.` : ''}</p>
      {e?.correlationId && <p className="mono" style={{ fontSize: 11 }}>Ref: {e.correlationId}</p>}
      {onRetry && <Btn onClick={onRetry}>Retry</Btn>}
    </div>
  );
}

/** One place that renders loading / error / empty for any query. */
export function DataState<T>({ q, empty, isEmpty, skeleton, children }: {
  q: UseQueryResult<T>; empty?: ReactNode; isEmpty?: (d: T) => boolean; skeleton?: ReactNode; children: (d: T) => ReactNode;
}) {
  if (q.isPending) return <>{skeleton ?? <SkeletonList />}</>;
  if (q.isError) return <ErrorState error={q.error} onRetry={() => void q.refetch()} />;
  if (isEmpty?.(q.data)) return <>{empty ?? <EmptyState />}</>;
  return <>{children(q.data)}</>;
}

export function Pager({ page, totalPages, total, onPage }: { page: number; totalPages: number; total: number; onPage: (p: number) => void }) {
  if (total === 0) return null;
  return (
    <nav className="pager" aria-label="Pagination">
      <span className="eyebrow">{total.toLocaleString()} result{total === 1 ? '' : 's'} · page {page} of {Math.max(totalPages, 1)}</span>
      <div className="row"><Btn size="sm" variant="ghost" disabled={page <= 1} onClick={() => onPage(page - 1)}>Prev</Btn><Btn size="sm" variant="ghost" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>Next</Btn></div>
    </nav>
  );
}

export function Field({ label, error, help, children }: { label: string; error?: string; help?: string; children: (p: { id: string; 'aria-invalid'?: boolean; 'aria-describedby'?: string }) => ReactNode }) {
  const id = useId(); const dId = `${id}-d`;
  return (
    <div className="field"><label htmlFor={id}>{label}</label>
      {children({ id, 'aria-invalid': error ? true : undefined, 'aria-describedby': error || help ? dId : undefined })}
      {error ? <span id={dId} className="err" role="alert">{error}</span> : help ? <span id={dId} className="help">{help}</span> : null}
    </div>
  );
}

function useOverlay(onClose: () => void) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'Tab' && ref.current) { // simple focus trap
        const f = ref.current.querySelectorAll<HTMLElement>('a[href],button:not(:disabled),input,select,textarea,[tabindex]:not([tabindex="-1"])');
        if (!f.length) return; const first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); } else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    };
    document.addEventListener('keydown', onKey); document.body.style.overflow = 'hidden';
    ref.current?.querySelector<HTMLElement>('[data-autofocus],input,select,textarea,button')?.focus();
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = ''; prev?.focus(); };
  }, [onClose]);
  return ref;
}

export function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const ref = useOverlay(onClose);
  return (
    <div className="scrim" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="drawer" ref={ref} role="dialog" aria-modal="true" aria-label={title}>
        <header><h3>{title}</h3><Btn size="sm" variant="ghost" onClick={onClose} aria-label="Close"><X size={14} /></Btn></header>
        <div className="body">{children}</div>
      </div>
    </div>
  );
}

export function Dialog({ title, onClose, children, footer }: { title: string; onClose: () => void; children: ReactNode; footer?: ReactNode }) {
  const ref = useOverlay(onClose);
  return (
    <div className="scrim center" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog" ref={ref} role="dialog" aria-modal="true" aria-label={title}>
        <header><h3>{title}</h3><Btn size="sm" variant="ghost" onClick={onClose} aria-label="Close"><X size={14} /></Btn></header>
        <div className="body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  );
}

interface Toast { id: number; text: string; kind: 'ok' | 'error' }
const ToastCtx = createContext<{ ok: (t: string) => void; error: (e: unknown) => void } | null>(null);
export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Toast[]>([]);
  const push = useCallback((text: string, kind: Toast['kind']) => {
    const id = Date.now() + Math.random(); setItems((s) => [...s, { id, text, kind }]); setTimeout(() => setItems((s) => s.filter((t) => t.id !== id)), 5000);
  }, []);
  const api = { ok: (t: string) => push(t, 'ok'), error: (e: unknown) => push(errMsg(e), 'error') };
  return <ToastCtx.Provider value={api}>{children}<div className="toasts" aria-live="polite">{items.map((t) => <div key={t.id} className={cx('toast', t.kind === 'error' && 'error')} role={t.kind === 'error' ? 'alert' : 'status'}>{t.text}</div>)}</div></ToastCtx.Provider>;
}
export function useToast() { const c = useContext(ToastCtx); if (!c) throw new Error('useToast outside ToastProvider'); return c; }

export function Tabs<T extends string>({ value, onChange, tabs }: { value: T; onChange: (v: T) => void; tabs: { id: T; label: string }[] }) {
  return <div className="tabs" role="tablist">{tabs.map((t) => <button key={t.id} role="tab" aria-selected={value === t.id} className="tab" onClick={() => onChange(t.id)}>{t.label}</button>)}</div>;
}
export function Segmented<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: { id: T; label: string }[] }) {
  return <div className="seg" role="group">{options.map((o) => <button key={o.id} aria-pressed={value === o.id} onClick={() => onChange(o.id)}>{o.label}</button>)}</div>;
}
export function Chips<T extends string>({ value, onChange, options }: { value: T; onChange: (v: T) => void; options: { id: T; label: string }[] }) {
  return <div className="row" role="group" style={{ gap: 8 }}>{options.map((o) => <button key={o.id} className="chip" aria-pressed={value === o.id} onClick={() => onChange(o.id)}>{o.label}</button>)}</div>;
}
export function useDebounced<T>(v: T, ms = 300): T { const [d, setD] = useState(v); useEffect(() => { const t = setTimeout(() => setD(v), ms); return () => clearTimeout(t); }, [v, ms]); return d; }
export function PageHead({ eyebrow, title, children }: { eyebrow?: string; title: string; children?: ReactNode }) {
  return <div className="section-head"><div>{eyebrow && <div className="eyebrow" style={{ marginBottom: 10 }}>{eyebrow}</div>}<h2>{title}</h2></div>{children}</div>;
}
