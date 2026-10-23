import { ArrowLeft, Briefcase, CheckCircle2, DoorOpen, Flag, Fuel, Gauge, Leaf, Route, Sofa, Users } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { errorMessage } from '../api/client'
import { api } from '../api/endpoints'
import type { Quote, Review, VehicleDetail } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { Gallery } from '../components/Gallery'
import { PriceBreakdown } from '../components/PriceBreakdown'
import { TripSearch, useLocations } from '../components/TripSearch'
import { Alert, EmptyState, Modal, Pagination, SampleBadge, Spinner, Stars, StatusBadge } from '../components/ui'
import { formatDate, formatDateTime, humanise, money } from '../lib/format'
import { readTrip, tripIso, tripQuery, type Trip } from '../lib/trip'
import { useAsync } from '../lib/useAsync'

export default function VehiclePage() {
  const { id = '' } = useParams()
  const vehicle = useAsync(() => api.catalog.vehicle(id), [id])

  if (vehicle.loading) return <Spinner label="Loading vehicle…" />
  if (vehicle.error || !vehicle.data) {
    return <div className="container-page py-10"><Alert onRetry={vehicle.reload}>{vehicle.error ?? 'Vehicle not found.'}</Alert></div>
  }
  return <VehicleView v={vehicle.data} />
}

function VehicleView({ v }: { v: VehicleDetail }) {
  const s = v.summary
  const specs = [
    { icon: Users, label: 'Seats', value: s.seats },
    { icon: DoorOpen, label: 'Doors', value: s.doors },
    { icon: Briefcase, label: 'Luggage', value: `${s.luggageCapacity} bags` },
    { icon: Gauge, label: 'Gearbox', value: humanise(s.transmission) },
    { icon: Fuel, label: 'Fuel', value: humanise(s.fuelType) },
    { icon: Leaf, label: 'Consumption', value: v.fuelEconomyLitresPer100Km ? `${v.fuelEconomyLitresPer100Km} L/100 km` : s.fuelType === 'Electric' ? 'Electric' : '—' },
    { icon: Sofa, label: 'Comfort', value: `${v.comfortRating}/5` },
    { icon: Route, label: 'Mileage', value: v.dailyMileageLimitKm ? `${v.dailyMileageLimitKm} km/day` : 'Unlimited' },
  ]

  return (
    <div className="container-page py-6">
      <Link to="/vehicles" className="btn-ghost -ml-3 mb-3"><ArrowLeft className="size-4" aria-hidden />Back to fleet</Link>
      <div className="grid gap-8 lg:grid-cols-[1.5fr_1fr]">
        <div className="min-w-0">
          <Gallery media={v.media} name={s.displayName} modelUrl={v.modelAssetUrl} modelAttribution={v.modelAttribution} />
          {v.media.some((m) => m.isSampleData) && (
            <p className="mt-2 text-xs text-ink-500">Images are original illustrations generated for this demo, not photographs of the vehicle.</p>
          )}

          <div className="mt-8">
            <div className="flex flex-wrap items-center gap-2">
              <span className="chip bg-amber-400/20 text-amber-700">{s.category}</span>
              <StatusBadge status={s.currentState} />
              {s.isSampleData && <SampleBadge />}
            </div>
            <h1 className="mt-3 text-3xl sm:text-4xl">{s.displayName}</h1>
            <div className="mt-2 flex items-center gap-2 text-sm text-ink-500">
              <Stars value={v.ratings.average} />
              {v.ratings.count ? <span>{v.ratings.average?.toFixed(1)} from {v.ratings.count} verified rentals</span> : <span>No reviews yet</span>}
            </div>
            <p className="mt-4 max-w-2xl text-ink-700">{v.description}</p>
          </div>

          <dl className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
            {specs.map(({ icon: Icon, label, value }) => (
              <div key={label} className="rounded-xl border border-ink-100 bg-white p-3">
                <dt className="flex items-center gap-1.5 text-xs font-semibold text-ink-500"><Icon className="size-3.5" aria-hidden />{label}</dt>
                <dd className="mt-1 font-bold">{value}</dd>
              </div>
            ))}
          </dl>

          {v.features.length > 0 && (
            <section className="mt-8">
              <h2 className="text-xl">Features</h2>
              <ul className="mt-3 grid gap-2 sm:grid-cols-2">
                {v.features.map((f) => <li key={f} className="flex items-center gap-2 text-sm"><CheckCircle2 className="size-4 text-leaf-600" aria-hidden />{f}</li>)}
              </ul>
            </section>
          )}

          <section className="mt-8 rounded-2xl bg-sand-100 p-5 text-sm text-ink-700">
            <h2 className="text-base">Rental terms</h2>
            <ul className="mt-2 list-disc space-y-1 pl-5">
              <li>Charged per started 24 hours, with a 59-minute grace period on return.</li>
              <li>10% off rentals of 7 days or more; 15% off from 21 days.</li>
              <li>{v.dailyMileageLimitKm ? `${v.dailyMileageLimitKm} km per day included; extra kilometres at ${money(v.excessMileageRatePerKm)} per km.` : 'Unlimited kilometres.'}</li>
              <li>Refundable deposit of {money(v.depositAmount)} held at pickup.</li>
              <li>Free cancellation up to 48 hours before pickup; later cancellations cost one day&apos;s rental.</li>
            </ul>
          </section>

          <ReviewsSection vehicleId={s.id} ratings={v.ratings} />
        </div>

        <aside className="lg:sticky lg:top-24 lg:self-start">
          <BookingPanel v={v} />
        </aside>
      </div>
    </div>
  )
}

