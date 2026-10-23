import { ArrowLeft, ArrowRight, Check, Info, MessageSquareText, Sparkles, TriangleAlert, Wand2 } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../api/client'
import { api } from '../api/endpoints'
import type { InterpretedPreferences, RecommendationRequest, RecommendationResponse, RecommendedVehicle } from '../api/types'
import { useLocations } from '../components/TripSearch'
import { Alert, SampleBadge, Spinner } from '../components/ui'
import { defaultWindow, formatDateTime, humanise, isoToLocalInput, localInputToIso, mediaUrl, money } from '../lib/format'
import { tripProblem, tripQuery } from '../lib/trip'

interface Prefs {
  pickup: string
  dropoff: string
  from?: number
  to?: number
  passengers: number
  luggage: number
  maxDaily: string
  maxTotal: string
  category: string
  transmission: string
  transmissionRequired: boolean
  fuel: string
  journey: string
  comfort: boolean
  economy: boolean
  space: boolean
}

const initialPrefs = (): Prefs => {
  const w = defaultWindow(2, 4)
  return { pickup: w.pickup, dropoff: w.dropoff, passengers: 2, luggage: 2, maxDaily: '', maxTotal: '', category: '', transmission: '', transmissionRequired: false, fuel: '', journey: '', comfort: false, economy: false, space: false }
}

const steps = ['Describe', 'Trip', 'Needs', 'Results'] as const
const categories = [['', 'No preference'], ['economy', 'Economy'], ['hatchback', 'Hatchback'], ['sedan', 'Sedan'], ['suv', 'SUV'], ['minivan', 'Minivan / MPV'], ['bakkie', 'Bakkie'], ['luxury', 'Luxury']] as const
const journeys = [['', 'Not sure'], ['city', 'City driving'], ['business', 'Business trip'], ['family', 'Family trip'], ['road-trip', 'Long-distance road trip']] as const

export default function RecommendPage() {
  const [step, setStep] = useState(0)
  const [prefs, setPrefs] = useState<Prefs>(initialPrefs)
  const [result, setResult] = useState<RecommendationResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<Prefs>) => setPrefs((p) => ({ ...p, ...patch }))

  const run = async () => {
    const problem = tripProblem({ pickup: prefs.pickup, dropoff: prefs.dropoff })
    if (problem) { setError(problem); setStep(1); return }
    setLoading(true); setError(null); setStep(3)
    const body: RecommendationRequest = {
      pickupAt: localInputToIso(prefs.pickup), returnAt: localInputToIso(prefs.dropoff),
      pickupLocationId: prefs.from ?? null, returnLocationId: prefs.to ?? prefs.from ?? null,
      passengers: prefs.passengers, luggageBags: prefs.luggage,
      maxDailyBudget: prefs.maxDaily ? Number(prefs.maxDaily) : null, maxTotalBudget: prefs.maxTotal ? Number(prefs.maxTotal) : null,
      preferredCategory: prefs.category || null, transmission: prefs.transmission || null, transmissionRequired: prefs.transmissionRequired,
      fuel: prefs.fuel || null, journey: prefs.journey || null,
      prioritiseComfort: prefs.comfort, prioritiseFuelEfficiency: prefs.economy, prioritiseSpace: prefs.space,
    }
    try { setResult(await api.recommendations.recommend(body)) } catch (e) { setError(errorMessage(e)); setResult(null) } finally { setLoading(false) }
  }

  return (
    <div className="container-page max-w-5xl py-8">
      <div className="mb-6">
        <p className="chip bg-amber-400/20 text-amber-700"><Sparkles className="size-3.5" aria-hidden />Recommendation wizard</p>
        <h1 className="mt-2 text-3xl">Find the right car for your trip</h1>
        <p className="mt-1 max-w-2xl text-sm text-ink-500">SmartDrive scores every available vehicle against your needs with a transparent, weighted formula. Each result shows how its score was built and what you give up.</p>
      </div>

      <ol className="mb-6 grid grid-cols-4 gap-2" aria-label="Progress">
        {steps.map((label, i) => (
          <li key={label}>
            <button type="button" onClick={() => i < 3 && setStep(i)} disabled={i === 3 && !result} aria-current={step === i ? 'step' : undefined}
              className={`w-full rounded-xl border px-2 py-2 text-left text-xs font-bold transition sm:text-sm ${step === i ? 'border-amber-500 bg-amber-400/15' : i < step ? 'border-leaf-600/30 bg-leaf-50 text-leaf-700' : 'border-ink-100 bg-white text-ink-500'}`}>
              <span className="mr-1 text-ink-400">{i + 1}.</span>{label}
            </button>
          </li>
        ))}
      </ol>

      {error && <div className="mb-4"><Alert>{error}</Alert></div>}

      {step === 0 && <DescribeStep onApply={(i) => { set(fromInterpretation(i, prefs)); setStep(1) }} onSkip={() => setStep(1)} />}
      {step === 1 && (
        <Panel footer={<><span /><button className="btn-primary" onClick={() => { const p = tripProblem({ pickup: prefs.pickup, dropoff: prefs.dropoff }); setError(p); if (!p) setStep(2) }}>Next <ArrowRight className="size-4" /></button></>}>
          <TripStep prefs={prefs} set={set} />
        </Panel>
      )}
      {step === 2 && (
        <Panel footer={<><button className="btn-ghost" onClick={() => setStep(1)}><ArrowLeft className="size-4" />Back</button><button className="btn-primary" onClick={run}><Wand2 className="size-4" />Recommend cars</button></>}>
          <NeedsStep prefs={prefs} set={set} />
        </Panel>
      )}
      {step === 3 && (loading ? <Spinner label="Scoring the fleet…" /> : result && <Results result={result} prefs={prefs} onEdit={() => setStep(2)} />)}
    </div>
  )
}

