import { CalendarClock, Clock, MapPin } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { errorMessage } from '../api/client'
import { api } from '../api/endpoints'
import { useAuth } from '../auth/AuthContext'
import { PriceBreakdown } from '../components/PriceBreakdown'
import { Alert, PageHeader, Spinner } from '../components/ui'
import { formatDateTime, mediaUrl } from '../lib/format'
import { readTrip, tripIso, tripQuery } from '../lib/trip'
import { useAsync } from '../lib/useAsync'

/** Review the server's quote, then create a booking held for 30 minutes while the (simulated) payment is made. */
export default function CheckoutPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { user } = useAuth()
  const vehicleId = params.get('vehicle') ?? ''
  const trip = readTrip(params)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [agreed, setAgreed] = useState(false)

  const data = useAsync(async () => {
    const vehicle = await api.catalog.vehicle(vehicleId)
    const from = trip.from ?? (await api.catalog.locations())[0]?.id
    const request = { vehicleId, ...tripIso(trip), pickupLocationId: from!, returnLocationId: trip.to ?? from! }
    return { vehicle, request, quote: await api.bookings.quote(request) }
  }, [params.toString()])

  if (!vehicleId) return <div className="container-page py-10"><Alert>No vehicle selected. <Link to="/vehicles" className="underline">Browse the fleet</Link>.</Alert></div>
  if (data.loading) return <Spinner label="Preparing your booking…" />
  if (data.error || !data.data) return <div className="container-page py-10"><Alert onRetry={data.reload}>{data.error}</Alert></div>

  const { vehicle, quote, request } = data.data
  const confirm = async () => {
    setSubmitting(true)
    setError(null)
    try {
      const booking = await api.bookings.create(request)
      navigate(`/bookings/${booking.summary.reference}?new=1`, { replace: true })
    } catch (e) {
      setError(errorMessage(e))
      data.reload()
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="container-page max-w-4xl py-8">
      <PageHeader title="Review and reserve" subtitle={`Booking as ${user?.firstName} ${user?.lastName} (${user?.email})`} />
      <div className="grid gap-6 md:grid-cols-[1.2fr_1fr]">
        <section className="card overflow-hidden">
          {vehicle.summary.imageUrl && <img src={mediaUrl(vehicle.summary.imageUrl)} alt="" className="aspect-[16/8] w-full object-cover" />}
          <div className="space-y-3 p-5 text-sm">
            <h2 className="text-xl">{vehicle.summary.displayName}</h2>
            <p className="flex items-start gap-2"><CalendarClock className="mt-0.5 size-4 text-ink-400" aria-hidden /><span><strong>Pickup</strong> {formatDateTime(quote.pickupAt)}<br /><strong>Return</strong> {formatDateTime(quote.returnAt)}</span></p>
            <p className="flex items-start gap-2"><MapPin className="mt-0.5 size-4 text-ink-400" aria-hidden /><span>{quote.pickupLocation}{quote.returnLocation !== quote.pickupLocation ? <> → {quote.returnLocation} <em className="text-ink-500">(one-way)</em></> : ''}</span></p>
            <Link to={`/vehicles/${vehicleId}?${tripQuery(trip)}`} className="inline-block font-bold text-amber-700 underline">Change dates or branch</Link>
          </div>
        </section>
        <section className="card p-5">
          <h2 className="mb-3 text-lg">Price</h2>
          <PriceBreakdown lines={quote.lines} total={quote.total} vat={quote.vatIncluded} deposit={quote.deposit} rentalDays={quote.rentalDays} />
          {!quote.isAvailable && (
            <div className="mt-4"><Alert tone="warning" title="No longer available">{quote.unavailableReasons.join(' ')}</Alert></div>
          )}
          {error && <div className="mt-4"><Alert>{error}</Alert></div>}
          <label className="mt-4 flex items-start gap-2 text-xs text-ink-700">
            <input type="checkbox" className="mt-0.5 size-4 accent-amber-600" checked={agreed} onChange={(e) => setAgreed(e.target.checked)} />
            I understand free cancellation ends 48 hours before pickup, after which one day&apos;s rental is charged.
          </label>
          <button className="btn-primary mt-4 w-full py-3" disabled={!quote.isAvailable || !agreed || submitting} onClick={confirm}>
            {submitting ? 'Reserving…' : 'Reserve and continue to payment'}
          </button>
          <p className="mt-2 flex items-center justify-center gap-1 text-xs text-ink-500"><Clock className="size-3.5" aria-hidden />The car is held for 30 minutes while you pay.</p>
        </section>
      </div>
    </div>
  )
}
