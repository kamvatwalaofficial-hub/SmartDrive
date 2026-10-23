import { Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { ReactNode } from 'react'

// Categorical slots in fixed order (validated reference palette); never cycled or reassigned by rank.
export const SERIES = ['#2a78d6', '#eb6834', '#1baf7a'] as const
export const SEQUENTIAL = '#2a78d6'
const GRID = '#e9edf2'
const AXIS = '#5b6b82'

const axisProps = { tick: { fill: AXIS, fontSize: 11 }, tickLine: false, axisLine: { stroke: GRID } } as const

export function ChartCard({ title, subtitle, children, table, actions }: { title: string; subtitle?: ReactNode; children: ReactNode; table?: ReactNode; actions?: ReactNode }) {
  return (
    <section className="card p-4 sm:p-5">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-base">{title}</h2>
          {subtitle && <p className="mt-0.5 text-xs text-ink-500">{subtitle}</p>}
        </div>
        {actions}
      </div>
      {children}
      {table && (
        <details className="mt-3 text-xs">
          <summary className="cursor-pointer font-semibold text-ink-500">Show data table</summary>
          <div className="mt-2 max-h-64 overflow-auto">{table}</div>
        </details>
      )}
    </section>
  )
}

function TooltipBox({ active, payload, label, format }: { active?: boolean; payload?: { name?: string; value?: number; color?: string }[]; label?: string; format: (v: number) => string }) {
  if (!active || !payload?.length) return null
  return (
    <div className="rounded-lg border border-ink-100 bg-white px-3 py-2 text-xs shadow-lg">
      <p className="mb-1 font-bold text-ink-900">{label}</p>
      {payload.map((p) => (
        <p key={p.name} className="flex items-center gap-2 text-ink-700">
          {payload.length > 1 && <span className="inline-block size-2 rounded-full" style={{ background: p.color }} aria-hidden />}
          <span>{p.name}</span><span className="ml-auto pl-3 font-semibold text-ink-900">{format(Number(p.value))}</span>
        </p>
      ))}
    </div>
  )
}

export interface SeriesDef { key: string; name: string; dashed?: boolean; colour?: string }

export function SimpleLine({ data, xKey, series, height = 240, format = String }: { data: object[]; xKey: string; series: SeriesDef[]; height?: number; format?: (v: number) => string }) {
  return (
    <div style={{ height }} role="img" aria-label={`Line chart of ${series.map((s) => s.name).join(' and ')}`}>
      <ResponsiveContainer>
        <LineChart data={data} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
          <CartesianGrid stroke={GRID} vertical={false} />
          <XAxis dataKey={xKey} {...axisProps} minTickGap={24} />
          <YAxis {...axisProps} width={56} tickFormatter={(v) => format(Number(v))} />
          <Tooltip content={<TooltipBox format={format} />} cursor={{ stroke: AXIS, strokeDasharray: '3 3' }} />
          {series.length > 1 && <Legend iconType="plainline" wrapperStyle={{ fontSize: 12, color: AXIS }} />}
          {series.map((s, i) => (
            <Line key={s.key} type="monotone" dataKey={s.key} name={s.name} stroke={s.colour ?? SERIES[i % SERIES.length]} strokeWidth={2}
              strokeDasharray={s.dashed ? '6 4' : undefined} dot={false} activeDot={{ r: 4 }} connectNulls={false} isAnimationActive={false} />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}

export function SimpleBar({ data, xKey, series, height = 240, format = String, horizontal = false }: { data: object[]; xKey: string; series: SeriesDef[]; height?: number; format?: (v: number) => string; horizontal?: boolean }) {
  return (
    <div style={{ height }} role="img" aria-label={`Bar chart of ${series.map((s) => s.name).join(' and ')}`}>
      <ResponsiveContainer>
        <BarChart data={data} layout={horizontal ? 'vertical' : 'horizontal'} margin={{ top: 8, right: 12, left: 0, bottom: 0 }} barGap={2}>
          <CartesianGrid stroke={GRID} vertical={horizontal} horizontal={!horizontal} />
          {horizontal ? (
            <>
              <XAxis type="number" {...axisProps} tickFormatter={(v) => format(Number(v))} />
              <YAxis type="category" dataKey={xKey} {...axisProps} width={120} />
            </>
          ) : (
            <>
              <XAxis dataKey={xKey} {...axisProps} minTickGap={8} />
              <YAxis {...axisProps} width={56} tickFormatter={(v) => format(Number(v))} />
            </>
          )}
          <Tooltip content={<TooltipBox format={format} />} cursor={{ fill: 'rgb(15 27 45 / 0.05)' }} />
          {series.length > 1 && <Legend wrapperStyle={{ fontSize: 12, color: AXIS }} />}
          {series.map((s, i) => (
            <Bar key={s.key} dataKey={s.key} name={s.name} fill={s.colour ?? (series.length === 1 ? SEQUENTIAL : SERIES[i % SERIES.length])}
              radius={horizontal ? [0, 4, 4, 0] : [4, 4, 0, 0]} maxBarSize={36} stroke="#fff" strokeWidth={1} isAnimationActive={false} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

export function DataTable({ headers, rows }: { headers: string[]; rows: (string | number)[][] }) {
  return (
    <table className="table-base">
      <thead><tr>{headers.map((h) => <th key={h}>{h}</th>)}</tr></thead>
      <tbody>{rows.map((r, i) => <tr key={i}>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody>
    </table>
  )
}