function BookingPanel({ v }: { v: VehicleDetail }) {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const locations = useLocations()
  const trip = readTrip(params)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const from = trip.from ?? locations[0]?.id
  const to = trip.to ?? from

  useEffect(() => {
    if (!from) return
    let stale = false
    setLoading(true)
    setError(null)
    api.bookings.quote({ vehicleId: v.summary.id, ...tripIso(trip), pickupLocationId: from, returnLocationId: to! })
      .then((q) => { if (!stale) setQuote(q) })
      .catch((e) => { if (!stale) { setQuote(null); setError(errorMessage(e)) } })
      .finally(() => { if (!stale) setLoading(false) })
    return () => { stale = true }
  }, [v.summary.id, trip.pickup, trip.dropoff, from, to]) // eslint-disable-line react-hooks/exhaustive-deps

  const apply = (t: Trip) => setParams(new URLSearchParams(tripQuery(t)), { replace: true })

  return (
    <div className="card p-5">
      <p className="text-2xl font-extrabold">{money(v.summary.dailyRate, true)} <span className="text-sm font-semibold text-ink-500">/ day</span></p>
      <p className="text-xs text-ink-500">VAT included</p>
      <div className="mt-4">
        <TripSearch compact initial={{ ...trip, from, to }} onSubmit={apply} submitLabel="Update price" />
      </div>
      <div className="mt-5 border-t border-ink-100 pt-4" aria-live="polite">
        {loading && <Spinner label="Calculating price…" className="py-4" />}
        {error && <Alert>{error}</Alert>}
        {quote && !loading && (
          <>
            {!quote.isAvailable && (
              <Alert tone="warning" title="Not available for these dates">
                <ul className="list-disc pl-4">{quote.unavailableReasons.map((r) => <li key={r}>{r}</li>)}</ul>
              </Alert>
            )}
            <div className="mt-3">
              <p className="mb-2 text-xs text-ink-500">{formatDateTime(quote.pickupAt)} → {formatDateTime(quote.returnAt)}<br />{quote.pickupLocation}{quote.returnLocation !== quote.pickupLocation ? ` → ${quote.returnLocation}` : ''}</p>
              <PriceBreakdown lines={quote.lines} total={quote.total} vat={quote.vatIncluded} deposit={quote.deposit} rentalDays={quote.rentalDays} />
            </div>
            <button className="btn-primary mt-4 w-full py-3" disabled={!quote.isAvailable}
              onClick={() => navigate(`/checkout?${tripQuery({ ...trip, from, to }, { vehicle: v.summary.id })}`)}>
              Reserve this car
            </button>
            <p className="mt-2 text-center text-xs text-ink-500">You won&apos;t pay anything yet. Payments in this demo are simulated.</p>
          </>
        )}
      </div>
    </div>
  )
}