function Panel({ children, footer }: { children: ReactNode; footer: ReactNode }) {
  return (
    <div className="card">
      <div className="p-5 sm:p-6">{children}</div>
      <div className="flex items-center justify-between border-t border-ink-100 px-5 py-4">{footer}</div>
    </div>
  )
}

function fromInterpretation(i: InterpretedPreferences, prefs: Prefs): Partial<Prefs> {
  const patch: Partial<Prefs> = {}
  if (i.passengers) patch.passengers = i.passengers
  if (i.luggageBags != null) patch.luggage = i.luggageBags
  if (i.maxDailyBudget) patch.maxDaily = String(i.maxDailyBudget)
  if (i.maxTotalBudget) patch.maxTotal = String(i.maxTotalBudget)
  if (i.preferredCategory) patch.category = i.preferredCategory
  if (i.transmission) { patch.transmission = i.transmission; patch.transmissionRequired = i.transmissionRequired }
  if (i.fuel) patch.fuel = i.fuel
  if (i.journey) patch.journey = i.journey
  patch.comfort = i.prioritiseComfort
  patch.economy = i.prioritiseFuelEfficiency
  patch.space = i.prioritiseSpace
  if (i.durationDays) {
    const start = new Date(localInputToIso(prefs.pickup))
    patch.dropoff = isoToLocalInput(new Date(start.getTime() + i.durationDays * 86_400_000))
  }
  return patch
}

