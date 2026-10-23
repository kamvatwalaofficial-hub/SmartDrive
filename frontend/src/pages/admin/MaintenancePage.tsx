import { AlertTriangle, Plus } from 'lucide-react'
import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import type { Maintenance } from '../../api/types'
import { Alert, EmptyState, Modal, PageHeader, Pagination, SampleBadge, Spinner, StatusBadge } from '../../components/ui'
import { formatDate, formatDateTime, isoToLocalInput, localInputToIso, money, number } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

export default function MaintenancePage() {
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)
  const [completing, setCompleting] = useState<Maintenance | null>(null)
  const [error, setError] = useState<string | null>(null)
  const alerts = useAsync(() => api.admin.alerts(), [])
  const list = useAsync(() => api.admin.maintenance({ status: status || undefined, page, pageSize: 20 }), [status, page])
  const refresh = () => { list.reload(); alerts.reload() }
  const act = async (fn: () => Promise<unknown>) => { setError(null); try { await fn(); refresh() } catch (e) { setError(errorMessage(e)) } }

  return (
    <>
      <PageHeader title="Maintenance" subtitle="Service schedule, workshop jobs and reported issues. Scheduled work blocks bookings for its window." actions={<button className="btn-primary" onClick={() => setCreating(true)}><Plus className="size-4" />Schedule work</button>} />

      <section className="card mb-6 p-5">
        <h2 className="flex items-center gap-2 text-base"><AlertTriangle className="size-4 text-amber-600" />Service alerts</h2>
        {alerts.loading && <Spinner />}
        {alerts.data?.length === 0 && <p className="mt-2 text-sm text-ink-500">No vehicles are due, overdue or showing repeated issues.</p>}
        {alerts.data && alerts.data.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            <table className="table-base">
              <thead><tr><th>Vehicle</th><th>State</th><th className="text-right">Odometer</th><th className="text-right">Km left</th><th className="text-right">Days left</th><th>Reported issues (90 days)</th></tr></thead>
              <tbody>{alerts.data.map((a) => (
                <tr key={a.vehicleId}>
                  <td className="font-semibold">{a.vehicleName}<br /><span className="font-mono text-xs font-normal text-ink-500">{a.registrationNumber}</span></td>
                  <td><StatusBadge status={a.serviceState} /></td>
                  <td className="text-right">{number(a.odometerKm)} km</td>
                  <td className={`text-right ${a.kmRemaining !== null && a.kmRemaining < 0 ? 'font-bold text-rose-700' : ''}`}>{a.kmRemaining === null ? '—' : `${number(a.kmRemaining)} km`}</td>
                  <td className={`text-right ${a.daysRemaining !== null && a.daysRemaining < 0 ? 'font-bold text-rose-700' : ''}`}>{a.daysRemaining ?? '—'}</td>
                  <td>{a.hasRepeatedIssues ? <span className="chip bg-rose-50 text-rose-700">{a.recentIssueCount} — repeated</span> : a.recentIssueCount}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </section>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        {['', 'Scheduled', 'InProgress', 'Completed', 'Cancelled'].map((s) => (
          <button key={s} className={status === s ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} onClick={() => { setStatus(s); setPage(1) }}>{s ? s.replace('InProgress', 'In progress') : 'All'}</button>
        ))}
      </div>
      {error && <div className="mb-3"><Alert>{error}</Alert></div>}
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data && (list.data.items.length === 0 ? <EmptyState title="No maintenance records" /> : (
        <div className="card overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>Vehicle</th><th>Work</th><th>Window</th><th>Status</th><th className="text-right">Cost</th><th /></tr></thead>
            <tbody>{list.data.items.map((m) => (
              <tr key={m.id}>
                <td className="font-semibold">{m.vehicleName}<br /><span className="font-mono text-xs font-normal text-ink-500">{m.registrationNumber}</span></td>
                <td>{m.title}<br /><span className="text-xs text-ink-500">{m.type}{m.isReportedIssue ? ' · reported issue' : ''}{m.odometerKm ? ` · ${number(m.odometerKm)} km` : ''}</span> {m.isSampleData && <SampleBadge />}</td>
                <td className="whitespace-nowrap text-xs">{formatDateTime(m.startAt)}<br />{m.endAt ? `→ ${formatDateTime(m.endAt)}` : 'open-ended'}</td>
                <td><StatusBadge status={m.status} /></td>
                <td className="text-right">{m.cost ? money(m.cost) : '—'}</td>
                <td className="whitespace-nowrap text-right">
                  {m.status === 'Scheduled' && <button className="btn-outline btn-sm mr-1" onClick={() => act(() => api.admin.startMaintenance(m.id))}>Start</button>}
                  {(m.status === 'Scheduled' || m.status === 'InProgress') && <button className="btn-dark btn-sm mr-1" onClick={() => setCompleting(m)}>Complete</button>}
                  {m.status === 'Scheduled' && <button className="btn-ghost btn-sm text-rose-700" onClick={() => act(() => api.admin.cancelMaintenance(m.id))}>Cancel</button>}
                </td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ))}
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}

      <CreateModal open={creating} onClose={() => setCreating(false)} onDone={() => { setCreating(false); refresh() }} />
      <CompleteModal record={completing} onClose={() => setCompleting(null)} onDone={() => { setCompleting(null); refresh() }} />
    </>
  )
}

function CreateModal({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const vehicles = useAsync(() => (open ? api.admin.vehicles({ pageSize: 100 }) : Promise.resolve(null)), [open])
  const now = isoToLocalInput(new Date())
  const [v, setV] = useState({ vehicleId: '', type: 'Service', title: '', description: '', start: now, end: '', odometer: '', cost: '', reported: false })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  return (
    <Modal open={open} title="Schedule maintenance" onClose={onClose}>
      <form className="space-y-3" onSubmit={async (e) => {
        e.preventDefault(); setBusy(true); setError(null)
        try {
          await api.admin.createMaintenance({ vehicleId: v.vehicleId, type: v.type, title: v.title.trim(), description: v.description.trim() || null, startAt: localInputToIso(v.start), endAt: v.end ? localInputToIso(v.end) : null, odometerKm: v.odometer ? Number(v.odometer) : null, cost: v.cost ? Number(v.cost) : null, isReportedIssue: v.reported })
          onDone()
        } catch (err) { setError(errorMessage(err)) } finally { setBusy(false) }
      }}>
        <div><label htmlFor="mt-v" className="label">Vehicle</label><select id="mt-v" required className="input" value={v.vehicleId} onChange={(e) => setV({ ...v, vehicleId: e.target.value })}><option value="">Choose…</option>{vehicles.data?.items.map((x) => <option key={x.summary.id} value={x.summary.id}>{x.summary.displayName} · {x.registrationNumber}</option>)}</select></div>
        <div className="grid grid-cols-2 gap-3">
          <div><label htmlFor="mt-type" className="label">Type</label><select id="mt-type" className="input" value={v.type} onChange={(e) => setV({ ...v, type: e.target.value })}>{['Service', 'Repair', 'Inspection', 'Tyres', 'Other'].map((t) => <option key={t}>{t}</option>)}</select></div>
          <div><label htmlFor="mt-title" className="label">Title</label><input id="mt-title" required maxLength={150} className="input" value={v.title} onChange={(e) => setV({ ...v, title: e.target.value })} /></div>
          <div><label htmlFor="mt-start" className="label">Start (SAST)</label><input id="mt-start" type="datetime-local" required className="input" value={v.start} onChange={(e) => setV({ ...v, start: e.target.value })} /></div>
          <div><label htmlFor="mt-end" className="label">End (SAST)</label><input id="mt-end" type="datetime-local" className="input" value={v.end} onChange={(e) => setV({ ...v, end: e.target.value })} /></div>
          <div><label htmlFor="mt-odo" className="label">Odometer (km)</label><input id="mt-odo" type="number" min={0} className="input" value={v.odometer} onChange={(e) => setV({ ...v, odometer: e.target.value })} /></div>
          <div><label htmlFor="mt-cost" className="label">Estimated cost (R)</label><input id="mt-cost" type="number" min={0} step="0.01" className="input" value={v.cost} onChange={(e) => setV({ ...v, cost: e.target.value })} /></div>
        </div>
        <div><label htmlFor="mt-desc" className="label">Notes</label><textarea id="mt-desc" maxLength={2000} className="input" value={v.description} onChange={(e) => setV({ ...v, description: e.target.value })} /></div>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="size-4 accent-amber-600" checked={v.reported} onChange={(e) => setV({ ...v, reported: e.target.checked })} />This is a reported fault</label>
        <p className="text-xs text-ink-500">Scheduling over existing confirmed bookings is refused; move or cancel those bookings first.</p>
        {error && <Alert>{error}</Alert>}
        <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary" disabled={busy}>Schedule</button></div>
      </form>
    </Modal>
  )
}

function CompleteModal({ record, onClose, onDone }: { record: Maintenance | null; onClose: () => void; onDone: () => void }) {
  const [odo, setOdo] = useState('')
  const [cost, setCost] = useState('')
  const [nextKm, setNextKm] = useState('')
  const [nextDate, setNextDate] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Modal open={!!record} title={`Complete: ${record?.title ?? ''}`} onClose={onClose}>
      <form className="space-y-3" onSubmit={async (e) => {
        e.preventDefault(); setError(null)
        try {
          await api.admin.completeMaintenance(record!.id, { odometerKm: odo ? Number(odo) : null, cost: cost ? Number(cost) : null, nextServiceDueKm: nextKm ? Number(nextKm) : null, nextServiceDueDate: nextDate ? `${nextDate}T00:00:00+02:00` : null })
          onDone()
        } catch (err) { setError(errorMessage(err)) }
      }}>
        <p className="text-sm text-ink-500">{record?.vehicleName} · started {formatDate(record?.startAt)}</p>
        <div className="grid grid-cols-2 gap-3">
          <div><label htmlFor="c-odo" className="label">Odometer (km)</label><input id="c-odo" type="number" min={0} className="input" value={odo} onChange={(e) => setOdo(e.target.value)} /></div>
          <div><label htmlFor="c-cost" className="label">Final cost (R)</label><input id="c-cost" type="number" min={0} step="0.01" className="input" value={cost} onChange={(e) => setCost(e.target.value)} /></div>
          <div><label htmlFor="c-nkm" className="label">Next service at (km)</label><input id="c-nkm" type="number" min={0} className="input" value={nextKm} onChange={(e) => setNextKm(e.target.value)} /></div>
          <div><label htmlFor="c-nd" className="label">Next service date</label><input id="c-nd" type="date" className="input" value={nextDate} onChange={(e) => setNextDate(e.target.value)} /></div>
        </div>
        <p className="text-xs text-ink-500">For services, leaving the next-service fields blank schedules the next one from the vehicle’s service interval.</p>
        {error && <Alert>{error}</Alert>}
        <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-dark">Mark complete</button></div>
      </form>
    </Modal>
  )
}
