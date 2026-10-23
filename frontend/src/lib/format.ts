// South African formatting: Rand amounts and South African Standard Time (UTC+2, no daylight saving).

export const TIME_ZONE = 'Africa/Johannesburg'

const zar = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const zarWhole = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR', maximumFractionDigits: 0 })

/** R 1 234,50 — the en-ZA locale uses a space for thousands and a comma for decimals. */
export function money(amount: number | null | undefined, whole = false): string {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return '—'
  return (whole ? zarWhole : zar).format(amount)
}

export function percent(fraction: number | null | undefined, digits = 0): string {
  if (fraction === null || fraction === undefined || Number.isNaN(fraction)) return '—'
  return `${(fraction * 100).toFixed(digits)}%`
}

export function number(value: number | null | undefined, digits = 0): string {
  if (value === null || value === undefined) return '—'
  return new Intl.NumberFormat('en-ZA', { maximumFractionDigits: digits }).format(value)
}

const dateTime = new Intl.DateTimeFormat('en-ZA', { timeZone: TIME_ZONE, weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false })
const dateOnly = new Intl.DateTimeFormat('en-ZA', { timeZone: TIME_ZONE, day: 'numeric', month: 'short', year: 'numeric' })
const timeOnly = new Intl.DateTimeFormat('en-ZA', { timeZone: TIME_ZONE, hour: '2-digit', minute: '2-digit', hour12: false })

export function formatDateTime(iso: string | null | undefined): string {
  return iso ? dateTime.format(new Date(iso)) : '—'
}

export function formatDate(iso: string | null | undefined): string {
  return iso ? dateOnly.format(new Date(iso)) : '—'
}

export function formatTime(iso: string | null | undefined): string {
  return iso ? timeOnly.format(new Date(iso)) : '—'
}

/**
 * Converts a <input type="datetime-local"> value, which the user enters in South African time,
 * into an ISO string with the +02:00 offset. South Africa has no daylight saving, so the offset is fixed.
 */
export function localInputToIso(value: string): string {
  if (!value) return ''
  const withSeconds = value.length === 16 ? `${value}:00` : value
  return `${withSeconds}+02:00`
}

/** The reverse: an instant to the "YYYY-MM-DDTHH:mm" SAST wall-clock string a datetime-local input expects. */
export function isoToLocalInput(iso: string | Date): string {
  const date = typeof iso === 'string' ? new Date(iso) : iso
  const sast = new Date(date.getTime() + 2 * 60 * 60 * 1000)
  return sast.toISOString().slice(0, 16)
}

/** Default rental window: tomorrow 09:00 SAST for three days. */
export function defaultWindow(daysAhead = 1, length = 3): { pickup: string; dropoff: string } {
  const now = new Date()
  const sastToday = new Date(now.getTime() + 2 * 60 * 60 * 1000).toISOString().slice(0, 10)
  const base = new Date(`${sastToday}T09:00:00+02:00`)
  const pickup = new Date(base.getTime() + daysAhead * 86_400_000)
  const dropoff = new Date(pickup.getTime() + length * 86_400_000)
  return { pickup: isoToLocalInput(pickup), dropoff: isoToLocalInput(dropoff) }
}

export function plural(count: number, one: string, many = `${one}s`) {
  return `${count} ${count === 1 ? one : many}`
}

/** Splits "UnderMaintenance" into "Under maintenance". */
export function humanise(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}

/** Media URLs from the API are relative (/media/...). Prefix the API origin when the API runs elsewhere. */
export function mediaUrl(url: string | null | undefined): string | undefined {
  if (!url) return undefined
  if (/^https?:\/\//.test(url)) return url
  return `${import.meta.env.VITE_API_BASE_URL ?? ''}${url}`
}

export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return `pay-${crypto.randomUUID()}`
  return `pay-${Date.now()}-${Math.random().toString(36).slice(2)}`
}