function ReviewsSection({ vehicleId, ratings }: { vehicleId: string; ratings: VehicleDetail['ratings'] }) {
  const { user } = useAuth()
  const [sort, setSort] = useState('newest')
  const [rating, setRating] = useState<number | undefined>()
  const [page, setPage] = useState(1)
  const [reporting, setReporting] = useState<Review | null>(null)
  const reviews = useAsync(() => api.catalog.reviews(vehicleId, { sort, rating, page, pageSize: 5 }), [vehicleId, sort, rating, page])

  return (
    <section className="mt-10" aria-labelledby="reviews-heading">
      <h2 id="reviews-heading" className="text-xl">Reviews</h2>
      <p className="mt-1 text-sm text-ink-500">Only customers who completed a rental of this vehicle can review it.</p>

      {ratings.count > 0 && (
        <div className="mt-4 grid gap-4 rounded-2xl border border-ink-100 bg-white p-4 sm:grid-cols-[auto_1fr_1fr]">
          <div className="text-center sm:pr-4">
            <p className="text-4xl font-extrabold">{ratings.average?.toFixed(1)}</p>
            <Stars value={ratings.average} />
            <p className="mt-1 text-xs text-ink-500">{ratings.count} reviews</p>
          </div>
          <div className="space-y-1">
            {[5, 4, 3, 2, 1].map((n) => {
              const count = ratings.distribution[String(n)] ?? 0
              return (
                <button key={n} onClick={() => { setRating(rating === n ? undefined : n); setPage(1) }} aria-pressed={rating === n}
                  className={`flex w-full items-center gap-2 rounded px-1 text-xs ${rating === n ? 'bg-amber-400/20' : ''}`}>
                  <span className="w-3">{n}</span>
                  <span className="h-2 flex-1 overflow-hidden rounded-full bg-ink-100"><span className="block h-full bg-amber-500" style={{ width: `${(count / ratings.count) * 100}%` }} /></span>
                  <span className="w-6 text-right text-ink-500">{count}</span>
                </button>
              )
            })}
          </div>
          <dl className="grid grid-cols-2 gap-2 text-xs">
            {(['condition', 'cleanliness', 'comfort', 'value'] as const).map((k) => (
              <div key={k}><dt className="text-ink-500">{humanise(k)}</dt><dd className="font-bold">{ratings[k]?.toFixed(1) ?? '—'} / 5</dd></div>
            ))}
          </dl>
        </div>
      )}

      <div className="mt-4 flex flex-wrap items-center gap-2 text-sm">
        <label htmlFor="review-sort" className="text-ink-500">Sort</label>
        <select id="review-sort" className="input w-auto py-1.5" value={sort} onChange={(e) => { setSort(e.target.value); setPage(1) }}>
          <option value="newest">Newest</option><option value="oldest">Oldest</option><option value="highest">Highest rated</option><option value="lowest">Lowest rated</option>
        </select>
        {rating && <button className="chip bg-amber-400/20 text-amber-700" onClick={() => setRating(undefined)}>{rating} stars ✕</button>}
      </div>

      <div className="mt-4 space-y-3">
        {reviews.loading && <Spinner />}
        {reviews.error && <Alert onRetry={reviews.reload}>{reviews.error}</Alert>}
        {reviews.data?.items.length === 0 && <EmptyState title="No reviews yet">Reviews appear here after completed rentals.</EmptyState>}
        {reviews.data?.items.map((r) => (
          <article key={r.id} className="rounded-2xl border border-ink-100 bg-white p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2"><Stars value={r.overall} /><h3 className="text-sm">{r.title}</h3></div>
              <p className="text-xs text-ink-500">{r.authorName} · {formatDate(r.createdAt)}{r.updatedAt ? ' · edited' : ''}</p>
            </div>
            <p className="mt-2 text-sm text-ink-700">{r.comment}</p>
            <div className="mt-2 flex items-center justify-between">
              {r.isSampleData ? <SampleBadge /> : <span />}
              {user && !r.isMine && (
                <button className="btn-ghost btn-sm text-ink-500" onClick={() => setReporting(r)}><Flag className="size-3.5" aria-hidden />Report</button>
              )}
            </div>
          </article>
        ))}
        {reviews.data && <Pagination page={reviews.data.page} totalPages={reviews.data.totalPages} onChange={setPage} />}
      </div>
      <ReportModal review={reporting} onClose={() => setReporting(null)} />
    </section>
  )
}

function ReportModal({ review, onClose }: { review: Review | null; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const [state, setState] = useState<'idle' | 'sending' | 'done'>('idle')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { setReason(''); setState('idle'); setError(null) }, [review])

  return (
    <Modal open={!!review} title="Report this review" onClose={onClose}>
      {state === 'done' ? (
        <Alert tone="success">Thanks — an administrator will look at it.</Alert>
      ) : (
        <form onSubmit={async (e) => {
          e.preventDefault()
          if (!review) return
          setState('sending'); setError(null)
          try { await api.reviews.report(review.id, reason.trim()); setState('done') } catch (err) { setError(errorMessage(err)); setState('idle') }
        }}>
          <label htmlFor="report-reason" className="label">What is wrong with it?</label>
          <textarea id="report-reason" className="input min-h-24" value={reason} onChange={(e) => setReason(e.target.value)} minLength={5} maxLength={500} required />
          {error && <p className="field-error">{error}</p>}
          <div className="mt-4 flex justify-end gap-2">
            <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
            <button className="btn-dark" disabled={state === 'sending' || reason.trim().length < 5}>Send report</button>
          </div>
        </form>
      )}
    </Modal>
  )
}

