import { ArrowRight } from 'lucide-react'
import { Link } from 'react-router'
import { api } from '../../api/endpoints'
import type { BookingSummary } from '../../api/types'
import { Alert, PageHeader, Spinner, Stat, StatusBadge } from '../../components/ui'
import { formatDateTime, money, percent } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'
import { ChartCard, DataTable, SimpleLine } from './charts'

export default function DashboardPage() {
  const d = useAsync(() => api.analytics.dashboard(), [])
  if (d.loading) return <Spinner />
  if (d.error || !d.data) return <Alert onRetry={d.reload}>{d.error}</Alert>
  const x = d.data
  const change = x.revenueLastMonth > 0 ? (x.revenueThisMonth - x.revenueLastMonth) / x.revenueLastMonth : null

  return (
    <>
      <PageHeader title="Operations dashboard" subtitle="Live view of the fleet, bookings and revenue (SAST)." actions={<Link to="/admin/insights" className="btn-dark">Smart Insights <ArrowRight className="size-4" /></Link>} />
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-6">
        <Stat label="Fleet" value={x.totalVehicles} hint={`${x.availableNow} available now`} />
        <Stat label="On rent" value={x.rentedNow} hint={`${x.reservedNow} reserved`} />
        <Stat label="In workshop" value={x.inMaintenance} hint={`${x.maintenanceAlerts} service alerts`} tone={x.maintenanceAlerts ? 'warn' : undefined} />
        <Stat label="Pending payment" value={x.pendingBookings} hint="Held bookings" />
        <Stat label="Revenue this month" value={money(x.revenueThisMonth, true)} hint={change === null ? 'No prior month' : `${change >= 0 ? '+' : ''}${percent(change)} month-to-date vs all of last month`} />
        <Stat label="Utilisation (30 days)" value={percent(x.fleetUtilisationLast30Days)} hint={x.averageRating ? `Rating ${x.averageRating.toFixed(2)} · ${x.reportedReviews} reported` : undefined} />
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-[1.4fr_1fr]">
        <ChartCard title="Bookings per week" subtitle="Bookings created per week (Monday start), last 12 weeks, excluding rejected"
          table={<DataTable headers={['Week', 'Bookings']} rows={x.weeklyBookings.map((p) => [p.label, p.value])} />}>
          <SimpleLine data={x.weeklyBookings} xKey="label" series={[{ key: 'value', name: 'Bookings' }]} />
        </ChartCard>
        <div className="grid gap-6">
          <Upcoming title={`Pickups in the next 7 days (${x.upcomingPickups})`} rows={x.nextPickups} when={(b) => b.pickupAt} />
          <Upcoming title={`Returns due in the next 7 days (${x.upcomingReturns})`} rows={x.nextReturns} when={(b) => b.returnAt} />
        </div>
      </div>
    </>
  )
}

function Upcoming({ title, rows, when }: { title: string; rows: BookingSummary[]; when: (b: BookingSummary) => string }) {
  return (
    <section className="card p-4">
      <h2 className="text-base">{title}</h2>
      {rows.length === 0 ? <p className="mt-2 text-sm text-ink-500">None scheduled.</p> : (
        <ul className="mt-2 divide-y divide-ink-100 text-sm">
          {rows.map((b) => (
            <li key={b.id} className="flex items-center justify-between gap-2 py-2">
              <div className="min-w-0"><p className="truncate font-semibold">{b.vehicleName}</p><p className="text-xs text-ink-500">{formatDateTime(when(b))} · {b.customerName}</p></div>
              <Link to={`/admin/bookings?ref=${b.reference}`} className="shrink-0"><StatusBadge status={b.status} /></Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
