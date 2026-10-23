import { zodResolver } from '@hookform/resolvers/zod'
import { CheckCircle2, Clock, CreditCard, FlaskConical } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link, useParams, useSearchParams } from 'react-router'
import { z } from 'zod'
import { ApiError, errorMessage } from '../api/client'
import { api } from '../api/endpoints'
import type { BookingDetail } from '../api/types'
import { PriceBreakdown } from '../components/PriceBreakdown'
import { Alert, Modal, SampleBadge, Spinner, StarInput, StatusBadge } from '../components/ui'
import { formatDateTime, humanise, mediaUrl, money, newIdempotencyKey } from '../lib/format'
import { applyServerErrors } from '../lib/forms'
import { useAsync } from '../lib/useAsync'

export default function BookingPage() {
  const { reference = '' } = useParams()
  const [params] = useSearchParams()
  const booking = useAsync(() => api.bookings.detail(reference), [reference])

  if (booking.loading && !booking.data) return <Spinner label="Loading booking…" />
  if (booking.error || !booking.data) return <div className="container-page py-10"><Alert onRetry={booking.reload}>{booking.error ?? 'Booking not found.'}</Alert></div>

  const b = booking.data
  const s = b.summary
  return (
    <div className="container-page max-w-5xl py-8">
      <Link to="/account/bookings" className="btn-ghost -ml-3 mb-3">← My bookings</Link>
      {params.get('new') && s.status === 'Pending' && <div className="mb-4"><Alert tone="success" title="Car reserved">Complete the payment below to confirm your booking.</Alert></div>}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl sm:text-3xl">Booking {s.reference}</h1>
        <StatusBadge status={s.status} />
        <StatusBadge status={s.paymentStatus} />
        {s.isSampleData && <SampleBadge />}
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-[1.4fr_1fr]">
        <div className="space-y-6">
          <section className="card flex flex-col gap-4 p-5 sm:flex-row">
            {s.vehicleImageUrl && <img src={mediaUrl(s.vehicleImageUrl)} alt="" className="h-28 w-full rounded-xl object-cover sm:w-44" />}
            <div className="text-sm">
              <Link to={`/vehicles/${s.vehicleId}`} className="text-lg font-extrabold hover:text-amber-700">{s.vehicleName}</Link>
              <p className="mt-2"><strong>Pickup:</strong> {formatDateTime(s.pickupAt)} · {s.pickupLocation}</p>
              <p><strong>Return:</strong> {formatDateTime(s.returnAt)} · {s.returnLocation}</p>
              {b.pickupOdometerKm != null && <p className="mt-1 text-ink-500">Odometer: {b.pickupOdometerKm} km{b.returnOdometerKm != null ? ` → ${b.returnOdometerKm} km` : ''}</p>}
            </div>
          </section>

          {s.canPay && <PaymentPanel booking={b} onPaid={booking.setData} />}
          {s.canReview && <ReviewPanel reference={s.reference} onDone={booking.reload} />}
          {s.hasReview && <Alert tone="success">Thanks for reviewing this rental.</Alert>}

          <section className="card p-5">
            <h2 className="text-lg">Timeline</h2>
            <ol className="mt-3 space-y-3 border-l-2 border-ink-100 pl-4 text-sm">
              {b.statusHistory.map((h, i) => (
                <li key={i} className="relative">
                  <span className="absolute -left-[1.4rem] top-1 size-3 rounded-full border-2 border-white bg-amber-500" aria-hidden />
                  <p className="font-semibold">{h.from ? `${humanise(h.from)} → ` : ''}{humanise(h.to)}</p>
                  <p className="text-xs text-ink-500">{formatDateTime(h.at)}{h.reason ? ` · ${h.reason}` : ''}</p>
                </li>
              ))}
            </ol>
          </section>
        </div>

        <aside className="space-y-6">
          <section className="card p-5">
            <h2 className="mb-3 text-lg">Charges</h2>
            <PriceBreakdown lines={b.lines} total={s.totalAmount} vat={b.vatAmount} deposit={b.depositAmount} rentalDays={s.rentalDays} />
            {b.cancellationFee > 0 && <p className="mt-2 text-sm text-rose-700">Cancellation fee: {money(b.cancellationFee)}</p>}
          </section>
          {b.payments.length > 0 && (
            <section className="card p-5">
              <h2 className="text-lg">Payments</h2>
              <ul className="mt-3 space-y-2 text-sm">
                {b.payments.map((p) => (
                  <li key={p.id} className="flex items-center justify-between gap-2">
                    <span>{humanise(p.kind)} <span className="text-xs text-ink-500">· {formatDateTime(p.createdAt)}</span></span>
                    <span className="text-right font-semibold">{money(p.amount)}<br /><span className="text-xs font-normal text-ink-500">{humanise(p.status)}{p.isSimulated ? ' · simulated' : ''}</span></span>
                  </li>
                ))}
              </ul>
            </section>
          )}
          {s.canCancel && <CancelPanel booking={b} onCancelled={booking.setData} />}
        </aside>
      </div>
    </div>
  )
}

