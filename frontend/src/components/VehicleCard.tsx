import { Briefcase, Fuel, Gauge, Users } from 'lucide-react'
import { Link } from 'react-router'
import type { VehicleSummary } from '../api/types'
import { humanise, mediaUrl, money } from '../lib/format'
import { SampleBadge, Stars, StatusBadge } from './ui'

export function VehicleCard({ vehicle, query = '' }: { vehicle: VehicleSummary; query?: string }) {
  const unavailable = vehicle.availableForPeriod === false
  return (
    <article className="card group flex flex-col overflow-hidden transition hover:-translate-y-0.5 hover:shadow-lg">
      <Link to={`/vehicles/${vehicle.id}${query ? `?${query}` : ''}`} className="relative block aspect-[16/10] overflow-hidden bg-sand-100">
        {vehicle.imageUrl ? (
          <img src={mediaUrl(vehicle.imageUrl)} alt={vehicle.imageAlt ?? vehicle.displayName} loading="lazy"
            className="size-full object-cover transition duration-500 group-hover:scale-[1.03]" />
        ) : <div className="grid size-full place-items-center text-sm text-ink-400">No image</div>}
        <span className="absolute left-3 top-3 chip bg-white/90 text-ink-800 shadow-sm">{vehicle.category}</span>
        {vehicle.availableForPeriod !== null && (
          <span className={`absolute right-3 top-3 chip shadow-sm ${unavailable ? 'bg-rose-50 text-rose-700' : 'bg-leaf-50 text-leaf-700'}`}>
            {unavailable ? 'Unavailable for your dates' : 'Available for your dates'}
          </span>
        )}
      </Link>
      <div className="flex flex-1 flex-col p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="truncate text-base">
              <Link to={`/vehicles/${vehicle.id}${query ? `?${query}` : ''}`} className="hover:text-amber-700">{vehicle.displayName}</Link>
            </h3>
            <div className="mt-1 flex items-center gap-1.5 text-xs text-ink-500">
              <Stars value={vehicle.averageRating} size={12} />
              <span>{vehicle.reviewCount ? `${vehicle.averageRating?.toFixed(1)} · ${vehicle.reviewCount} reviews` : 'No reviews yet'}</span>
            </div>
          </div>
          <div className="text-right">
            <p className="text-lg font-extrabold leading-none">{money(vehicle.dailyRate, true)}</p>
            <p className="mt-0.5 text-[11px] text-ink-500">per day, VAT incl.</p>
          </div>
        </div>
        <ul className="mt-4 grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs text-ink-700">
          <li className="flex items-center gap-1.5"><Users className="size-3.5 text-ink-400" aria-hidden />{vehicle.seats} seats</li>
          <li className="flex items-center gap-1.5"><Briefcase className="size-3.5 text-ink-400" aria-hidden />{vehicle.luggageCapacity} bags</li>
          <li className="flex items-center gap-1.5"><Gauge className="size-3.5 text-ink-400" aria-hidden />{humanise(vehicle.transmission)}</li>
          <li className="flex items-center gap-1.5"><Fuel className="size-3.5 text-ink-400" aria-hidden />{humanise(vehicle.fuelType)}</li>
        </ul>
        <div className="mt-auto flex items-center justify-between gap-2 pt-4">
          {vehicle.availableForPeriod === null ? <StatusBadge status={vehicle.currentState} /> : <span />}
          {vehicle.isSampleData && <SampleBadge />}
        </div>
      </div>
    </article>
  )
}
