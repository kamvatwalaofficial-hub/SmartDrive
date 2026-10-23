import { CalendarClock, MapPin, Search } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { api } from '../api/endpoints'
import type { Location } from '../api/types'
import { tripProblem, type Trip } from '../lib/trip'

let cachedLocations: Promise<Location[]> | null = null
export function useLocations() {
  const [locations, setLocations] = useState<Location[]>([])
  useEffect(() => {
    cachedLocations ??= api.catalog.locations().catch((e) => { cachedLocations = null; throw e })
    cachedLocations.then(setLocations).catch(() => setLocations([]))
  }, [])
  return locations
}

/** Pickup branch, return branch and rental window. Used on the home page, browse page and vehicle page. */
export function TripSearch({ initial, onSubmit, submitLabel = 'Search cars', compact = false }: { initial: Trip; onSubmit: (trip: Trip) => void; submitLabel?: string; compact?: boolean }) {
  const locations = useLocations()
  const [trip, setTrip] = useState<Trip>(initial)
  const [sameReturn, setSameReturn] = useState(!initial.to || initial.to === initial.from)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { setTrip(initial) }, [initial.pickup, initial.dropoff, initial.from, initial.to]) // eslint-disable-line react-hooks/exhaustive-deps

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const final = { ...trip, to: sameReturn ? trip.from : trip.to }
    const problem = tripProblem(final)
    setError(problem)
    if (!problem) onSubmit(final)
  }

  const grid = compact ? 'grid gap-3' : 'grid gap-3 sm:grid-cols-2 lg:grid-cols-[1.2fr_1fr_1fr_auto]'
  return (
    <form onSubmit={submit} noValidate aria-label="Rental search" className="space-y-2">
      <div className={grid}>
        <div>
          <label htmlFor="trip-from" className="label"><MapPin className="mr-1 inline size-3.5" aria-hidden />Pickup branch</label>
          <select id="trip-from" className="input" value={trip.from ?? ''} onChange={(e) => setTrip({ ...trip, from: e.target.value ? Number(e.target.value) : undefined })}>
            <option value="">Any branch</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </div>
        <div>
          <label htmlFor="trip-pickup" className="label"><CalendarClock className="mr-1 inline size-3.5" aria-hidden />Pickup (SAST)</label>
          <input id="trip-pickup" type="datetime-local" className="input" value={trip.pickup} step={900}
            onChange={(e) => setTrip({ ...trip, pickup: e.target.value })} />
        </div>
        <div>
          <label htmlFor="trip-return" className="label"><CalendarClock className="mr-1 inline size-3.5" aria-hidden />Return (SAST)</label>
          <input id="trip-return" type="datetime-local" className="input" value={trip.dropoff} step={900}
            onChange={(e) => setTrip({ ...trip, dropoff: e.target.value })} />
        </div>
        <div className="flex items-end">
          <button type="submit" className="btn-primary w-full"><Search className="size-4" aria-hidden />{submitLabel}</button>
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <label className="inline-flex items-center gap-2 text-ink-700">
          <input type="checkbox" className="size-4 accent-amber-600" checked={sameReturn} onChange={(e) => setSameReturn(e.target.checked)} />
          Return to the same branch
        </label>
        {!sameReturn && (
          <select aria-label="Return branch" className="input max-w-xs py-1.5" value={trip.to ?? ''} onChange={(e) => setTrip({ ...trip, to: e.target.value ? Number(e.target.value) : undefined })}>
            <option value="">Choose return branch</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        )}
      </div>
      {error && <p role="alert" className="field-error text-sm">{error}</p>}
    </form>
  )
}
