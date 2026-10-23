import { AlertTriangle, CheckCircle2, Info, Loader2, Star, X } from 'lucide-react'
import { useEffect, useRef, type ReactNode } from 'react'
import { humanise } from '../lib/format'

export function Spinner({ label = 'Loading…', className = '' }: { label?: string; className?: string }) {
  return (
    <div role="status" className={`flex items-center justify-center gap-2 py-10 text-sm text-ink-500 ${className}`}>
      <Loader2 className="size-5 animate-spin text-amber-600" aria-hidden />
      <span>{label}</span>
    </div>
  )
}

export function Alert({ tone = 'error', title, children, onRetry }: { tone?: 'error' | 'info' | 'success' | 'warning'; title?: string; children?: ReactNode; onRetry?: () => void }) {
  const styles = {
    error: 'border-rose-600/25 bg-rose-50 text-rose-700',
    warning: 'border-amber-600/30 bg-amber-400/10 text-amber-700',
    info: 'border-ink-200 bg-ink-100/60 text-ink-800',
    success: 'border-leaf-600/25 bg-leaf-50 text-leaf-700',
  }[tone]
  const Icon = tone === 'success' ? CheckCircle2 : tone === 'info' ? Info : AlertTriangle
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={`flex gap-3 rounded-xl border px-4 py-3 text-sm ${styles}`}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <div className="min-w-0 flex-1">
        {title && <p className="font-bold">{title}</p>}
        {children && <div className={title ? 'mt-0.5' : ''}>{children}</div>}
        {onRetry && <button type="button" onClick={onRetry} className="mt-2 font-bold underline underline-offset-2">Try again</button>}
      </div>
    </div>
  )
}

export function EmptyState({ icon, title, children, action }: { icon?: ReactNode; title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center rounded-2xl border border-dashed border-ink-200 bg-white/60 px-6 py-12 text-center">
      {icon && <div className="mb-3 text-ink-400">{icon}</div>}
      <p className="text-base font-bold text-ink-900">{title}</p>
      {children && <div className="mt-1 max-w-md text-sm text-ink-500">{children}</div>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

/** Marks generated demo content so nobody mistakes it for real customers, vehicles or reviews. */
export function SampleBadge({ className = '' }: { className?: string }) {
  return (
    <span title="Generated sample data for demonstration" className={`chip bg-ink-100 text-ink-500 ${className}`}>
      Sample data
    </span>
  )
}

const statusTones: Record<string, string> = {
  pending: 'bg-amber-400/20 text-amber-700',
  confirmed: 'bg-leaf-50 text-leaf-700',
  active: 'bg-ink-900 text-white',
  completed: 'bg-ink-100 text-ink-700',
  cancelled: 'bg-rose-50 text-rose-700',
  rejected: 'bg-rose-50 text-rose-700',
  available: 'bg-leaf-50 text-leaf-700',
  reserved: 'bg-amber-400/20 text-amber-700',
  rented: 'bg-ink-900 text-white',
  undermaintenance: 'bg-rose-50 text-rose-700',
  inactive: 'bg-ink-100 text-ink-500',
  scheduled: 'bg-amber-400/20 text-amber-700',
  inprogress: 'bg-ink-900 text-white',
  overdue: 'bg-rose-50 text-rose-700',
  duesoon: 'bg-amber-400/20 text-amber-700',
  ok: 'bg-leaf-50 text-leaf-700',
  published: 'bg-leaf-50 text-leaf-700',
  reported: 'bg-amber-400/20 text-amber-700',
  hidden: 'bg-ink-100 text-ink-500',
  removed: 'bg-ink-100 text-ink-500',
  paid: 'bg-leaf-50 text-leaf-700',
  unpaid: 'bg-amber-400/20 text-amber-700',
  refunded: 'bg-ink-100 text-ink-700',
  partiallyrefunded: 'bg-ink-100 text-ink-700',
  positive: 'bg-leaf-50 text-leaf-700',
  negative: 'bg-rose-50 text-rose-700',
  mixed: 'bg-amber-400/20 text-amber-700',
  neutral: 'bg-ink-100 text-ink-700',
}

export function StatusBadge({ status, className = '' }: { status: string; className?: string }) {
  const tone = statusTones[status.toLowerCase().replace(/[^a-z]/g, '')] ?? 'bg-ink-100 text-ink-700'
  return <span className={`chip ${tone} ${className}`}>{humanise(status)}</span>
}

export function Stars({ value, size = 14, label }: { value: number | null | undefined; size?: number; label?: string }) {
  const v = value ?? 0
  return (
    <span className="inline-flex items-center gap-0.5" role="img" aria-label={label ?? (value ? `${v.toFixed(1)} out of 5` : 'No rating yet')}>
      {[1, 2, 3, 4, 5].map((i) => (
        <Star key={i} width={size} height={size} aria-hidden
          className={i <= Math.round(v) ? 'fill-amber-500 text-amber-500' : 'fill-ink-100 text-ink-200'} />
      ))}
    </span>
  )
}

export function StarInput({ value, onChange, label }: { value: number; onChange: (v: number) => void; label: string }) {
  return (
    <fieldset>
      <legend className="label">{label}</legend>
      <div className="flex gap-1" role="radiogroup" aria-label={label}>
        {[1, 2, 3, 4, 5].map((i) => (
          <button key={i} type="button" role="radio" aria-checked={value === i} aria-label={`${i} star${i > 1 ? 's' : ''}`}
            onClick={() => onChange(i)} className="rounded p-0.5">
            <Star className={`size-6 ${i <= value ? 'fill-amber-500 text-amber-500' : 'fill-ink-100 text-ink-200'}`} aria-hidden />
          </button>
        ))}
      </div>
    </fieldset>
  )
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null
  return (
    <nav aria-label="Pagination" className="mt-6 flex items-center justify-center gap-3 text-sm">
      <button className="btn-outline btn-sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>Previous</button>
      <span className="text-ink-500">Page {page} of {totalPages}</span>
      <button className="btn-outline btn-sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>Next</button>
    </nav>
  )
}

export function Modal({ open, title, onClose, children, wide = false }: { open: boolean; title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  const panel = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const previous = document.activeElement as HTMLElement | null
    panel.current?.focus()
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = overflow
      previous?.focus?.()
    }
  }, [open, onClose])
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-ink-950/60 p-0 sm:items-center sm:p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label={title} tabIndex={-1}
        className={`w-full ${wide ? 'sm:max-w-3xl' : 'sm:max-w-lg'} rounded-t-2xl bg-white shadow-2xl outline-none sm:rounded-2xl`}>
        <div className="flex items-center justify-between border-b border-ink-100 px-5 py-4">
          <h2 className="text-lg">{title}</h2>
          <button type="button" onClick={onClose} className="btn-ghost btn-sm" aria-label="Close"><X className="size-4" /></button>
        </div>
        <div className="max-h-[75vh] overflow-y-auto px-5 py-4">{children}</div>
      </div>
    </div>
  )
}

export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: ReactNode; tone?: 'warn' | 'good' }) {
  return (
    <div className="card p-4">
      <p className="text-xs font-bold uppercase tracking-wide text-ink-500">{label}</p>
      <p className={`mt-1 text-2xl font-extrabold ${tone === 'warn' ? 'text-rose-700' : tone === 'good' ? 'text-leaf-700' : 'text-ink-900'}`}>{value}</p>
      {hint && <p className="mt-0.5 text-xs text-ink-500">{hint}</p>}
    </div>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h1 className="text-2xl sm:text-3xl">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-500">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
