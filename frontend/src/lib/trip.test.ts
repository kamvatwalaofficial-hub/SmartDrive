import { describe, expect, it } from 'vitest'
import { readTrip, tripProblem, tripQuery } from './trip'

describe('trip query string', () => {
  it('round-trips through the URL', () => {
    const trip = { pickup: '2030-05-01T09:00', dropoff: '2030-05-04T09:00', from: 2, to: 3 }
    expect(readTrip(new URLSearchParams(tripQuery(trip)))).toEqual(trip)
  })

  it('ignores malformed values and falls back to a default window', () => {
    const t = readTrip(new URLSearchParams('pickup=<script>&from=abc'))
    expect(t.pickup).toMatch(/^\d{4}-\d{2}-\d{2}T09:00$/)
    expect(t.from).toBeUndefined()
  })

  it('rejects a return before pickup and a pickup in the past', () => {
    expect(tripProblem({ pickup: '2030-05-04T09:00', dropoff: '2030-05-01T09:00' })).toMatch(/after the pickup/)
    expect(tripProblem({ pickup: '2001-01-01T09:00', dropoff: '2001-01-02T09:00' })).toMatch(/future/)
    expect(tripProblem({ pickup: '2030-05-01T09:00', dropoff: '2030-05-02T09:00' })).toBeNull()
  })
})
