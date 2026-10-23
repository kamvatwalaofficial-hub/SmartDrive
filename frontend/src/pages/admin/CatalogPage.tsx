import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { errorMessage } from '../../api/client'
import { api } from '../../api/endpoints'
import type { AdminLocation, Category } from '../../api/types'
import { Alert, Modal, PageHeader, Spinner, StatusBadge } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'

export default function CatalogPage() {
  const cats = useAsync(() => api.admin.categories(), [])
  const locs = useAsync(() => api.admin.locations(), [])
  const [cat, setCat] = useState<Category | 'new' | null>(null)
  const [loc, setLoc] = useState<AdminLocation | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)

  return (
    <>
      <PageHeader title="Categories & branches" />
      {error && <div className="mb-3"><Alert>{error}</Alert></div>}
      <div className="grid gap-6 xl:grid-cols-2">
        <section className="card p-5">
          <div className="flex items-center justify-between"><h2 className="text-base">Vehicle categories</h2><button className="btn-outline btn-sm" onClick={() => setCat('new')}><Plus className="size-3.5" />Add</button></div>
          {cats.loading && <Spinner />}
          <ul className="mt-3 divide-y divide-ink-100 text-sm">
            {cats.data?.map((c) => (
              <li key={c.id} className="flex items-center justify-between gap-2 py-2">
                <span><strong>{c.name}</strong> <span className="font-mono text-xs text-ink-500">/{c.slug}</span><br /><span className="text-xs text-ink-500">{c.vehicleCount} vehicles</span></span>
                <span className="flex gap-1">
                  <button aria-label={`Edit ${c.name}`} className="btn-ghost btn-sm" onClick={() => setCat(c)}><Pencil className="size-3.5" /></button>
                  <button aria-label={`Delete ${c.name}`} className="btn-ghost btn-sm text-rose-700" disabled={c.vehicleCount > 0} title={c.vehicleCount > 0 ? 'Move or archive its vehicles first' : 'Delete'}
                    onClick={async () => { if (!window.confirm(`Delete ${c.name}?`)) return; setError(null); try { await api.admin.deleteCategory(c.id); cats.reload() } catch (e) { setError(errorMessage(e)) } }}><Trash2 className="size-3.5" /></button>
                </span>
              </li>
            ))}
          </ul>
        </section>
        <section className="card p-5">
          <div className="flex items-center justify-between"><h2 className="text-base">Branches</h2><button className="btn-outline btn-sm" onClick={() => setLoc('new')}><Plus className="size-3.5" />Add</button></div>
          {locs.loading && <Spinner />}
          <ul className="mt-3 divide-y divide-ink-100 text-sm">
            {locs.data?.map((l) => (
              <li key={l.id} className="flex items-center justify-between gap-2 py-2">
                <span><strong>{l.name}</strong> <span className="font-mono text-xs text-ink-500">{l.code}</span> {!l.isActive && <StatusBadge status="Inactive" />}<br /><span className="text-xs text-ink-500">{l.addressLine}, {l.city}, {l.province}{l.isAirport ? ' · airport' : ''}</span></span>
                <button aria-label={`Edit ${l.name}`} className="btn-ghost btn-sm" onClick={() => setLoc(l)}><Pencil className="size-3.5" /></button>
              </li>
            ))}
          </ul>
        </section>
      </div>
      {cat && <CategoryModal value={cat} onClose={() => setCat(null)} onSaved={() => { setCat(null); cats.reload() }} />}
      {loc && <LocationModal value={loc} onClose={() => setLoc(null)} onSaved={() => { setLoc(null); locs.reload() }} />}
    </>
  )
}

function CategoryModal({ value, onClose, onSaved }: { value: Category | 'new'; onClose: () => void; onSaved: () => void }) {
  const existing = value === 'new' ? null : value
  const [v, setV] = useState({ name: existing?.name ?? '', slug: existing?.slug ?? '', description: existing?.description ?? '', sortOrder: 0 })
  const [error, setError] = useState<string | null>(null)
  return (
    <Modal open title={existing ? `Edit ${existing.name}` : 'New category'} onClose={onClose}>
      <form className="space-y-3" onSubmit={async (e) => { e.preventDefault(); setError(null); try { await api.admin.saveCategory(existing?.id ?? null, v); onSaved() } catch (err) { setError(errorMessage(err)) } }}>
        <div><label htmlFor="cat-name" className="label">Name</label><input id="cat-name" required maxLength={60} className="input" value={v.name} onChange={(e) => setV({ ...v, name: e.target.value, slug: existing ? v.slug : e.target.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') })} /></div>
        <div><label htmlFor="cat-slug" className="label">Slug</label><input id="cat-slug" required pattern="[a-z0-9-]+" maxLength={40} className="input font-mono" value={v.slug} onChange={(e) => setV({ ...v, slug: e.target.value })} /></div>
        <div><label htmlFor="cat-desc" className="label">Description</label><textarea id="cat-desc" maxLength={500} className="input" value={v.description} onChange={(e) => setV({ ...v, description: e.target.value })} /></div>
        <div><label htmlFor="cat-sort" className="label">Sort order</label><input id="cat-sort" type="number" min={0} max={1000} className="input" value={v.sortOrder} onChange={(e) => setV({ ...v, sortOrder: Number(e.target.value) })} /></div>
        {error && <Alert>{error}</Alert>}
        <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary">Save</button></div>
      </form>
    </Modal>
  )
}

function LocationModal({ value, onClose, onSaved }: { value: AdminLocation | 'new'; onClose: () => void; onSaved: () => void }) {
  const existing = value === 'new' ? null : value
  const [v, setV] = useState({ name: existing?.name ?? '', code: existing?.code ?? '', addressLine: existing?.addressLine ?? '', city: existing?.city ?? '', province: existing?.province ?? 'Gauteng', isAirport: existing?.isAirport ?? false, isActive: existing?.isActive ?? true })
  const [error, setError] = useState<string | null>(null)
  const text = (key: 'name' | 'code' | 'addressLine' | 'city', label: string, max: number) => (
    <div><label htmlFor={`loc-${key}`} className="label">{label}</label><input id={`loc-${key}`} required maxLength={max} className="input" value={v[key]} onChange={(e) => setV({ ...v, [key]: e.target.value })} /></div>
  )
  return (
    <Modal open title={existing ? `Edit ${existing.name}` : 'New branch'} onClose={onClose}>
      <form className="space-y-3" onSubmit={async (e) => { e.preventDefault(); setError(null); try { await api.admin.saveLocation(existing?.id ?? null, v); onSaved() } catch (err) { setError(errorMessage(err)) } }}>
        {text('name', 'Name', 120)}{text('code', 'Code', 10)}{text('addressLine', 'Address', 200)}{text('city', 'City', 80)}
        <div><label htmlFor="loc-prov" className="label">Province</label><select id="loc-prov" className="input" value={v.province} onChange={(e) => setV({ ...v, province: e.target.value })}>{['Eastern Cape', 'Free State', 'Gauteng', 'KwaZulu-Natal', 'Limpopo', 'Mpumalanga', 'North West', 'Northern Cape', 'Western Cape'].map((p) => <option key={p}>{p}</option>)}</select></div>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="size-4 accent-amber-600" checked={v.isAirport} onChange={(e) => setV({ ...v, isAirport: e.target.checked })} />Airport branch</label>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="size-4 accent-amber-600" checked={v.isActive} onChange={(e) => setV({ ...v, isActive: e.target.checked })} />Open for bookings</label>
        {error && <Alert>{error}</Alert>}
        <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary">Save</button></div>
      </form>
    </Modal>
  )
}
