import { Download, Lightbulb } from 'lucide-react'
import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api, downloadCsv, type AnalyticsQuery } from '../../api/endpoints'
import type { Insight } from '../../api/types'
import { Alert, EmptyState, PageHeader, Spinner, Stat } from '../../components/ui'
import { money, number, percent } from '../../lib/format'
import { useAsync } from '../../lib/useAsync'
import { ChartCard, DataTable, SERIES, SimpleBar, SimpleLine } from './charts'

const tabs = ['Insights', 'Demand', 'Utilisation', 'Revenue', 'Sentiment', 'Forecast'] as const
type Tab = (typeof tabs)[number]

function isoDay(d: Date) { return d.toISOString().slice(0, 10) }

export default function InsightsPage() {
  const today = new Date()
  const [tab, setTab] = useState<Tab>('Insights')
  const [query, setQuery] = useState<AnalyticsQuery>({ from: isoDay(new Date(today.getTime() - 180 * 86_400_000)), to: isoDay(today) })
  const categories = useAsync(() => api.catalog.categories(), [])

  return (
    <>
      <PageHeader title="Smart Insights" subtitle="Rule-based findings and statistics computed from booking, maintenance and review records. Every figure states its period, evidence and limits." />
      <div className="card mb-5 flex flex-wrap items-end gap-3 p-4" role="group" aria-label="Analysis period">
        <div><label htmlFor="a-from" className="label">From</label><input id="a-from" type="date" className="input" value={query.from} max={query.to} onChange={(e) => setQuery({ ...query, from: e.target.value })} /></div>
        <div><label htmlFor="a-to" className="label">To</label><input id="a-to" type="date" className="input" value={query.to} min={query.from} onChange={(e) => setQuery({ ...query, to: e.target.value })} /></div>
        <div>
          <label htmlFor="a-cat" className="label">Category</label>
          <select id="a-cat" className="input" value={query.category ?? ''} onChange={(e) => setQuery({ ...query, category: e.target.value || undefined })}>
            <option value="">Whole fleet</option>{categories.data?.map((c) => <option key={c.slug} value={c.slug}>{c.name}</option>)}
          </select>
        </div>
        <div className="flex gap-1">
          {[30, 90, 180, 365].map((days) => (
            <button key={days} className="btn-outline btn-sm" onClick={() => setQuery({ ...query, from: isoDay(new Date(today.getTime() - days * 86_400_000)), to: isoDay(today) })}>{days}d</button>
          ))}
        </div>
      </div>

      <div role="tablist" aria-label="Insight area" className="mb-5 flex gap-1 overflow-x-auto rounded-xl bg-ink-100 p-1">
        {tabs.map((t) => (
          <button key={t} role="tab" aria-selected={tab === t} onClick={() => setTab(t)} className={`shrink-0 rounded-lg px-3 py-2 text-sm font-bold ${tab === t ? 'bg-white shadow-sm' : 'text-ink-500'}`}>{t}</button>
        ))}
      </div>

      {tab === 'Insights' && <InsightsTab q={query} />}
      {tab === 'Demand' && <DemandTab q={query} />}
      {tab === 'Utilisation' && <UtilisationTab q={query} />}
      {tab === 'Revenue' && <RevenueTab q={query} />}
      {tab === 'Sentiment' && <SentimentTab q={query} />}
      {tab === 'Forecast' && <ForecastTab category={query.category} />}
    </>
  )
}

function ExportButton({ report, q }: { report: string; q: AnalyticsQuery }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <span className="inline-flex items-center gap-2">
      {error && <span className="text-xs text-rose-700">{error}</span>}
      <button className="btn-outline btn-sm" disabled={busy} onClick={async () => {
        setBusy(true); setError(null)
        try { await downloadCsv(`/api/admin/analytics/export/${report}`, q, `smartdrive-${report}.csv`) } catch (e) { setError(errorMessage(e)) } finally { setBusy(false) }
      }}><Download className="size-3.5" />CSV</button>
    </span>
  )
}

function Definition({ children }: { children: string }) {
  return <p className="mt-4 rounded-xl bg-sand-100 p-3 text-xs text-ink-700"><strong>Definition.</strong> {children}</p>
}

const severityTone: Record<string, string> = { warning: 'border-l-rose-600', attention: 'border-l-amber-500', info: 'border-l-ink-400' }

function InsightsTab({ q }: { q: AnalyticsQuery }) {
  const r = useAsync(() => api.analytics.insights(q), [q.from, q.to, q.category])
  if (r.loading) return <Spinner label="Analysing…" />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  return (
    <div className="space-y-4">
      <Alert tone="info">{r.data.notice} ({r.data.totalBookings} bookings in {r.data.period.label}.)</Alert>
      {r.data.insights.length === 0 ? (
        <EmptyState icon={<Lightbulb className="size-10" />} title="No findings crossed their thresholds">Insights only appear when the evidence is strong enough. Try a longer period.</EmptyState>
      ) : r.data.insights.map((i) => <InsightCard key={`${i.area}-${i.title}`} insight={i} />)}
    </div>
  )
}

