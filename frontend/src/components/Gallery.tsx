import { Box, ChevronLeft, ChevronRight, Expand, X } from 'lucide-react'
import { lazy, Suspense, useEffect, useState } from 'react'
import type { Media } from '../api/types'
import { humanise, mediaUrl } from '../lib/format'
import { Spinner } from './ui'

const Viewer3D = lazy(() => import('./Viewer3D'))

export function Gallery({ media, name, modelUrl, modelAttribution }: { media: Media[]; name: string; modelUrl: string | null; modelAttribution: string | null }) {
  const images = media.filter((m) => m.kind !== 'Video')
  const [index, setIndex] = useState(Math.max(0, images.findIndex((m) => m.isPrimary)))
  const [fullscreen, setFullscreen] = useState(false)
  const [show3d, setShow3d] = useState(false)
  const current = images[index]

  const count = images.length
  const step = (delta: number) => setIndex((i) => (i + delta + count) % count)

  useEffect(() => {
    if (!fullscreen) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setFullscreen(false)
      if (e.key === 'ArrowRight') setIndex((i) => (i + 1) % count)
      if (e.key === 'ArrowLeft') setIndex((i) => (i - 1 + count) % count)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [fullscreen, count])

  if (images.length === 0) return <div className="grid aspect-[16/10] place-items-center rounded-2xl bg-sand-100 text-ink-400">No images yet</div>

  return (
    <div>
      <div className="mb-2 flex gap-2" role="tablist" aria-label="Viewer mode">
        <button role="tab" aria-selected={!show3d} className={!show3d ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} onClick={() => setShow3d(false)}>Photos</button>
        <button role="tab" aria-selected={show3d} className={show3d ? 'btn-dark btn-sm' : 'btn-outline btn-sm'} onClick={() => setShow3d(true)}>
          <Box className="size-3.5" aria-hidden />3D view
        </button>
      </div>

      {show3d ? (
        <div>
          <Suspense fallback={<Spinner label="Loading 3D viewer…" />}>
            <Viewer3D modelUrl={modelUrl} label={`3D view of ${modelUrl ? name : 'a generic demo car'}`} />
          </Suspense>
          <p className="mt-2 text-xs text-ink-500">
            {modelUrl ? (modelAttribution ?? '3D model supplied by SmartDrive.') : 'Demo 3D model: a generic car shape for illustration only — it is not a model of this vehicle.'}
          </p>
        </div>
      ) : (
        <>
          <div className="group relative overflow-hidden rounded-2xl bg-sand-100">
            <img src={mediaUrl(current.url)} alt={current.altText || `${name} — ${humanise(current.kind)}`} className="aspect-[16/10] w-full object-cover" />
            <button className="btn absolute right-3 top-3 bg-white/90 text-ink-900 shadow hover:bg-white" onClick={() => setFullscreen(true)}>
              <Expand className="size-4" aria-hidden />Full screen
            </button>
            {images.length > 1 && (
              <>
                <button aria-label="Previous image" className="absolute left-3 top-1/2 -translate-y-1/2 rounded-full bg-white/90 p-2 shadow" onClick={() => step(-1)}><ChevronLeft className="size-5" /></button>
                <button aria-label="Next image" className="absolute right-3 top-1/2 -translate-y-1/2 rounded-full bg-white/90 p-2 shadow" onClick={() => step(1)}><ChevronRight className="size-5" /></button>
              </>
            )}
            {current.attribution && <p className="absolute bottom-2 left-3 rounded bg-white/85 px-2 py-0.5 text-[11px] text-ink-700">{current.attribution}</p>}
          </div>
          <div className="mt-3 flex gap-2 overflow-x-auto pb-1">
            {images.map((m, i) => (
              <button key={m.id} onClick={() => setIndex(i)} aria-label={`Show ${humanise(m.kind).toLowerCase()} image`} aria-current={i === index}
                className={`shrink-0 overflow-hidden rounded-lg ring-2 transition ${i === index ? 'ring-amber-500' : 'ring-transparent opacity-75 hover:opacity-100'}`}>
                <img src={mediaUrl(m.url)} alt="" className="h-16 w-24 object-cover" loading="lazy" />
              </button>
            ))}
          </div>
        </>
      )}

      {fullscreen && (
        <div role="dialog" aria-modal="true" aria-label={`${name} images`} className="fixed inset-0 z-50 flex flex-col bg-ink-950/95">
          <div className="flex items-center justify-between p-4 text-white">
            <p className="text-sm font-semibold">{name} · {humanise(current.kind)} · {index + 1}/{images.length}</p>
            <button aria-label="Close full screen" className="rounded-full p-2 hover:bg-white/10" onClick={() => setFullscreen(false)} autoFocus><X className="size-6" /></button>
          </div>
          <div className="relative flex flex-1 items-center justify-center px-4 pb-6">
            <img src={mediaUrl(current.url)} alt={current.altText || name} className="max-h-full max-w-full rounded-lg object-contain" />
            {images.length > 1 && (
              <>
                <button aria-label="Previous image" className="absolute left-4 rounded-full bg-white/15 p-3 text-white hover:bg-white/25" onClick={() => step(-1)}><ChevronLeft className="size-6" /></button>
                <button aria-label="Next image" className="absolute right-4 rounded-full bg-white/15 p-3 text-white hover:bg-white/25" onClick={() => step(1)}><ChevronRight className="size-6" /></button>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
