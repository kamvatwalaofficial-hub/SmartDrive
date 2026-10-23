import { defaultWindow, localInputToIso } from './format'

/** A rental window carried between pages in the query string, in SAST wall-clock form (YYYY-MM-DDTHH:mm). */
export interface Trip {
  pickup: string
  dropoff: string
  from?: number
  to?: number
}

export function readTrip(params: URLSearchParams): Trip {
  const fallback = defaultWindow()
  const valid = (v: string | null) => (v && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(v) ? v : null)
  const num = (v: string | null) => (v && /^\d+$/.test(v) ? Number(v) : undefined)
  return {
    pickup: valid(params.get('pickup')) ?? fallback.pickup,
    dropoff: valid(params.get('return')) ?? fallback.dropoff,
    from: num(params.get('from')),
    to: num(params.get('to')),
  }
}

export function tripQuery(trip: Trip, extra: Record<string, string | number | undefined> = {}): string {
  const p = new URLSearchParams()
  p.set('pickup', trip.pickup)
  p.set('return', trip.dropoff)
  if (trip.from) p.set('from', String(trip.from))
  if (trip.to) p.set('to', String(trip.to))
  for (const [k, v] of Object.entries(extra)) if (v !== undefined && v !== '') p.set(k, String(v))
  return p.toString()
}

export function tripIso(trip: Trip) {
  return { pickupAt: localInputToIso(trip.pickup), returnAt: localInputToIso(trip.dropoff) }
}

/** Client-side sanity check; the server re-validates everything. */
export function tripProblem(trip: Trip): string | null {
  const { pickupAt, returnAt } = tripIso(trip)
  const start = new Date(pickupAt).getTime()
  const end = new Date(returnAt).getTime()
  if (Number.isNaN(start) || Number.isNaN(end)) return 'Choose a pickup and return date and time.'
  if (end <= start) return 'The return must be after the pickup.'
  if (start < Date.now() - 5 * 60_000) return 'Pickup must be in the future.'
  return null
}
