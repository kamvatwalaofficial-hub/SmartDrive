import { zodResolver } from '@hookform/resolvers/zod'
import { Archive, Plus, Star, Trash2, Upload } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { z } from 'zod'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import type { AdminVehicle, UpsertVehicle } from '../../api/types'
import { Field } from '../../components/Field'
import { Alert, EmptyState, PageHeader, Pagination, SampleBadge, Spinner, StatusBadge } from '../../components/ui'
import { formatDate, formatDateTime, humanise, mediaUrl, money, number } from '../../lib/format'
import { applyServerErrors } from '../../lib/forms'
import { useAsync } from '../../lib/useAsync'

export function FleetListPage() {
  const [q, setQ] = useState('')
  const [status, setStatus] = useState('')
  const [category, setCategory] = useState('')
  const [archived, setArchived] = useState(false)
  const [page, setPage] = useState(1)
  const categories = useAsync(() => api.catalog.categories(), [])
  const list = useAsync(() => api.admin.vehicles({ q: q || undefined, status: status || undefined, category: category || undefined, includeArchived: archived, page, pageSize: 25 }), [q, status, category, archived, page])

  return (
    <>
      <PageHeader title="Fleet" subtitle="Vehicles, pricing, service schedules and media." actions={<Link to="/admin/fleet/new" className="btn-primary"><Plus className="size-4" />Add vehicle</Link>} />
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-48 flex-1"><label htmlFor="fl-q" className="label">Search</label><input id="fl-q" className="input" placeholder="Make, model or registration" defaultValue={q} onKeyDown={(e) => { if (e.key === 'Enter') { setQ(e.currentTarget.value.trim()); setPage(1) } }} onBlur={(e) => { setQ(e.currentTarget.value.trim()); setPage(1) }} /></div>
        <div><label htmlFor="fl-cat" className="label">Category</label><select id="fl-cat" className="input" value={category} onChange={(e) => { setCategory(e.target.value); setPage(1) }}><option value="">All</option>{categories.data?.map((c) => <option key={c.slug} value={c.slug}>{c.name}</option>)}</select></div>
        <div><label htmlFor="fl-st" className="label">Status</label><select id="fl-st" className="input" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}><option value="">All</option><option value="Available">Available</option><option value="UnderMaintenance">Under maintenance</option><option value="Inactive">Inactive</option></select></div>
        <label className="flex items-center gap-2 pb-2.5 text-sm"><input type="checkbox" className="size-4 accent-amber-600" checked={archived} onChange={(e) => setArchived(e.target.checked)} />Include archived</label>
      </div>
      {list.loading && <Spinner />}
      {list.error && <Alert onRetry={list.reload}>{list.error}</Alert>}
      {list.data && (list.data.items.length === 0 ? <EmptyState title="No vehicles found" /> : (
        <div className="card overflow-x-auto">
          <table className="table-base">
            <thead><tr><th>Vehicle</th><th>Registration</th><th>Status</th><th>Now</th><th className="text-right">Daily rate</th><th className="text-right">Odometer</th><th>Service</th><th className="text-right">Upcoming</th></tr></thead>
            <tbody>
              {list.data.items.map((v) => (
                <tr key={v.summary.id} className={v.archivedAt ? 'opacity-55' : ''}>
                  <td>
                    <Link to={`/admin/fleet/${v.summary.id}`} className="flex items-center gap-3 font-semibold hover:text-amber-700">
                      {v.summary.imageUrl && <img src={mediaUrl(v.summary.imageUrl)} alt="" className="h-9 w-14 rounded object-cover" />}
                      <span>{v.summary.displayName}<br /><span className="text-xs font-normal text-ink-500">{v.summary.category}{v.archivedAt ? ' · archived' : ''}</span></span>
                    </Link>
                  </td>
                  <td className="font-mono text-xs">{v.registrationNumber}</td>
                  <td><StatusBadge status={v.status} /></td>
                  <td><StatusBadge status={v.summary.currentState} /></td>
                  <td className="text-right">{money(v.summary.dailyRate, true)}</td>
                  <td className="text-right">{number(v.odometerKm)} km</td>
                  <td><StatusBadge status={v.serviceState} /></td>
                  <td className="text-right">{v.upcomingBookings}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}
      {list.data && <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />}
    </>
  )
}

const num = (min: number, max: number) => z.number({ error: 'Enter a number.' }).min(min).max(max)
const optNum = (min: number, max: number) => z.union([z.nan().transform(() => null), z.null(), num(min, max)])

const vehicleSchema = z.object({
  registrationNumber: z.string().trim().min(1, 'Required.').max(20).regex(/^[A-Za-z0-9 -]+$/, 'Letters, digits, spaces and hyphens only.'),
  make: z.string().trim().min(1, 'Required.').max(60),
  model: z.string().trim().min(1, 'Required.').max(80),
  year: num(1990, new Date().getFullYear() + 1),
  categoryId: num(1, 1_000_000),
  description: z.string().max(2000),
  seats: num(1, 15),
  doors: num(2, 6),
  luggageCapacity: num(0, 15),
  transmission: z.enum(['Manual', 'Automatic']),
  fuelType: z.enum(['Petrol', 'Diesel', 'Hybrid', 'Electric']),
  fuelEconomyLitresPer100Km: optNum(1, 30),
  comfortRating: num(1, 5),
  featuresText: z.string().max(2400),
  dailyRate: num(50, 100_000),
  dailyMileageLimitKm: optNum(10, 5000),
  excessMileageRatePerKm: optNum(0, 1000),
  depositAmount: num(0, 1_000_000),
  odometerKm: num(0, 2_000_000),
  serviceIntervalKm: num(1000, 100_000),
  nextServiceDueKm: optNum(0, 2_100_000),
  nextServiceDueDate: z.string(),
  videoUrl: z.string().max(500).refine((u) => !u || /^https:\/\//.test(u), 'Use an https link.'),
})
type VehicleValues = z.input<typeof vehicleSchema>
type VehicleOutput = z.output<typeof vehicleSchema>

export function VehicleEditPage() {
  const { id } = useParams()
  const vehicle = useAsync(() => (id ? api.admin.vehicle(id) : Promise.resolve(null)), [id])
  if (vehicle.loading) return <Spinner />
  if (vehicle.error) return <Alert onRetry={vehicle.reload}>{vehicle.error}</Alert>
  return <VehicleEditor key={id ?? 'new'} existing={vehicle.data ?? null} onChange={vehicle.reload} />
}

function VehicleEditor({ existing, onChange }: { existing: AdminVehicle | null; onChange: () => void }) {
  const navigate = useNavigate()
  const categories = useAsync(() => api.admin.categories(), [])
  const detail = useAsync(() => (existing ? api.catalog.vehicle(existing.summary.id).catch(() => null) : Promise.resolve(null)), [existing?.summary.id])
  const [message, setMessage] = useState<{ tone: 'success' | 'error'; text: string } | null>(null)
  const form = useForm<VehicleValues, unknown, VehicleOutput>({ resolver: zodResolver(vehicleSchema) })
  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = form

  useEffect(() => {
    const d = detail.data
    const s = existing?.summary
    reset({
      registrationNumber: existing?.registrationNumber ?? '', make: s?.make ?? '', model: s?.model ?? '', year: s?.year ?? new Date().getFullYear(),
      categoryId: categories.data?.find((c) => c.slug === s?.categorySlug)?.id ?? categories.data?.[0]?.id ?? 0,
      description: d?.description ?? '', seats: s?.seats ?? 5, doors: s?.doors ?? 4, luggageCapacity: s?.luggageCapacity ?? 2,
      transmission: (s?.transmission as 'Manual' | 'Automatic') ?? 'Manual', fuelType: (s?.fuelType as 'Petrol') ?? 'Petrol',
      fuelEconomyLitresPer100Km: d?.fuelEconomyLitresPer100Km ?? null, comfortRating: d?.comfortRating ?? 3, featuresText: d?.features.join('\n') ?? '',
      dailyRate: s?.dailyRate ?? 500, dailyMileageLimitKm: d?.dailyMileageLimitKm ?? 200, excessMileageRatePerKm: d?.excessMileageRatePerKm ?? 3,
      depositAmount: d?.depositAmount ?? 2500, odometerKm: existing?.odometerKm ?? 0, serviceIntervalKm: existing?.serviceIntervalKm ?? 15000,
      nextServiceDueKm: existing?.nextServiceDueKm ?? null, nextServiceDueDate: existing?.nextServiceDueDate?.slice(0, 10) ?? '', videoUrl: d?.videoUrl ?? '',
    })
  }, [existing, detail.data, categories.data, reset])

  const onSubmit = handleSubmit(async (v) => {
    setMessage(null)
    const { featuresText, nextServiceDueDate, ...rest } = v
    const body: UpsertVehicle = {
      ...rest,
      features: featuresText.split('\n').map((f) => f.trim()).filter(Boolean),
      nextServiceDueDate: nextServiceDueDate ? `${nextServiceDueDate}T00:00:00+02:00` : null,
      videoUrl: v.videoUrl || null,
    }
    try {
      if (existing) { await api.admin.updateVehicle(existing.summary.id, body); setMessage({ tone: 'success', text: 'Vehicle saved.' }); onChange() }
      else { const created = await api.admin.createVehicle(body); navigate(`/admin/fleet/${created.summary.id}`, { replace: true }) }
    } catch (e) {
      const text = applyServerErrors(e, form.setError, Object.keys(vehicleSchema.shape))
      if (text) setMessage({ tone: 'error', text })
    }
  })

  const numberField = (name: keyof VehicleValues, label: string, step = 1, hint?: string) => (
    <Field id={`v-${name}`} label={label} error={errors[name]?.message} hint={hint}>
      <input id={`v-${name}`} type="number" step={step} className="input" {...register(name, { valueAsNumber: true })} />
    </Field>
  )

  return (
    <>
      <PageHeader title={existing ? existing.summary.displayName : 'Add vehicle'} subtitle={existing ? <span className="inline-flex flex-wrap items-center gap-2">{existing.registrationNumber} <StatusBadge status={existing.status} /> <StatusBadge status={existing.summary.currentState} />{existing.summary.isSampleData && <SampleBadge />}</span> : 'New vehicles start as Available.'}
        actions={<Link to="/admin/fleet" className="btn-ghost">← Fleet</Link>} />
      <div className="grid gap-6 xl:grid-cols-[1.5fr_1fr]">
        <form className="card space-y-5 p-5" onSubmit={onSubmit} noValidate>
          <fieldset className="grid gap-4 sm:grid-cols-3">
            <legend className="mb-2 text-base font-extrabold">Vehicle</legend>
            <Field id="v-make" label="Make" error={errors.make?.message}><input id="v-make" className="input" {...register('make')} /></Field>
            <Field id="v-model" label="Model" error={errors.model?.message}><input id="v-model" className="input" {...register('model')} /></Field>
            {numberField('year', 'Year')}
            <Field id="v-reg" label="Registration" error={errors.registrationNumber?.message}><input id="v-reg" className="input uppercase" {...register('registrationNumber')} /></Field>
            <Field id="v-cat" label="Category" error={errors.categoryId?.message}>
              <select id="v-cat" className="input" {...register('categoryId', { valueAsNumber: true })}>{categories.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>
            </Field>
            {numberField('comfortRating', 'Comfort (1–5)')}
            <div className="sm:col-span-3"><Field id="v-desc" label="Description" error={errors.description?.message}><textarea id="v-desc" className="input min-h-20" {...register('description')} /></Field></div>
          </fieldset>
          <fieldset className="grid gap-4 sm:grid-cols-3">
            <legend className="mb-2 text-base font-extrabold">Specification</legend>
            {numberField('seats', 'Seats')}{numberField('doors', 'Doors')}{numberField('luggageCapacity', 'Luggage (bags)')}
            <Field id="v-tr" label="Gearbox" error={errors.transmission?.message}><select id="v-tr" className="input" {...register('transmission')}><option>Manual</option><option>Automatic</option></select></Field>
            <Field id="v-fuel" label="Fuel" error={errors.fuelType?.message}><select id="v-fuel" className="input" {...register('fuelType')}><option>Petrol</option><option>Diesel</option><option>Hybrid</option><option>Electric</option></select></Field>
            {numberField('fuelEconomyLitresPer100Km', 'L/100 km', 0.1, 'Leave blank for electric.')}
            <div className="sm:col-span-3"><Field id="v-feat" label="Features (one per line)" error={errors.featuresText?.message}><textarea id="v-feat" className="input min-h-20" {...register('featuresText')} /></Field></div>
          </fieldset>
          <fieldset className="grid gap-4 sm:grid-cols-3">
            <legend className="mb-2 text-base font-extrabold">Pricing (ZAR, VAT incl.)</legend>
            {numberField('dailyRate', 'Daily rate', 0.01)}{numberField('depositAmount', 'Deposit', 0.01)}{numberField('dailyMileageLimitKm', 'Free km per day', 1, 'Blank = unlimited')}
            {numberField('excessMileageRatePerKm', 'Excess per km', 0.01)}
          </fieldset>
          <fieldset className="grid gap-4 sm:grid-cols-3">
            <legend className="mb-2 text-base font-extrabold">Service</legend>
            {numberField('odometerKm', 'Odometer (km)')}{numberField('serviceIntervalKm', 'Service interval (km)')}{numberField('nextServiceDueKm', 'Next service at (km)')}
            <Field id="v-due" label="Next service date" error={errors.nextServiceDueDate?.message}><input id="v-due" type="date" className="input" {...register('nextServiceDueDate')} /></Field>
            <div className="sm:col-span-2"><Field id="v-video" label="Video link (https, optional)" error={errors.videoUrl?.message}><input id="v-video" className="input" {...register('videoUrl')} /></Field></div>
          </fieldset>
          {message && <Alert tone={message.tone}>{message.text}</Alert>}
          <button className="btn-primary" disabled={isSubmitting}>{isSubmitting ? 'Saving…' : existing ? 'Save changes' : 'Create vehicle'}</button>
        </form>

        {existing && (
          <div className="space-y-6">
            <StatusControls vehicle={existing} onChange={onChange} />
            <MediaManager vehicleId={existing.summary.id} media={detail.data?.media ?? []} onChange={detail.reload} />
            <History vehicleId={existing.summary.id} />
          </div>
        )}
      </div>
    </>
  )
}

function StatusControls({ vehicle, onChange }: { vehicle: AdminVehicle; onChange: () => void }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const act = async (fn: () => Promise<unknown>) => { setError(null); try { await fn(); onChange() } catch (e) { setError(errorMessage(e)) } }
  return (
    <section className="card p-5">
      <h2 className="text-base">Status</h2>
      <p className="mt-1 text-xs text-ink-500">Under maintenance or inactive vehicles cannot be booked. Archiving hides a vehicle but keeps its history.</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {['Available', 'UnderMaintenance', 'Inactive'].map((s) => (
          <button key={s} className={vehicle.status === s ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} disabled={vehicle.status === s || !!vehicle.archivedAt} onClick={() => act(() => api.admin.setVehicleStatus(vehicle.summary.id, s))}>{humanise(s)}</button>
        ))}
        {!vehicle.archivedAt && (
          <button className="btn-outline btn-sm text-rose-700" onClick={() => { if (window.confirm('Archive this vehicle? It will no longer appear to customers.')) act(async () => { await api.admin.archiveVehicle(vehicle.summary.id); navigate('/admin/fleet') }) }}><Archive className="size-3.5" />Archive</button>
        )}
      </div>
      {error && <div className="mt-3"><Alert>{error}</Alert></div>}
    </section>
  )
}

function MediaManager({ vehicleId, media, onChange }: { vehicleId: string; media: { id: string; url: string; kind: string; isPrimary: boolean; altText: string }[]; onChange: () => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [kind, setKind] = useState('Exterior')
  const [alt, setAlt] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const act = async (fn: () => Promise<unknown>) => { setBusy(true); setError(null); try { await fn(); onChange() } catch (e) { setError(errorMessage(e)) } finally { setBusy(false) } }

  return (
    <section className="card p-5">
      <h2 className="text-base">Images</h2>
      <div className="mt-3 grid grid-cols-3 gap-2">
        {media.map((m) => (
          <figure key={m.id} className="group relative overflow-hidden rounded-lg">
            <img src={mediaUrl(m.url)} alt={m.altText} className="aspect-[4/3] w-full object-cover" />
            <figcaption className="absolute inset-x-0 bottom-0 flex items-center justify-between bg-ink-950/70 px-1.5 py-1 text-[10px] text-white">
              {humanise(m.kind)}{m.isPrimary && ' · primary'}
              <span className="flex gap-1">
                {!m.isPrimary && <button aria-label="Make primary" title="Make primary" disabled={busy} onClick={() => act(() => api.admin.setPrimaryMedia(vehicleId, m.id))}><Star className="size-3.5" /></button>}
                <button aria-label="Delete image" title="Delete" disabled={busy} onClick={() => { if (window.confirm('Delete this image?')) act(() => api.admin.deleteMedia(vehicleId, m.id)) }}><Trash2 className="size-3.5" /></button>
              </span>
            </figcaption>
          </figure>
        ))}
      </div>
      <form className="mt-4 space-y-3 border-t border-ink-100 pt-4" onSubmit={(e) => { e.preventDefault(); if (file) act(async () => { await api.admin.uploadMedia(vehicleId, file, kind, alt); setFile(null); setAlt(''); (e.target as HTMLFormElement).reset() }) }}>
        <label htmlFor="m-file" className="label">Upload JPG, PNG or WebP (max 5 MB)</label>
        <input id="m-file" type="file" accept="image/jpeg,image/png,image/webp" className="block w-full text-sm" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        <div className="grid grid-cols-2 gap-2">
          <select aria-label="Image type" className="input" value={kind} onChange={(e) => setKind(e.target.value)}>{['Exterior', 'Interior', 'Dashboard', 'Boot'].map((k) => <option key={k}>{k}</option>)}</select>
          <input aria-label="Alt text" className="input" placeholder="Describe the image" value={alt} maxLength={200} onChange={(e) => setAlt(e.target.value)} />
        </div>
        <p className="text-xs text-ink-500">Only upload images you own or are licensed to use.</p>
        <button className="btn-dark btn-sm" disabled={!file || busy}><Upload className="size-3.5" />Upload</button>
      </form>
      {error && <div className="mt-3"><Alert>{error}</Alert></div>}
    </section>
  )
}

function History({ vehicleId }: { vehicleId: string }) {
  const h = useAsync(() => api.admin.vehicleHistory(vehicleId), [vehicleId])
  if (h.loading) return <Spinner />
  if (h.error || !h.data) return <Alert onRetry={h.reload}>{h.error}</Alert>
  const latest = h.data.mileage.reduce<(typeof h.data.mileage)[number] | null>((a, m) => (!a || m.recordedAt > a.recordedAt ? m : a), null)
  return (
    <section className="card p-5">
      <h2 className="text-base">History</h2>
      <h3 className="mt-3 text-xs font-bold uppercase text-ink-500">Recent bookings</h3>
      <ul className="mt-1 divide-y divide-ink-100 text-sm">
        {h.data.bookings.slice(0, 8).map((b) => <li key={b.id} className="flex justify-between gap-2 py-1.5"><span>{formatDate(b.pickupAt)} → {formatDate(b.returnAt)} <span className="text-ink-500">· {b.customerName}</span></span><StatusBadge status={b.status} /></li>)}
      </ul>
      <h3 className="mt-4 text-xs font-bold uppercase text-ink-500">Maintenance</h3>
      <ul className="mt-1 divide-y divide-ink-100 text-sm">
        {h.data.maintenance.slice(0, 8).map((m) => <li key={m.id} className="flex justify-between gap-2 py-1.5"><span>{formatDate(m.startAt)} · {m.title}{m.cost ? ` · ${money(m.cost)}` : ''}</span><StatusBadge status={m.status} /></li>)}
      </ul>
      {latest && <p className="mt-3 text-xs text-ink-500">Latest odometer reading: {number(latest.odometerKm)} km ({formatDateTime(latest.recordedAt)})</p>}
    </section>
  )
}