function useCountdown(until: string | null) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!until) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [until])
  if (!until) return null
  const ms = Math.max(0, new Date(until).getTime() - now)
  return { ms, label: `${Math.floor(ms / 60000)}:${String(Math.floor((ms % 60000) / 1000)).padStart(2, '0')}` }
}

function PaymentPanel({ booking, onPaid }: { booking: BookingDetail; onPaid: (b: BookingDetail) => void }) {
  // One idempotency key per payment attempt, so the server never records the same payment twice.
  const key = useRef(newIdempotencyKey())
  const [state, setState] = useState<'idle' | 'paying'>('idle')
  const [error, setError] = useState<string | null>(null)
  const countdown = useCountdown(booking.summary.holdExpiresAt)
  const expired = countdown !== null && countdown.ms === 0

  const pay = async (simulateFailure: boolean) => {
    setState('paying'); setError(null)
    try {
      onPaid(await api.bookings.pay(booking.summary.reference, key.current, simulateFailure))
    } catch (e) {
      setError(errorMessage(e))
      // A definite answer from the server (such as a decline) ends this attempt. If the request may not
      // have arrived (network failure), the same key is kept so a retry cannot create a second payment.
      if (!(e instanceof ApiError && e.status === 0)) key.current = newIdempotencyKey()
    } finally {
      setState('idle')
    }
  }

  return (
    <section className="card border-amber-500/40 p-5" aria-labelledby="pay-heading">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="pay-heading" className="text-lg">Payment</h2>
        {countdown && <p className={`chip ${expired ? 'bg-rose-50 text-rose-700' : 'bg-amber-400/20 text-amber-700'}`}><Clock className="size-3.5" aria-hidden />{expired ? 'Hold expired' : `Held for ${countdown.label}`}</p>}
      </div>
      <div className="mt-3 flex gap-3 rounded-xl bg-sand-100 p-3 text-sm text-ink-700">
        <FlaskConical className="size-5 shrink-0 text-amber-700" aria-hidden />
        <p><strong>Simulated payment.</strong> This demo does not connect to a payment provider and never asks for card details. Pressing pay records a test transaction.</p>
      </div>
      {error && <div className="mt-3"><Alert>{error}</Alert></div>}
      <div className="mt-4 flex flex-wrap gap-2">
        <button className="btn-primary flex-1 py-3" disabled={state === 'paying' || expired} onClick={() => pay(false)}>
          <CreditCard className="size-4" aria-hidden />{state === 'paying' ? 'Processing…' : `Pay ${money(booking.summary.totalAmount)} (simulated)`}
        </button>
        <button className="btn-outline" disabled={state === 'paying' || expired} onClick={() => pay(true)} title="Records a declined test payment so you can see the failure path">Simulate a decline</button>
      </div>
      {expired && <p className="mt-2 text-sm text-ink-500">The hold has lapsed and the car has been released. <Link to={`/vehicles/${booking.summary.vehicleId}`} className="font-bold underline">Check availability again</Link>.</p>}
    </section>
  )
}

