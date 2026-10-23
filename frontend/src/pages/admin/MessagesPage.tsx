import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import { Alert, EmptyState, PageHeader, Pagination, Spinner } from '../../components/ui'
import { formatDateTime } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'

export default function MessagesPage() {
  const [handled, setHandled] = useState<boolean | undefined>(false)
  const [page, setPage] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const list = useAsync(() => api.admin.messages(handled, page), [handled, page])
  return (
    <>
      <PageHeader title="Messages" subtitle="Sent through the public contact form." />
      <div className="mb-4 flex gap-2">
        {([[false, 'Open'], [true, 'Handled'], [undefined, 'All']] as const).map(([v, l]) => (
          <button key={l} className={handled === v ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} onClick={() => { setHandled(v); setPage(1) }}>{l}</button>
        ))}
      </div>
      {error && <div className="mb-3"><Alert>{error}</Alert></div>}
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data?.items.length === 0 && <EmptyState title="No messages" />}
      <div className="space-y-3">
        {list.data?.items.map((m) => (
          <article key={m.id} className="card p-4">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div><h3 className="text-sm">{m.subject}</h3><p className="text-xs text-ink-500">{m.name} · <a className="underline" href={`mailto:${m.email}`}>{m.email}</a> · {formatDateTime(m.createdAt)}</p></div>
              <button className="btn-outline btn-sm" onClick={async () => { setError(null); try { await api.admin.setMessageHandled(m.id, !m.isHandled); list.reload() } catch (e) { setError(errorMessage(e)) } }}>{m.isHandled ? 'Reopen' : 'Mark handled'}</button>
            </div>
            <p className="mt-2 whitespace-pre-line text-sm text-ink-700">{m.message}</p>
          </article>
        ))}
      </div>
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
    </>
  )
}
