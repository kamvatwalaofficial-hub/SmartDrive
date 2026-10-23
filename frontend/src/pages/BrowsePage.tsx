import { CarFront, SlidersHorizontal } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { api } from '../api/endpoints'
import { TripSearch } from '../components/TripSearch'
import { Alert, EmptyState, PageHeader, Pagination, Spinner } from '../components/ui'
import { VehicleCard } from '../components/VehicleCard'
import { useAsync } from '../lib/useAsync'
import { readTrip, tripIso, tripQuery } from '../lib/trip'

const sorts = [
  ['recommended', 'Recommended'],
  ['price-asc', 'Price: low to high'],
  ['price-desc', 'Price: high to low'],
  ['rating', 'Highest rated'],
  ['seats', 'Most seats'],
  ['newest', 'Newest models'],
] as const

export default function BrowsePage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const [showFilters, setShowFilters] = useState(false)
  const trip = readTrip(params)
  const hasDates = params.has('pickup') && params.has('return')
  const categories = useAsync(() => api.catalog.categories(), [])

  const filters = {
    q: params.get('q') ?? '',
    category: params.get('category') ?? '',
    transmission: params.get('transmission') ?? '',
    fuel: params.get('fuel') ?? '',
    minSeats: params.get('minSeats') ?? '',
    minLuggage: params.get('minLuggage') ?? '',
    maxDailyRate: params.get('maxDailyRate') ?? '',
    availableOnly: params.get('availableOnly') === 'true',
    sort: params.get('sort') ?? 'recommended',
    page: Number(params.get('page') ?? '1') || 1,
  }

  const key = params.toString()
  const results = useAsync(() => api.catalog.search({
    q: filters.q || undefined,
    category: filters.category || undefined,
    transmission: filters.transmission || undefined,
    fuel: filters.fuel || undefined,
    minSeats: filters.minSeats ? Number(filters.minSeats) : undefined,
    minLuggage: filters.minLuggage ? Number(filters.minLuggage) : undefined,
    maxDailyRate: filters.maxDailyRate ? Number(filters.maxDailyRate) : undefined,
    ...(hasDates ? tripIso(trip) : {}),
    availableOnly: hasDates && filters.availableOnly,
    sort: filters.sort,
    page: filters.page,
    pageSize: 12,
  }), [key])

  const update = (patch: Record<string, string | boolean | number | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) {
      if (v === undefined || v === '' || v === false) next.delete(k)
      else next.set(k, String(v))
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next)
  }

  const activeCount = useMemo(() => ['q', 'category', 'transmission', 'fuel', 'minSeats', 'minLuggage', 'maxDailyRate'].filter((k) => params.get(k)).length, [params])
  const vehicleQuery = hasDates ? tripQuery(trip) : ''

  return (
    <div className="container-page py-8">
      <PageHeader title="Browse the fleet" subtitle={hasDates ? 'Showing availability for your dates.' : 'Add dates to check availability and see trip prices.'} />

      <div className="card mb-6 p-4 sm:p-5">
        <TripSearch initial={trip} onSubmit={(t) => navigate(`/vehicles?${tripQuery(t, { ...Object.fromEntries([...params].filter(([k]) => !['pickup', 'return', 'from', 'to', 'page'].includes(k))) })}`)} submitLabel="Update" />
      </div>

      <div className="grid gap-6 lg:grid-cols-[260px_1fr]">
        <aside>
          <button className="btn-outline mb-3 w-full lg:hidden" aria-expanded={showFilters} onClick={() => setShowFilters((s) => !s)}>
            <SlidersHorizontal className="size-4" aria-hidden />Filters{activeCount ? ` (${activeCount})` : ''}
          </button>
          <div className={`card space-y-4 p-4 ${showFilters ? '' : 'hidden lg:block'}`} role="search" aria-label="Vehicle filters">
            <div>
              <label htmlFor="f-q" className="label">Search</label>
              <input id="f-q" className="input" placeholder="Make or model" defaultValue={filters.q}
                onKeyDown={(e) => { if (e.key === 'Enter') update({ q: e.currentTarget.value.trim() }) }}
                onBlur={(e) => { if (e.currentTarget.value.trim() !== filters.q) update({ q: e.currentTarget.value.trim() }) }} />
            </div>
            <div>
              <label htmlFor="f-cat" className="label">Category</label>
              <select id="f-cat" className="input" value={filters.category} onChange={(e) => update({ category: e.target.value })}>
                <option value="">All categories</option>
                {categories.data?.map((c) => <option key={c.slug} value={c.slug}>{c.name}</option>)}
              </select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="f-tr" className="label">Gearbox</label>
                <select id="f-tr" className="input" value={filters.transmission} onChange={(e) => update({ transmission: e.target.value })}>
                  <option value="">Any</option><option value="Automatic">Automatic</option><option value="Manual">Manual</option>
                </select>
              </div>
              <div>
                <label htmlFor="f-fuel" className="label">Fuel</label>
                <select id="f-fuel" className="input" value={filters.fuel} onChange={(e) => update({ fuel: e.target.value })}>
                  <option value="">Any</option><option>Petrol</option><option>Diesel</option><option>Hybrid</option><option>Electric</option>
                </select>
              </div>
              <div>
                <label htmlFor="f-seats" className="label">Min. seats</label>
                <select id="f-seats" className="input" value={filters.minSeats} onChange={(e) => update({ minSeats: e.target.value })}>
                  <option value="">Any</option>{[2, 4, 5, 7, 8].map((n) => <option key={n} value={n}>{n}+</option>)}
                </select>
              </div>
              <div>
                <label htmlFor="f-bags" className="label">Min. bags</label>
                <select id="f-bags" className="input" value={filters.minLuggage} onChange={(e) => update({ minLuggage: e.target.value })}>
                  <option value="">Any</option>{[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>{n}+</option>)}
                </select>
              </div>
            </div>
            <div>
              <label htmlFor="f-max" className="label">Max. per day (R)</label>
              <select id="f-max" className="input" value={filters.maxDailyRate} onChange={(e) => update({ maxDailyRate: e.target.value })}>
                <option value="">No limit</option>{[400, 600, 800, 1000, 1500, 2000].map((n) => <option key={n} value={n}>R{n}</option>)}
              </select>
            </div>
            <label className={`flex items-center gap-2 text-sm ${hasDates ? 'text-ink-800' : 'text-ink-400'}`}>
              <input type="checkbox" className="size-4 accent-amber-600" disabled={!hasDates} checked={filters.availableOnly} onChange={(e) => update({ availableOnly: e.target.checked })} />
              Only cars available for my dates
            </label>
            {activeCount > 0 && (
              <button className="btn-ghost btn-sm w-full" onClick={() => update({ q: '', category: '', transmission: '', fuel: '', minSeats: '', minLuggage: '', maxDailyRate: '' })}>Clear filters</button>
            )}
          </div>
        </aside>

        <section aria-live="polite">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-ink-500">{results.data ? `${results.data.totalCount} vehicle${results.data.totalCount === 1 ? '' : 's'}` : ' '}</p>
            <label className="flex items-center gap-2 text-sm">
              <span className="text-ink-500">Sort</span>
              <select className="input w-auto py-1.5" value={filters.sort} onChange={(e) => update({ sort: e.target.value })}>
                {sorts.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </select>
            </label>
          </div>
          {results.loading && <Spinner label="Finding vehicles…" />}
          {results.error && <Alert onRetry={results.reload}>{results.error}</Alert>}
          {results.data && !results.loading && (results.data.items.length === 0 ? (
            <EmptyState icon={<CarFront className="size-10" />} title="No vehicles match"
              action={<button className="btn-outline" onClick={() => setParams(new URLSearchParams())}>Reset search</button>}>
              Try fewer filters or different dates. The recommendation wizard can also suggest close alternatives.
            </EmptyState>
          ) : (
            <>
              <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
                {results.data.items.map((v) => <VehicleCard key={v.id} vehicle={v} query={vehicleQuery} />)}
              </div>
              <Pagination page={results.data.page} totalPages={results.data.totalPages} onChange={(page) => update({ page })} />
            </>
          ))}
        </section>
      </div>
    </div>
  )
}
