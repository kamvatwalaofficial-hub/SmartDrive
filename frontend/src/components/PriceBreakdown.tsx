import type { PriceLine } from '../api/types'
import { money } from '../lib/format'

export function PriceBreakdown({ lines, total, vat, deposit, rentalDays }: { lines: PriceLine[]; total: number; vat: number; deposit?: number; rentalDays?: number }) {
  return (
    <div className="text-sm">
      <dl className="space-y-1.5">
        {lines.map((line, i) => (
          <div key={`${line.type}-${i}`} className="flex justify-between gap-4">
            <dt className="text-ink-700">{line.description}</dt>
            <dd className={`font-semibold ${line.amount < 0 ? 'text-leaf-700' : ''}`}>{money(line.amount)}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-3 flex items-baseline justify-between border-t border-ink-100 pt-3">
        <span className="font-bold">Total{rentalDays ? ` · ${rentalDays} day${rentalDays === 1 ? '' : 's'}` : ''}</span>
        <span className="text-xl font-extrabold">{money(total)}</span>
      </div>
      <p className="mt-1 text-right text-xs text-ink-500">Includes VAT of {money(vat)}</p>
      {deposit !== undefined && deposit > 0 && (
        <p className="mt-2 rounded-lg bg-sand-100 px-3 py-2 text-xs text-ink-700">A refundable deposit of <strong>{money(deposit)}</strong> is held at pickup (not charged in this demo).</p>
      )}
    </div>
  )
}