function CancelPanel({ booking, onCancelled }: { booking: BookingDetail; onCancelled: (b: BookingDetail) => void }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const preview = useAsync(() => (open ? api.bookings.cancellation(booking.summary.reference) : Promise.resolve(booking.cancellation)), [open])

  return (
    <section className="card p-5">
      <h2 className="text-lg">Cancel booking</h2>
      <p className="mt-1 text-sm text-ink-500">{booking.cancellation.explanation}</p>
      <button className="btn-outline mt-3 w-full text-rose-700" onClick={() => setOpen(true)}>Cancel this booking</button>
      <Modal open={open} title="Cancel booking?" onClose={() => setOpen(false)}>
        {preview.loading ? <Spinner /> : preview.data && (
          <div className="space-y-3 text-sm">
            <p>{preview.data.explanation}</p>
            <dl className="grid grid-cols-2 gap-2 rounded-xl bg-sand-100 p-3">
              <dt>Cancellation fee</dt><dd className="text-right font-bold">{money(preview.data.fee)}</dd>
              <dt>Refund</dt><dd className="text-right font-bold">{money(preview.data.refund)}</dd>
            </dl>
            <label htmlFor="cancel-reason" className="label">Reason (optional)</label>
            <textarea id="cancel-reason" className="input" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
            {error && <Alert>{error}</Alert>}
            <div className="flex justify-end gap-2">
              <button className="btn-ghost" onClick={() => setOpen(false)}>Keep booking</button>
              <button className="btn-danger" disabled={busy} onClick={async () => {
                setBusy(true); setError(null)
                try { onCancelled(await api.bookings.cancel(booking.summary.reference, reason.trim() || null)); setOpen(false) } catch (e) { setError(errorMessage(e)) } finally { setBusy(false) }
              }}>{busy ? 'Cancelling…' : 'Cancel booking'}</button>
            </div>
          </div>
        )}
      </Modal>
    </section>
  )
}

const reviewSchema = z.object({
  overall: z.number().int().min(1, 'Choose a rating.').max(5),
  condition: z.number().int().min(1, 'Choose a rating.').max(5),
  cleanliness: z.number().int().min(1, 'Choose a rating.').max(5),
  comfort: z.number().int().min(1, 'Choose a rating.').max(5),
  value: z.number().int().min(1, 'Choose a rating.').max(5),
  title: z.string().trim().min(1, 'Add a short title.').max(100),
  comment: z.string().trim().min(10, 'Write at least 10 characters.').max(2000),
})
type ReviewValues = z.infer<typeof reviewSchema>

export function ReviewPanel({ reference, onDone }: { reference: string; onDone: () => void }) {
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const form = useForm<ReviewValues>({ resolver: zodResolver(reviewSchema), defaultValues: { overall: 0, condition: 0, cleanliness: 0, comfort: 0, value: 0, title: '', comment: '' } })
  const { control, register, handleSubmit, formState: { errors, isSubmitting } } = form

  if (done) return <Alert tone="success" title="Review published"><CheckCircle2 className="inline size-4" /> Thank you for your feedback.</Alert>

  return (
    <section className="card p-5" aria-labelledby="review-heading">
      <h2 id="review-heading" className="text-lg">Review your rental</h2>
      <p className="mt-1 text-sm text-ink-500">Your review is published with your first name and last initial.</p>
      <form className="mt-4 space-y-4" noValidate onSubmit={handleSubmit(async (values) => {
        setError(null)
        try { await api.bookings.review(reference, values); setDone(true); onDone() } catch (e) { setError(applyServerErrors(e, form.setError, Object.keys(values))) }
      })}>
        <div className="grid gap-4 sm:grid-cols-2">
          {(['overall', 'condition', 'cleanliness', 'comfort', 'value'] as const).map((name) => (
            <div key={name}>
              <Controller control={control} name={name} render={({ field }) => (
                <StarInput label={name === 'overall' ? 'Overall' : humanise(name)} value={field.value} onChange={field.onChange} />
              )} />
              {errors[name] && <p className="field-error">{errors[name]?.message}</p>}
            </div>
          ))}
        </div>
        <div>
          <label htmlFor="review-title" className="label">Title</label>
          <input id="review-title" className="input" {...register('title')} />
          {errors.title && <p className="field-error">{errors.title.message}</p>}
        </div>
        <div>
          <label htmlFor="review-comment" className="label">Your experience</label>
          <textarea id="review-comment" className="input min-h-28" {...register('comment')} />
          {errors.comment && <p className="field-error">{errors.comment.message}</p>}
        </div>
        {error && <Alert>{error}</Alert>}
        <button className="btn-dark" disabled={isSubmitting}>{isSubmitting ? 'Publishing…' : 'Publish review'}</button>
      </form>
    </section>
  )
}