function DescribeStep({ onApply, onSkip }: { onApply: (i: InterpretedPreferences) => void; onSkip: () => void }) {
  const [text, setText] = useState('')
  const [result, setResult] = useState<InterpretedPreferences | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const examples = [
    'A comfortable car for four adults for a five-day trip, no more than R600 per day',
    'Automatic SUV for a family of 5 with lots of luggage, road trip to the coast',
    'Cheap small car for city driving this weekend',
  ]

  const interpret = async () => {
    setBusy(true); setError(null)
    try { setResult(await api.recommendations.interpret(text)) } catch (e) { setError(errorMessage(e)) } finally { setBusy(false) }
  }

  return (
    <Panel footer={<><button className="btn-ghost" onClick={onSkip}>Skip — fill in the form</button>{result ? <button className="btn-primary" onClick={() => onApply(result)}>Use these preferences <ArrowRight className="size-4" /></button> : <span />}</>}>
      <h2 className="flex items-center gap-2 text-lg"><MessageSquareText className="size-5 text-amber-600" aria-hidden />Describe your trip (optional)</h2>
      <p className="mt-1 text-sm text-ink-500">Write it the way you would say it. SmartDrive extracts passengers, luggage, budget and preferences — it does not invent vehicles, prices or availability. You can check and change everything on the next steps.</p>
      <label htmlFor="describe" className="sr-only">Trip description</label>
      <textarea id="describe" className="input mt-4 min-h-24" maxLength={500} value={text} onChange={(e) => { setText(e.target.value); setResult(null) }} placeholder={examples[0]} />
      <div className="mt-2 flex flex-wrap gap-2">
        {examples.map((ex) => <button key={ex} type="button" className="chip bg-ink-100 text-left text-ink-700 hover:bg-ink-200" onClick={() => { setText(ex); setResult(null) }}>{ex}</button>)}
      </div>
      <button className="btn-dark mt-4" disabled={!text.trim() || busy} onClick={interpret}>{busy ? 'Reading…' : 'Interpret'}</button>
      {error && <div className="mt-4"><Alert>{error}</Alert></div>}
      {result && (
        <div className="mt-5 rounded-xl border border-ink-100 bg-sand-50 p-4 text-sm" aria-live="polite">
          <p className="font-bold">What I understood</p>
          {result.understood.length ? (
            <ul className="mt-2 space-y-1">{result.understood.map((u) => <li key={u} className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-leaf-600" aria-hidden />{u}</li>)}</ul>
          ) : <p className="mt-1 text-ink-500">Nothing specific — you can set your needs on the next steps.</p>}
          {result.notes.length > 0 && <ul className="mt-3 space-y-1 text-ink-500">{result.notes.map((n) => <li key={n} className="flex gap-2"><Info className="mt-0.5 size-4 shrink-0" aria-hidden />{n}</li>)}</ul>}
          <p className="mt-3 text-xs text-ink-400">Interpreted by {result.source === 'rules' ? 'SmartDrive’s rule-based parser' : 'a language model, validated against SmartDrive’s allowed values'}.</p>
        </div>
      )}
    </Panel>
  )
}

function TripStep({ prefs, set }: { prefs: Prefs; set: (p: Partial<Prefs>) => void }) {
  const locations = useLocations()
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <div><label htmlFor="r-pickup" className="label">Pickup (SAST)</label><input id="r-pickup" type="datetime-local" className="input" value={prefs.pickup} onChange={(e) => set({ pickup: e.target.value })} /></div>
      <div><label htmlFor="r-return" className="label">Return (SAST)</label><input id="r-return" type="datetime-local" className="input" value={prefs.dropoff} onChange={(e) => set({ dropoff: e.target.value })} /></div>
      <div>
        <label htmlFor="r-from" className="label">Pickup branch</label>
        <select id="r-from" className="input" value={prefs.from ?? ''} onChange={(e) => set({ from: e.target.value ? Number(e.target.value) : undefined })}>
          <option value="">Any branch</option>{locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>
      </div>
      <div>
        <label htmlFor="r-to" className="label">Return branch</label>
        <select id="r-to" className="input" value={prefs.to ?? ''} onChange={(e) => set({ to: e.target.value ? Number(e.target.value) : undefined })}>
          <option value="">Same as pickup</option>{locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>
      </div>
      <div className="sm:col-span-2">
        <span className="label">Type of trip</span>
        <div className="flex flex-wrap gap-2">
          {journeys.map(([v, l]) => <button key={v} type="button" aria-pressed={prefs.journey === v} onClick={() => set({ journey: v })} className={prefs.journey === v ? 'btn-dark btn-sm' : 'btn-outline btn-sm'}>{l}</button>)}
        </div>
      </div>
    </div>
  )
}

function NeedsStep({ prefs, set }: { prefs: Prefs; set: (p: Partial<Prefs>) => void }) {
  const counter = (label: string, value: number, key: 'passengers' | 'luggage', min: number, max: number) => (
    <div>
      <span className="label" id={`lbl-${key}`}>{label}</span>
      <div className="flex items-center gap-3" role="group" aria-labelledby={`lbl-${key}`}>
        <button type="button" className="btn-outline size-10 p-0" aria-label={`Fewer ${label.toLowerCase()}`} disabled={value <= min} onClick={() => set({ [key]: value - 1 })}>−</button>
        <span className="w-8 text-center text-xl font-extrabold" aria-live="polite">{value}</span>
        <button type="button" className="btn-outline size-10 p-0" aria-label={`More ${label.toLowerCase()}`} disabled={value >= max} onClick={() => set({ [key]: value + 1 })}>+</button>
      </div>
    </div>
  )
  return (
    <div className="grid gap-5 sm:grid-cols-2">
      {counter('Passengers', prefs.passengers, 'passengers', 1, 15)}
      {counter('Suitcases', prefs.luggage, 'luggage', 0, 15)}
      <div><label htmlFor="r-maxd" className="label">Max. per day (R, optional)</label><input id="r-maxd" type="number" min={50} step={50} inputMode="numeric" className="input" value={prefs.maxDaily} onChange={(e) => set({ maxDaily: e.target.value })} /></div>
      <div><label htmlFor="r-maxt" className="label">Max. total (R, optional)</label><input id="r-maxt" type="number" min={50} step={100} inputMode="numeric" className="input" value={prefs.maxTotal} onChange={(e) => set({ maxTotal: e.target.value })} /></div>
      <div>
        <label htmlFor="r-cat" className="label">Preferred category</label>
        <select id="r-cat" className="input" value={prefs.category} onChange={(e) => set({ category: e.target.value })}>{categories.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>
      </div>
      <div>
        <label htmlFor="r-fuel" className="label">Fuel</label>
        <select id="r-fuel" className="input" value={prefs.fuel} onChange={(e) => set({ fuel: e.target.value })}>
          <option value="">No preference</option><option>Petrol</option><option>Diesel</option><option>Hybrid</option><option>Electric</option>
        </select>
      </div>
      <div>
        <label htmlFor="r-tr" className="label">Gearbox</label>
        <select id="r-tr" className="input" value={prefs.transmission} onChange={(e) => set({ transmission: e.target.value })}>
          <option value="">No preference</option><option value="Automatic">Automatic</option><option value="Manual">Manual</option>
        </select>
        {prefs.transmission && (
          <label className="mt-2 flex items-center gap-2 text-sm"><input type="checkbox" className="size-4 accent-amber-600" checked={prefs.transmissionRequired} onChange={(e) => set({ transmissionRequired: e.target.checked })} />I can only drive {prefs.transmission.toLowerCase()}</label>
        )}
      </div>
      <fieldset>
        <legend className="label">What matters most?</legend>
        <div className="space-y-2 text-sm">
          <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-amber-600" checked={prefs.comfort} onChange={(e) => set({ comfort: e.target.checked })} />Comfort</label>
          <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-amber-600" checked={prefs.economy} onChange={(e) => set({ economy: e.target.checked })} />Fuel efficiency</label>
          <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-amber-600" checked={prefs.space} onChange={(e) => set({ space: e.target.checked })} />Space</label>
        </div>
      </fieldset>
    </div>
  )
}

function Results({ result, prefs, onEdit }: { result: RecommendationResponse; prefs: Prefs; onEdit: () => void }) {
  const query = tripQuery({ pickup: prefs.pickup, dropoff: prefs.dropoff, from: prefs.from, to: prefs.to ?? prefs.from })
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-ink-500">{result.eligibleCount} of {result.fleetSize} vehicles meet your hard requirements · {formatDateTime(localInputToIso(prefs.pickup))} → {formatDateTime(localInputToIso(prefs.dropoff))}</p>
        <button className="btn-outline btn-sm" onClick={onEdit}>Adjust preferences</button>
      </div>

      {result.unmetRequirements.length > 0 && (
        <Alert tone="warning" title="Some requirements could not be met">
          <ul className="list-disc pl-4">{result.unmetRequirements.map((u) => <li key={u}>{u}</li>)}</ul>
        </Alert>
      )}
      {result.suggestions.length > 0 && (
        <Alert tone="info" title="Suggestions"><ul className="list-disc pl-4">{result.suggestions.map((s) => <li key={s}>{s}</li>)}</ul></Alert>
      )}

      {result.matches.length > 0 ? (
        <div className="space-y-4">
          {result.matches.map((m, i) => <MatchCard key={m.vehicle.id} match={m} rank={i + 1} query={query} />)}
        </div>
      ) : <Alert tone="info" title="No exact matches">No available vehicle meets every requirement for these dates. Close alternatives are listed below.</Alert>}

      {result.alternatives.length > 0 && (
        <section>
          <h2 className="mb-3 text-xl">Close alternatives</h2>
          <div className="space-y-4">{result.alternatives.map((m) => <MatchCard key={m.vehicle.id} match={m} query={query} />)}</div>
        </section>
      )}

      <details className="rounded-xl bg-sand-100 p-4 text-sm text-ink-700">
        <summary className="cursor-pointer font-bold">How are these scores calculated?</summary>
        <p className="mt-2 whitespace-pre-line">{result.algorithm}</p>
      </details>
    </div>
  )
}

function MatchCard({ match: m, rank, query }: { match: RecommendedVehicle; rank?: number; query: string }) {
  return (
    <article className="card overflow-hidden">
      <div className="grid gap-0 md:grid-cols-[220px_1fr_250px]">
        <div className="relative bg-sand-100">
          {m.vehicle.imageUrl && <img src={mediaUrl(m.vehicle.imageUrl)} alt={m.vehicle.imageAlt ?? m.vehicle.displayName} className="h-full max-h-56 w-full object-cover md:max-h-none" loading="lazy" />}
          {rank && <span className="absolute left-3 top-3 grid size-8 place-items-center rounded-full bg-ink-900 text-sm font-extrabold text-amber-400">#{rank}</span>}
        </div>
        <div className="p-5">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-lg">{m.vehicle.displayName}</h3>
            <span className="chip bg-ink-100 text-ink-700">{m.vehicle.category}</span>
            {m.vehicle.isSampleData && <SampleBadge />}
          </div>
          <p className="mt-1 text-sm text-ink-700">{m.summary}</p>
          {m.reasons.length > 0 && <ul className="mt-3 space-y-1 text-sm">{m.reasons.map((r) => <li key={r} className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-leaf-600" aria-hidden />{r}</li>)}</ul>}
          {m.tradeOffs.length > 0 && <ul className="mt-2 space-y-1 text-sm text-ink-500">{m.tradeOffs.map((t) => <li key={t} className="flex gap-2"><TriangleAlert className="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden />{t}</li>)}</ul>}
          {!m.isAvailable && m.nextAvailableFrom && <p className="mt-2 text-sm text-rose-700">Not free for your dates — next available {formatDateTime(m.nextAvailableFrom)}.</p>}
        </div>
        <div className="border-t border-ink-100 bg-sand-50 p-5 md:border-l md:border-t-0">
          <p className="text-xs font-bold uppercase tracking-wide text-ink-500">Match score</p>
          <p className="text-3xl font-extrabold">{m.score}<span className="text-base text-ink-400">/100</span></p>
          <dl className="mt-3 space-y-1.5" aria-label="Score breakdown">
            {m.components.map((c) => (
              <div key={c.name} className="text-xs">
                <div className="flex justify-between"><dt>{humanise(c.name)} <span className="text-ink-400">×{Math.round(c.weight * 100)}%</span></dt><dd className="font-semibold">{Math.round(c.score * 100)}</dd></div>
                <div className="mt-0.5 h-1.5 overflow-hidden rounded-full bg-ink-100" aria-hidden><div className="h-full rounded-full bg-amber-500" style={{ width: `${Math.round(c.score * 100)}%` }} /></div>
              </div>
            ))}
          </dl>
          <p className="mt-4 text-sm"><strong>{money(m.estimatedTotal)}</strong> total · {money(m.effectiveDailyRate, true)}/day</p>
          <Link to={`/vehicles/${m.vehicle.id}?${query}`} className="btn-primary mt-3 w-full">{m.isAvailable ? 'View and book' : 'View vehicle'}</Link>
        </div>
      </div>
    </article>
  )
}
