import { ArrowRight, BadgeCheck, Scale, ShieldCheck, Sparkles } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { api } from '../api/endpoints'
import { TripSearch } from '../components/TripSearch'
import { Alert, Spinner } from '../components/ui'
import { VehicleCard } from '../components/VehicleCard'
import { useAsync } from '../lib/useAsync'
import { readTrip, tripQuery } from '../lib/trip'

export default function HomePage() {
  const navigate = useNavigate()
  const initial = readTrip(new URLSearchParams())
  const featured = useAsync(() => api.catalog.featured(), [])
  const categories = useAsync(() => api.catalog.categories(), [])

  return (
    <>
      <section className="relative overflow-hidden bg-ink-900 text-white">
        <div aria-hidden className="pointer-events-none absolute inset-0 opacity-60"
          style={{ background: 'radial-gradient(60rem 30rem at 85% -10%, rgb(232 163 61 / 0.35), transparent 60%), radial-gradient(40rem 24rem at -10% 110%, rgb(31 122 109 / 0.35), transparent 60%)' }} />
        <div className="container-page relative grid gap-10 py-14 sm:py-20 lg:grid-cols-[1.1fr_1fr] lg:items-center">
          <div>
            <p className="chip bg-white/10 text-amber-400"><Sparkles className="size-3.5" aria-hidden />Explainable recommendations</p>
            <h1 className="mt-4 text-4xl text-white sm:text-5xl lg:text-6xl">The right car for the trip, <span className="text-amber-400">with the reasons shown.</span></h1>
            <p className="mt-5 max-w-xl text-base text-ink-200 sm:text-lg">
              Search by branch and dates, or describe your trip and let SmartDrive rank the fleet against your passengers, luggage and budget — and say why.
            </p>
            <div className="mt-7 flex flex-wrap gap-3">
              <Link to="/recommend" className="btn-primary px-5 py-3 text-base">Find my car <ArrowRight className="size-4" aria-hidden /></Link>
              <Link to="/vehicles" className="btn px-5 py-3 text-base text-white ring-1 ring-white/25 hover:bg-white/10">Browse the fleet</Link>
            </div>
          </div>
          <div className="card p-5 text-ink-900 sm:p-6">
            <h2 className="text-lg">Quick booking search</h2>
            <p className="mb-4 mt-1 text-sm text-ink-500">Prices include 15% VAT. Times are South African Standard Time.</p>
            <TripSearch compact initial={initial} onSubmit={(trip) => navigate(`/vehicles?${tripQuery(trip, { availableOnly: 'true' })}`)} submitLabel="Show available cars" />
          </div>
        </div>
      </section>

      <section className="container-page py-14">
        <div className="mb-6 flex items-end justify-between gap-4">
          <div>
            <h2 className="text-2xl sm:text-3xl">Featured vehicles</h2>
            <p className="mt-1 text-sm text-ink-500">Highly rated, available now, across categories.</p>
          </div>
          <Link to="/vehicles" className="btn-ghost hidden sm:inline-flex">All vehicles <ArrowRight className="size-4" aria-hidden /></Link>
        </div>
        {featured.loading && <Spinner />}
        {featured.error && <Alert onRetry={featured.reload}>{featured.error}</Alert>}
        {featured.data && (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {featured.data.map((v) => <VehicleCard key={v.id} vehicle={v} />)}
          </div>
        )}
      </section>

      {categories.data && categories.data.length > 0 && (
        <section className="bg-sand-100 py-12">
          <div className="container-page">
            <h2 className="text-2xl">Browse by category</h2>
            <div className="mt-5 flex flex-wrap gap-2">
              {categories.data.map((c) => (
                <Link key={c.id} to={`/vehicles?category=${c.slug}`} className="btn-outline rounded-full">
                  {c.name} <span className="text-ink-400">{c.vehicleCount}</span>
                </Link>
              ))}
            </div>
          </div>
        </section>
      )}

      <section className="container-page grid gap-5 py-14 md:grid-cols-3">
        {[
          { icon: Scale, title: 'Prices calculated on the server', body: 'Every quote and booking is priced by the API — per started 24 hours, long-rental discounts and one-way fees itemised. The browser never sets a price.' },
          { icon: ShieldCheck, title: 'No double bookings', body: 'Availability accounts for turnaround time, maintenance and unpaid holds, and concurrent booking attempts for the same car are resolved safely.' },
          { icon: BadgeCheck, title: 'Recommendations you can check', body: 'Each suggestion lists the factors, weights and trade-offs behind its score. Vehicles that do not fit are excluded, not hidden among the results.' },
        ].map(({ icon: Icon, title, body }) => (
          <div key={title} className="card p-5">
            <Icon className="size-6 text-amber-600" aria-hidden />
            <h3 className="mt-3 text-base">{title}</h3>
            <p className="mt-1.5 text-sm text-ink-500">{body}</p>
          </div>
        ))}
      </section>
    </>
  )
}
