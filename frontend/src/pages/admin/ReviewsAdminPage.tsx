import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import { Alert, EmptyState, PageHeader, Pagination, SampleBadge, Spinner, Stars, StatusBadge } from '../../components/ui'
import { formatDate } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

const filters = [['reported', 'Reported'], ['published', 'Published'], ['hidden', 'Hidden'], ['all', 'All']] as const

export default function ReviewsAdminPage() {
  const [status, setStatus] = useState('reported')
  const [page, setPage] = useState(1)
  const [notes, setNotes] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const list = useAsync(() => api.admin.reviews(status, page), [status, page])
  const moderate = async (id: string, action: string) => {
    setError(null)
    try { await api.admin.moderate(id, action, notes[id]?.trim() || null); list.reload() } catch (e) { setError(errorMessage(e)) }
  }

  return (
    <>
      <PageHeader title="Review moderation" subtitle="Customers' original words are always shown. Sentiment labels are automated estimates to help triage, not judgements."
        actions={<button className="btn-outline" onClick={async () => { setError(null); try { const r = await api.admin.reanalyse(); setInfo(`Re-analysed ${r.analysed} reviews.`); list.reload() } catch (e) { setError(errorMessage(e)) } }}>Re-run sentiment</button>} />
      <div role="tablist" className="mb-4 flex gap-1 rounded-xl bg-ink-100 p-1">
        {filters.map(([v, l]) => <button key={v} role="tab" aria-selected={status === v} className={`flex-1 rounded-lg px-3 py-2 text-sm font-bold ${status === v ? 'bg-white shadow-sm' : 'text-ink-500'}`} onClick={() => { setStatus(v); setPage(1) }}>{l}</button>)}
      </div>
      {error && <div className="mb-3"><Alert>{error}</Alert></div>}
      {info && <div className="mb-3"><Alert tone="success">{info}</Alert></div>}
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data?.items.length === 0 && <EmptyState title={status === 'reported' ? 'No reported reviews' : 'No reviews'}>{status === 'reported' ? 'Nothing needs moderation right now.' : ''}</EmptyState>}
      <div className="space-y-3">
        {list.data?.items.map((r) => (
          <article key={r.review.id} className="card p-4">
            <div className="flex flex-wrap items-center gap-2">
              <Stars value={r.review.overall} /><h3 className="text-sm">{r.review.title}</h3><StatusBadge status={r.status} />
              {r.sentimentLabel && <StatusBadge status={r.sentimentLabel} />}
              {r.review.isSampleData && <SampleBadge />}
            </div>
            <p className="mt-1 text-xs text-ink-500">{r.customerName} · {r.review.vehicleName} · booking {r.bookingReference} · {formatDate(r.review.createdAt)}</p>
            <p className="mt-2 text-sm">{r.review.comment}</p>
            {(r.positiveThemes.length > 0 || r.negativeThemes.length > 0) && (
              <p className="mt-2 text-xs text-ink-500">{r.positiveThemes.length > 0 && <>Positive themes: {r.positiveThemes.join(', ')}. </>}{r.negativeThemes.length > 0 && <>Negative themes: {r.negativeThemes.join(', ')}.</>}</p>
            )}
            {r.reportCount > 0 && (
              <div className="mt-3 rounded-xl bg-amber-400/10 p-3 text-sm"><p className="font-bold">{r.reportCount} report{r.reportCount === 1 ? '' : 's'}</p><ul className="mt-1 list-disc pl-5 text-ink-700">{r.reportReasons.map((x, i) => <li key={i}>{x}</li>)}</ul></div>
            )}
            {r.moderationNote && <p className="mt-2 text-xs text-ink-500">Moderation note: {r.moderationNote}</p>}
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <input aria-label="Moderation note" className="input max-w-xs py-1.5" placeholder="Note (optional)" maxLength={500} value={notes[r.review.id] ?? ''} onChange={(e) => setNotes({ ...notes, [r.review.id]: e.target.value })} />
              {r.status !== 'Published' && <button className="btn-dark btn-sm" onClick={() => moderate(r.review.id, 'publish')}>{r.status === 'Reported' ? 'Keep published' : 'Publish'}</button>}
              {r.status !== 'Hidden' && r.status !== 'Removed' && <button className="btn-outline btn-sm text-rose-700" onClick={() => moderate(r.review.id, 'hide')}>Hide</button>}
            </div>
          </article>
        ))}
      </div>
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
    </>
  )
}
