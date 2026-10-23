import { Download } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { errorMessage } from '../../api/client'
import { api, downloadCsv } from '../../api/endpoints'
import type { BookingDetail } from '../../api/types'
import { PriceBreakdown } from '../../components/PriceBreakdown'
import { Alert, EmptyState, Modal, PageHeader, Pagination, SampleBadge, Spinner, StatusBadge } from '../../components/ui'
import { formatDateTime, humanise, money } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

const statuses = ['', 'Pending', 'Confirmed', 'Active', 'Completed', 'Cancelled', 'Rejected']

export default function BookingsAdminPage() {
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState(params.get('q') ?? '')
  const [status, setStatus] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const selected = params.get('ref')
  const query = { q: q || undefined, status: status || undefined, from: from ? `${from}T00:00:00+02:00` : undefined, to: to ? `${to}T23:59:59+02:00` : undefined }
  const list = useAsync(() => api.admin.bookings({ ...query, page, pageSize: 25 }), [q, status, from, to, page])

  const open = (ref: string | null) => { const next = new URLSearchParams(params); if (ref) next.set('ref', ref); else next.delete('ref'); setParams(next) }

  return (
    <>
      <PageHeader title="Bookings" subtitle="Search, inspect and move bookings through their lifecycle."
        actions={<>
          {exportError && <span className="self-center text-xs text-rose-700">{exportError}</span>}
          <button className="btn-outline" disabled={exporting} onClick={async () => { setExporting(true); setExportError(null); try { await downloadCsv('/api/admin/bookings/export', query, 'smartdrive-bookings.csv') } catch (e) { setExportError(errorMessage(e)) } finally { setExporting(false) } }}><Download className="size-4" />Export CSV</button>
        </>} />
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-48 flex-1"><label htmlFor="b-q" className="label">Search</label><input id="b-q" className="input" placeholder="Reference, customer or vehicle" defaultValue={q} onKeyDown={(e) => { if (e.key === 'Enter') { setQ(e.currentTarget.value.trim()); setPage(1) } }} onBlur={(e) => { setQ(e.currentTarget.value.trim()); setPage(1) }} /></div>
        <div><label htmlFor="b-st" className="label">Status</label><select id="b-st" className="input" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}>{statuses.map((s) => <option key={s} value={s}>{s || 'All'}</option>)}</select></div>
        <div><label htmlFor="b-from" className="label">Pickup from</label><input id="b-from" type="date" className="input" value={from} onChange={(e) => { setFrom(e.target.value); setPage(1) }} /></div>
        <div><label htmlFor="b-to" className="label">Pickup to</label><input id="b-to" type="date" className="input" value={to} onChange={(e) => { setTo(e.target.value); setPage(1) }} /></div>
      </div>
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data && (list.data.items.length === 0 ? <EmptyState title="No bookings match" /> : (
        <div className="card overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>Reference</th><th>Customer</th><th>Vehicle</th><th>Pickup</th><th>Return</th><th>Status</th><th className="text-right">Total</th></tr></thead>
            <tbody>{list.data.items.map((b) => (
              <tr key={b.id} className="cursor-pointer hover:bg-sand-50" onClick={() => open(b.reference)}>
                <td><button className="font-mono text-xs font-bold text-amber-700 underline" onClick={(e) => { e.stopPropagation(); open(b.reference) }}>{b.reference}</button>{b.isSampleData && <><br /><SampleBadge className="mt-1" /></>}</td>
                <td>{b.customerName}<br /><span className="text-xs text-ink-500">{b.customerEmail}</span></td>
                <td>{b.vehicleName}</td>
                <td className="whitespace-nowrap text-xs">{formatDateTime(b.pickupAt)}<br /><span className="text-ink-500">{b.pickupLocation}</span></td>
                <td className="whitespace-nowrap text-xs">{formatDateTime(b.returnAt)}<br /><span className="text-ink-500">{b.returnLocation}</span></td>
                <td><StatusBadge status={b.status} /><br /><span className="text-xs text-ink-500">{humanise(b.paymentStatus)}</span></td>
                <td className="text-right font-semibold">{money(b.totalAmount)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ))}
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
      <BookingDrawer reference={selected} onClose={() => open(null)} onChanged={list.reload} />
    </>
  )
}

function BookingDrawer({ reference, onClose, onChanged }: { reference: string | null; onClose: () => void; onChanged: () => void }) {
  const detail = useAsync(() => (reference ? api.admin.booking(reference) : Promise.resolve(null)), [reference])
  const [target, setTarget] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [odometer, setOdometer] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  useEffect(() => { setTarget(null); setReason(''); setOdometer(''); setError(null) }, [reference])

  const b: BookingDetail | null | undefined = detail.data
  const needsOdometer = target === 'Active' || target === 'Completed'
  const needsReason = target === 'Rejected' || target === 'Cancelled'

  return (
    <Modal open={!!reference} title={`Booking ${reference ?? ''}`} onClose={onClose} wide>
      {detail.loading && <Spinner />}
      {detail.error && <Alert onRetry={detail.reload}>{detail.error}</Alert>}
      {b && (
        <div className="grid gap-5 md:grid-cols-2">
          <div className="space-y-3 text-sm">
            <div className="flex flex-wrap gap-2"><StatusBadge status={b.summary.status} /><StatusBadge status={b.summary.paymentStatus} />{b.summary.isSampleData && <SampleBadge />}</div>
            <p><strong>{b.summary.customerName}</strong> · {b.summary.customerEmail}</p>
            <p><strong>{b.summary.vehicleName}</strong></p>
            <p>Pickup {formatDateTime(b.summary.pickupAt)} · {b.summary.pickupLocation}<br />Return {formatDateTime(b.summary.returnAt)} · {b.summary.returnLocation}</p>
            {b.pickupOdometerKm != null && <p>Odometer {b.pickupOdometerKm} km{b.returnOdometerKm != null ? ` → ${b.returnOdometerKm} km (${b.returnOdometerKm - b.pickupOdometerKm} km driven)` : ''}</p>}
            <ol className="space-y-1 border-l-2 border-ink-100 pl-3 text-xs">
              {b.statusHistory.map((h, i) => <li key={i}><strong>{humanise(h.to)}</strong> · {formatDateTime(h.at)}{h.reason ? ` · ${h.reason}` : ''}</li>)}
            </ol>
          </div>
          <div>
            <PriceBreakdown lines={b.lines} total={b.summary.totalAmount} vat={b.vatAmount} />
            {b.payments.length > 0 && (
              <ul className="mt-3 space-y-1 text-xs">{b.payments.map((p) => <li key={p.id} className="flex justify-between"><span>{humanise(p.kind)} · {humanise(p.status)}{p.isSimulated ? ' (simulated)' : ''}</span><span>{money(p.amount)}</span></li>)}</ul>
            )}
            <div className="mt-5 rounded-xl bg-sand-100 p-3">
              <p className="text-sm font-bold">Change status</p>
              {b.allowedTransitions.length === 0 ? <p className="mt-1 text-xs text-ink-500">No further transitions are allowed from {humanise(b.summary.status).toLowerCase()}.</p> : (
                <>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {b.allowedTransitions.map((t) => <button key={t} className={target === t ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} onClick={() => setTarget(t)}>{t === 'Active' ? 'Hand over (Active)' : t === 'Completed' ? 'Return (Completed)' : humanise(t)}</button>)}
                  </div>
                  {target && (
                    <form className="mt-3 space-y-2" onSubmit={async (e) => {
                      e.preventDefault(); setBusy(true); setError(null)
                      try { detail.setData(await api.admin.changeBookingStatus(b.summary.reference, { status: target, reason: reason.trim() || null, odometerKm: odometer ? Number(odometer) : null })); setTarget(null); onChanged() } catch (err) { setError(errorMessage(err)) } finally { setBusy(false) }
                    }}>
                      {needsOdometer && <div><label htmlFor="bs-odo" className="label">Odometer (km)</label><input id="bs-odo" required type="number" min={0} className="input" value={odometer} onChange={(e) => setOdometer(e.target.value)} /></div>}
                      <div><label htmlFor="bs-reason" className="label">Reason{needsReason ? '' : ' (optional)'}</label><input id="bs-reason" required={needsReason} maxLength={500} className="input" value={reason} onChange={(e) => setReason(e.target.value)} /></div>
                      {target === 'Completed' && <p className="text-xs text-ink-500">Kilometres over the included allowance are charged automatically.</p>}
                      {target === 'Cancelled' && <p className="text-xs text-ink-500">A cancellation by SmartDrive carries no fee; anything paid is refunded in full.</p>}
                      {error && <Alert>{error}</Alert>}
                      <button className="btn-primary btn-sm" disabled={busy}>Confirm {humanise(target).toLowerCase()}</button>
                    </form>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
      )}
    </Modal>
  )
}
