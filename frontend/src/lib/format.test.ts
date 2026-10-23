import { describe, expect, it } from 'vitest'
import { formatDateTime, humanise, isoToLocalInput, localInputToIso, money, percent } from './format'

describe('South African formatting', () => {
  it('formats Rand amounts', () => {
    const text = money(1234.5)
    expect(text).toMatch(/^R/)
    expect(text.replace(/\s/g, '')).toBe('R1234,50')
    expect(money(null)).toBe('—')
  })

  it('treats datetime-local input as SAST (UTC+2, no daylight saving)', () => {
    expect(localInputToIso('2026-12-24T09:00')).toBe('2026-12-24T09:00:00+02:00')
    expect(isoToLocalInput('2026-12-24T07:00:00Z')).toBe('2026-12-24T09:00')
    // Round trip in mid-winter and mid-summer gives the same wall-clock time.
    for (const v of ['2026-06-21T14:30', '2026-01-15T08:15']) expect(isoToLocalInput(localInputToIso(v))).toBe(v)
  })

  it('shows instants in SAST regardless of the browser time zone', () => {
    expect(formatDateTime('2026-03-01T22:30:00Z')).toContain('00:30')
  })

  it('humanises enum names and percentages', () => {
    expect(humanise('UnderMaintenance')).toBe('Under maintenance')
    expect(percent(0.256, 1)).toBe('25.6%')
  })
})