export function InsightCard({ insight: i }: { insight: Insight }) {
  return (
    <article className={`card border-l-4 p-5 ${severityTone[i.severity.toLowerCase()] ?? 'border-l-ink-400'}`}>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="chip bg-ink-100 text-ink-700">{i.area}</span>
        <span className="chip bg-ink-100 text-ink-700">Severity: {i.severity}</span>
        <span className="chip bg-ink-100 text-ink-700">Confidence: {i.confidence}</span>
        <span className="text-ink-500">{i.period}</span>
      </div>
      <h3 className="mt-2 text-lg">{i.title}</h3>
      <p className="mt-1 text-sm text-ink-700">{i.finding}</p>
      <dl className="mt-3 grid gap-3 text-sm md:grid-cols-3">
        <div className="rounded-xl bg-sand-50 p-3"><dt className="text-xs font-bold uppercase text-ink-500">Evidence</dt><dd className="mt-1">{i.evidence}</dd></div>
        <div className="rounded-xl bg-sand-50 p-3"><dt className="text-xs font-bold uppercase text-ink-500">Suggested action</dt><dd className="mt-1">{i.suggestedAction}</dd></div>
        <div className="rounded-xl bg-sand-50 p-3"><dt className="text-xs font-bold uppercase text-ink-500">Limitations</dt><dd className="mt-1">{i.limitations}</dd></div>
      </dl>
    </article>
  )
}

function DemandTab({ q }: { q: AnalyticsQuery }) {
  const r = useAsync(() => api.analytics.demand(q), [q.from, q.to, q.category])
  if (r.loading) return <Spinner />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  const d = r.data
  const weekdayShare = d.weekdayPickups + d.weekendPickups ? d.weekendPickups / (d.weekdayPickups + d.weekendPickups) : null
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Bookings" value={number(d.totalBookings)} hint={d.period.label} />
        <Stat label="Avg. rental" value={`${d.averageRentalDays.toFixed(1)} days`} />
        <Stat label="Weekday pickups" value={number(d.weekdayPickups)} />
        <Stat label="Weekend pickups" value={number(d.weekendPickups)} hint={weekdayShare !== null ? `${percent(weekdayShare)} of pickups` : undefined} />
      </div>
      <div className="grid gap-5 xl:grid-cols-2">
        <ChartCard title="Bookings per month" actions={<ExportButton report="demand" q={q} />} table={<DataTable headers={['Month', 'Bookings']} rows={d.monthly.map((p) => [p.label, p.value])} />}>
          <SimpleBar data={d.monthly} xKey="label" series={[{ key: 'value', name: 'Bookings' }]} />
        </ChartCard>
        <ChartCard title="Bookings by category" table={<DataTable headers={['Category', 'Bookings']} rows={d.byCategory.map((p) => [p.label, p.value])} />}>
          <SimpleBar horizontal data={d.byCategory} xKey="label" series={[{ key: 'value', name: 'Bookings' }]} />
        </ChartCard>
        <ChartCard title="Pickups by weekday" table={<DataTable headers={['Day', 'Pickups']} rows={d.pickupsByWeekday.map((p) => [p.label, p.value])} />}>
          <SimpleBar data={d.pickupsByWeekday} xKey="label" series={[{ key: 'value', name: 'Pickups' }]} />
        </ChartCard>
      </div>
      <Definition>{d.definition}</Definition>
    </div>
  )
}

