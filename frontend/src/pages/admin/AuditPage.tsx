import { useState } from 'react'
import { api } from '../../api/endpoints'
import { Alert, PageHeader, Pagination, Spinner } from '../../components/ui'
import { formatDateTime } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

export default function AuditPage() {
  const [action, setAction] = useState('')
  const [entity, setEntity] = useState('')
  const [page, setPage] = useState(1)
  const list = useAsync(() => api.admin.auditLog({ action: action || undefined, entityType: entity || undefined, page, pageSize: 50 }), [action, entity, page])
  return (
    <>
      <PageHeader title="Audit log" subtitle="Who changed what, and when. Entries cannot be edited." />
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div><label htmlFor="au-a" className="label">Action starts with</label><input id="au-a" className="input" placeholder="e.g. booking." onBlur={(e) => { setAction(e.target.value.trim()); setPage(1) }} onKeyDown={(e) => { if (e.key === 'Enter') { setAction(e.currentTarget.value.trim()); setPage(1) } }} /></div>
        <div><label htmlFor="au-e" className="label">Entity</label><select id="au-e" className="input" value={entity} onChange={(e) => { setEntity(e.target.value); setPage(1) }}>{['', 'Booking', 'Vehicle', 'MaintenanceRecord', 'Review', 'User', 'VehicleCategory', 'Location', 'ContactMessage'].map((x) => <option key={x} value={x}>{x || 'All'}</option>)}</select></div>
      </div>
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data && (
        <div className="card overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>When (SAST)</th><th>User</th><th>Action</th><th>Entity</th><th>Details</th></tr></thead>
            <tbody>{list.data.items.map((a) => (
              <tr key={a.id}><td className="whitespace-nowrap text-xs">{formatDateTime(a.createdAt)}</td><td className="text-xs">{a.userEmail ?? 'system'}</td><td className="font-mono text-xs">{a.action}</td><td className="text-xs">{a.entityType} <span className="font-mono text-ink-500">{a.entityId}</span></td><td className="max-w-md truncate font-mono text-[11px] text-ink-500" title={a.details ?? ''}>{a.details}</td></tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
    </>
  )
}
