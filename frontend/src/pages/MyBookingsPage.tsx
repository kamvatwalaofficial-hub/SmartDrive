import { CalendarX } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../api/endpoints'
import { Alert, EmptyState, PageHeader, SampleBadge, Spinner, StatusBadge } from '../components/ui'
import { formatDateTime, mediaUrl, money } from '../lib/format'
import { useAsync } from '../lib/useAsync'

const tabs = [['upcoming', 'Upcoming'], ['past', 'Past'], ['cancelled', 'Cancelled'], ['all', 'All']] as const
type Scope = (typeof tabs)[number][0]

export default function MyBookingsPage() {
  const [scope, setScope] = useState<Scope>('upcoming')
  const bookings = useAsync(() => api.bookings.mine(scope), [scope])

  return (
    <div className="container-page max-w-5xl py-8">
      <PageHeader title="My bookings" actions={<Link to="/vehicles" className="btn-primary">Book a car</Link>} />
      <div role="tablist" aria-label="Booking filter" className="mb-5 flex gap-1 overflow-x-auto rounded-xl bg-ink-100 p-1">
        {tabs.map(([value, label]) => (
          <button key={value} role="tab" aria-selected={scope === value} onClick={() => setScope(value)}
            className={`flex-1 rounded-lg px-3 py-2 text-sm font-bold ${scope === value ? 'bg-white shadow-sm' : 'text-ink-500'}`}>{label}</button>
        ))}
      </div>
      {bookings.loading && <Spinner />}
      {bookings.error && <Alert onRetry={bookings.reload}>{bookings.error}</Alert>}
      {bookings.data && bookings.data.length === 0 && (
        <EmptyState icon={<CalendarX className="size-10" />} title="Nothing here yet" action={<Link to="/recommend" className="btn-dark">Find a car</Link>}>
          {scope === 'upcoming' ? 'You have no upcoming rentals.' : 'No bookings in this list.'}
        </EmptyState>
      )}
      <ul className="space-y-3">
        {bookings.data?.map((b) => (
          <li key={b.id}>
            <Link to={`/bookings/${b.reference}`} className="card flex flex-col gap-4 p-4 transition hover:border-amber-500/50 sm:flex-row sm:items-center">
              {b.vehicleImageUrl && <img src={mediaUrl(b.vehicleImageUrl)} alt="" className="h-20 w-full rounded-lg object-cover sm:w-32" />}
              <div className="min-w-0 flex-1 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-base font-extrabold">{b.vehicleName}</p>
                  <StatusBadge status={b.status} />
                  {b.canPay && <span className="chip bg-amber-400/20 text-amber-700">Payment due</span>}
                  {b.canReview && <span className="chip bg-leaf-50 text-leaf-700">Ready to review</span>}
                  {b.isSampleData && <SampleBadge />}
                </div>
                <p className="mt-1 text-ink-500">{formatDateTime(b.pickupAt)} → {formatDateTime(b.returnAt)}</p>
                <p className="text-ink-500">{b.pickupLocation}{b.returnLocation !== b.pickupLocation ? ` → ${b.returnLocation}` : ''}</p>
              </div>
              <div className="text-right">
                <p className="text-lg font-extrabold">{money(b.totalAmount)}</p>
                <p className="font-mono text-xs text-ink-500">{b.reference}</p>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}