function UtilisationTab({ q }: { q: AnalyticsQuery }) {
  const r = useAsync(() => api.analytics.utilisation(q), [q.from, q.to, q.category])
  const [sort, setSort] = useState<'asc' | 'desc'>('desc')
  if (r.loading) return <Spinner />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  const u = r.data
  const vehicles = [...u.vehicles].sort((a, b) => (sort === 'desc' ? b.utilisation - a.utilisation : a.utilisation - b.utilisation))
  const cats = u.categories.map((c) => ({ ...c, pct: Math.round(c.utilisation * 1000) / 10 }))
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Stat label="Fleet utilisation" value={percent(u.fleetUtilisation, 1)} hint={u.period.label} />
        <Stat label="Vehicles analysed" value={u.vehicles.length} />
      </div>
      <ChartCard title="Utilisation by category" subtitle="Booked days ÷ available days, %" table={<DataTable headers={['Category', 'Vehicles', 'Booked days', 'Available days', 'Utilisation']} rows={u.categories.map((c) => [c.category, c.vehicles, number(c.bookedDays, 1), number(c.availableDays, 1), percent(c.utilisation, 1)])} />}>
        <SimpleBar horizontal data={cats} xKey="category" series={[{ key: 'pct', name: 'Utilisation %' }]} format={(v) => `${v}%`} />
      </ChartCard>
      <ChartCard title="Vehicles" actions={<div className="flex gap-2"><button className="btn-outline btn-sm" onClick={() => setSort(sort === 'desc' ? 'asc' : 'desc')}>{sort === 'desc' ? 'Most used first' : 'Least used first'}</button><ExportButton report="utilisation" q={q} /></div>}>
        <div className="overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>Vehicle</th><th>Category</th><th className="text-right">Booked</th><th className="text-right">Available</th><th className="text-right">Workshop</th><th>Utilisation</th></tr></thead>
            <tbody>
              {vehicles.map((v) => (
                <tr key={v.vehicleId}>
                  <td className="font-semibold">{v.name}</td><td>{v.category}</td>
                  <td className="text-right">{number(v.bookedDays, 1)} d</td><td className="text-right">{number(v.availableDays, 1)} d</td><td className="text-right">{number(v.maintenanceDays, 1)} d</td>
                  <td className="min-w-40"><div className="flex items-center gap-2"><div className="h-2 flex-1 overflow-hidden rounded-full bg-ink-100" aria-hidden><div className="h-full rounded-full" style={{ width: `${Math.min(100, v.utilisation * 100)}%`, background: SERIES[0] }} /></div><span className="w-12 text-right text-xs font-semibold">{percent(v.utilisation)}</span></div></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </ChartCard>
      <Definition>{u.definition}</Definition>
    </div>
  )
}

function RevenueTab({ q }: { q: AnalyticsQuery }) {
  const r = useAsync(() => api.analytics.revenue(q), [q.from, q.to, q.category])
  if (r.loading) return <Spinner />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  const v = r.data
  const monthly = v.monthly.map((m) => ({ label: m.label, Realised: m.values.Realised ?? 0, Booked: m.values.Booked ?? 0 }))
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <Stat label="Realised revenue" value={money(v.realisedRevenue, true)} hint={v.period.label} />
        <Stat label="Booked revenue" value={money(v.bookedRevenue, true)} />
        <Stat label="Avg. rental value" value={money(v.averageRentalValue, true)} />
        <Stat label="Discounts given" value={money(v.discountsGiven, true)} />
        <Stat label="Cancellation fees" value={money(v.cancellationFees, true)} />
        <Stat label="Cancellation rate" value={percent(v.cancellationRate, 1)} hint={`${v.cancellations} of ${v.bookings}`} />
      </div>
      <div className="grid gap-5 xl:grid-cols-2">
        <ChartCard title="Revenue per month (ZAR, VAT incl.)" table={<DataTable headers={['Month', 'Realised', 'Booked']} rows={monthly.map((m) => [m.label, money(m.Realised), money(m.Booked)])} />}>
          <SimpleBar data={monthly} xKey="label" series={[{ key: 'Realised', name: 'Realised' }, { key: 'Booked', name: 'Booked' }]} format={(n) => `R${Math.round(n / 1000)}k`} />
        </ChartCard>
        <ChartCard title="Realised revenue by category" table={<DataTable headers={['Category', 'Revenue']} rows={v.byCategory.map((c) => [c.label, money(c.value)])} />}>
          <SimpleBar horizontal data={v.byCategory} xKey="label" series={[{ key: 'value', name: 'Revenue' }]} format={(n) => `R${Math.round(n / 1000)}k`} />
        </ChartCard>
      </div>
      <ChartCard title="Top vehicles by revenue" actions={<ExportButton report="revenue" q={q} />}>
        <DataTable headers={['Vehicle', 'Category', 'Rentals', 'Revenue', 'Per rental']} rows={v.topVehicles.map((t) => [t.vehicle, t.category, t.rentals, money(t.revenue), money(t.revenuePerRental)])} />
      </ChartCard>
      <Definition>{v.definition}</Definition>
    </div>
  )
}

function SentimentTab({ q }: { q: AnalyticsQuery }) {
  const r = useAsync(() => api.analytics.sentiment(q), [q.from, q.to, q.category])
  if (r.loading) return <Spinner />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  const s = r.data
  return (
    <div className="space-y-5">
      <Alert tone="warning" title="Automated classification">{s.disclaimer}</Alert>
      <div className="grid gap-5 xl:grid-cols-2">
        <ChartCard title={`Review sentiment (${s.reviewsAnalysed} reviews)`} table={<DataTable headers={['Label', 'Reviews']} rows={s.labels.map((l) => [l.label, l.value])} />}>
          <SimpleBar data={s.labels} xKey="label" series={[{ key: 'value', name: 'Reviews' }]} />
        </ChartCard>
        <ChartCard title="Themes mentioned" subtitle="Count of reviews mentioning each theme positively or negatively"
          table={<DataTable headers={['Theme', 'Positive', 'Negative']} rows={s.themes.map((t) => [t.theme, t.positive, t.negative])} />}>
          <SimpleBar horizontal data={s.themes} xKey="theme" series={[{ key: 'positive', name: 'Positive' }, { key: 'negative', name: 'Negative' }]} height={280} />
        </ChartCard>
      </div>
      <div className="grid gap-5 xl:grid-cols-2">
        <Examples title="Recent negative reviews" items={s.negativeExamples} />
        <Examples title="Recent positive reviews" items={s.positiveExamples} />
      </div>
      <Definition>{s.method}</Definition>
    </div>
  )
}

function Examples({ title, items }: { title: string; items: { reviewId: string; vehicle: string; overall: number; title: string; excerpt: string; themes: string[] }[] }) {
  return (
    <section className="card p-4">
      <h2 className="text-base">{title}</h2>
      {items.length === 0 && <p className="mt-2 text-sm text-ink-500">None in this period.</p>}
      <ul className="mt-2 space-y-3 text-sm">
        {items.map((e) => (
          <li key={e.reviewId} className="rounded-xl bg-sand-50 p-3">
            <p className="font-semibold">{e.title} <span className="font-normal text-ink-500">· {e.vehicle} · {e.overall}/5</span></p>
            <p className="mt-1 text-ink-700">“{e.excerpt}”</p>
            {e.themes.length > 0 && <p className="mt-1 text-xs text-ink-500">Themes: {e.themes.join(', ')}</p>}
          </li>
        ))}
      </ul>
    </section>
  )
}

function ForecastTab({ category }: { category?: string }) {
  const [horizon, setHorizon] = useState(4)
  const r = useAsync(() => api.analytics.forecast(category, horizon), [category, horizon])
  if (r.loading) return <Spinner />
  if (r.error || !r.data) return <Alert onRetry={r.reload}>{r.error}</Alert>
  const f = r.data
  const data = [
    ...f.history.map((p) => ({ label: p.label, actual: p.value, forecast: null as number | null })),
    ...f.forecast.map((p) => ({ label: p.label, actual: null as number | null, forecast: Math.round(p.value * 10) / 10 })),
  ]
  // Join the forecast line to the last actual point so the two read as one timeline.
  if (f.history.length && f.forecast.length) data[f.history.length - 1].forecast = f.history[f.history.length - 1].value
  const res = f.result
  const best = [...res.evaluations].sort((a, b) => a.meanAbsoluteError - b.meanAbsoluteError)[0]

  return (
    <div className="space-y-5">
      <Alert tone="warning" title="Forecast caveats">{f.disclaimer}</Alert>
      {!res.hasEnoughData ? (
        <Alert tone="info" title="Not enough history">{res.note}</Alert>
      ) : (
        <>
          <ChartCard title={`Weekly bookings: history and ${horizon}-week forecast`} subtitle={`${f.category ?? 'Whole fleet'} · method used: ${res.chosenMethod}`}
            actions={<label className="flex items-center gap-2 text-xs">Horizon<select className="input w-auto py-1" value={horizon} onChange={(e) => setHorizon(Number(e.target.value))}>{[2, 4, 8, 12].map((h) => <option key={h} value={h}>{h} weeks</option>)}</select></label>}
            table={<DataTable headers={['Week', 'Actual', 'Forecast']} rows={data.map((d) => [d.label, d.actual ?? '', d.forecast ?? ''])} />}>
            <SimpleLine data={data} xKey="label" height={280} series={[{ key: 'actual', name: 'Actual bookings' }, { key: 'forecast', name: 'Forecast', dashed: true }]} />
          </ChartCard>
          <section className="card p-5">
            <h2 className="text-base">Back-test against a baseline</h2>
            <p className="mt-1 text-sm text-ink-500">Both methods were fitted without the most recent weeks and scored on how well they predicted them (mean absolute error, bookings per week — lower is better).</p>
            <table className="table-base mt-3 max-w-lg">
              <thead><tr><th>Method</th><th className="text-right">MAE</th><th /></tr></thead>
              <tbody>{res.evaluations.map((e) => (
                <tr key={e.method}><td>{e.method}</td><td className="text-right font-semibold">{e.meanAbsoluteError.toFixed(2)}</td><td>{best?.method === e.method && <span className="chip bg-leaf-50 text-leaf-700">Lowest error</span>}</td></tr>
              ))}</tbody>
            </table>
            <p className="mt-3 text-sm text-ink-700">{res.note}</p>
            {res.alpha !== null && <p className="mt-1 text-xs text-ink-500">Holt smoothing parameters: α = {res.alpha}, β = {res.beta} · {res.historyWeeks} weeks of history.</p>}
          </section>
        </>
      )}
    </div>
  )
}
