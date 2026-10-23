import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import { useAuth } from '../../auth/AuthContext'
import { Alert, EmptyState, PageHeader, Pagination, SampleBadge, Spinner } from '../../components/ui'
import { formatDate, money } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

export default function CustomersPage() {
  const { user } = useAuth()
  const [q, setQ] = useState('')
  const [role, setRole] = useState('')
  const [active, setActive] = useState('')
  const [page, setPage] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const list = useAsync(() => api.admin.customers({ q: q || undefined, role: role || undefined, isActive: active === '' ? undefined : active === 'true', page, pageSize: 25 }), [q, role, active, page])
  const act = async (fn: () => Promise<unknown>) => { setError(null); try { await fn(); list.reload() } catch (e) { setError(errorMessage(e)) } }

  return (
    <>
      <PageHeader title="Customers" subtitle="Accounts, roles and booking history. Deactivating an account signs it out immediately." />
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-48 flex-1"><label htmlFor="cu-q" className="label">Search</label><input id="cu-q" className="input" placeholder="Name, email or phone" onKeyDown={(e) => { if (e.key === 'Enter') { setQ(e.currentTarget.value.trim()); setPage(1) } }} onBlur={(e) => { setQ(e.currentTarget.value.trim()); setPage(1) }} /></div>
        <div><label htmlFor="cu-role" className="label">Role</label><select id="cu-role" className="input" value={role} onChange={(e) => { setRole(e.target.value); setPage(1) }}><option value="">All</option><option>Customer</option><option>Administrator</option></select></div>
        <div><label htmlFor="cu-act" className="label">Status</label><select id="cu-act" className="input" value={active} onChange={(e) => { setActive(e.target.value); setPage(1) }}><option value="">All</option><option value="true">Active</option><option value="false">Deactivated</option></select></div>
      </div>
      {error && <div className="mb-3"><Alert>{error}</Alert></div>}
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data && (list.data.items.length === 0 ? <EmptyState title="No accounts match" /> : (
        <div className="card overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>Name</th><th>Roles</th><th className="text-right">Bookings</th><th className="text-right">Spend</th><th>Joined</th><th>Last sign-in</th><th /></tr></thead>
            <tbody>{list.data.items.map((c) => {
              const isAdmin = c.roles.includes('Administrator')
              const self = c.id === user?.id
              return (
                <tr key={c.id} className={c.isActive ? '' : 'opacity-60'}>
                  <td><span className="font-semibold">{c.fullName}</span>{!c.isActive && <span className="chip ml-2 bg-rose-50 text-rose-700">Deactivated</span>}{c.isSampleData && <SampleBadge className="ml-2" />}<br /><span className="text-xs text-ink-500">{c.email}{c.phoneNumber ? ` · ${c.phoneNumber}` : ''}</span></td>
                  <td className="text-xs">{c.roles.join(', ')}</td>
                  <td className="text-right">{c.bookingCount}</td>
                  <td className="text-right">{money(c.totalSpend, true)}</td>
                  <td className="text-xs">{formatDate(c.createdAt)}</td>
                  <td className="text-xs">{c.lastLoginAt ? formatDate(c.lastLoginAt) : '—'}</td>
                  <td className="whitespace-nowrap text-right">
                    {!self && <button className="btn-outline btn-sm mr-1" onClick={() => act(() => api.admin.setCustomerActive(c.id, !c.isActive))}>{c.isActive ? 'Deactivate' : 'Reactivate'}</button>}
                    {!self && <button className="btn-ghost btn-sm" onClick={() => { if (window.confirm(isAdmin ? `Remove administrator access from ${c.fullName}?` : `Give ${c.fullName} administrator access?`)) act(() => api.admin.setCustomerRole(c.id, 'Administrator', !isAdmin)) }}>{isAdmin ? 'Revoke admin' : 'Make admin'}</button>}
                    {self && <span className="text-xs text-ink-400">You</span>}
                  </td>
                </tr>
              )
            })}</tbody>
          </table>
        </div>
      ))}
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
    </>
  )
}
