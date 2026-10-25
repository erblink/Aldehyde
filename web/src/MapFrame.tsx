import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import DeckGL from '@deck.gl/react'
import { LinearInterpolator, OrthographicView, type PickingInfo } from '@deck.gl/core'
import { LineLayer, ScatterplotLayer } from '@deck.gl/layers'
import { api } from './api/client'
import type { Family, MapPoint, Neighbour, PerfumeDetail, SearchResult } from './api/types'
import Detail from './Detail'
import { FAMILIES } from './families'
import Search from './Search'
import { shares } from './text'

type RGB = [number, number, number]
type View = {
  target: [number, number] | [number, number, number]
  zoom: number
  transitionDuration?: number
  transitionInterpolator?: LinearInterpolator
}
type Line = { from: [number, number]; to: [number, number]; alpha: number }

const VIEW = new OrthographicView({ id: 'map' })
const FLY = new LinearInterpolator(['target', 'zoom'])
const BONE: RGB = [232, 226, 214]
const NARROW = 760
const NONE: Neighbour[] = []

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))
const fitZoom = (w: number, h: number) => Math.log2(Math.max(1, Math.min(w * 0.9, (h - 150) * 0.9)) / 1000)
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

function familyColours() {
  const css = getComputedStyle(document.documentElement)
  return Object.fromEntries(
    FAMILIES.map(({ id }) => {
      const hex = css.getPropertyValue(`--fam-${id}`).trim().slice(1)
      return [id, [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16))]
    }),
  ) as Record<Family, RGB>
}

function familyAnchors(points: MapPoint[]) {
  const cells = new Map<string, { family: Family; n: number; x: number; y: number }>()
  for (const p of points) {
    const key = `${p.family}:${Math.floor(p.x / 50)}:${Math.floor(p.y / 50)}`
    const c = cells.get(key) ?? { family: p.family, n: 0, x: 0, y: 0 }
    c.n++
    c.x += p.x
    c.y += p.y
    cells.set(key, c)
  }
  const best = new Map<Family, { n: number; x: number; y: number }>()
  for (const c of cells.values()) if (c.n > (best.get(c.family)?.n ?? 0)) best.set(c.family, c)
  return FAMILIES.filter((f) => best.has(f.id)).map((f) => {
    const c = best.get(f.id)!
    return { ...f, x: c.x / c.n, y: c.y / c.n }
  })
}

export default function MapFrame() {
  const stageRef = useRef<HTMLDivElement>(null)
  const detailRef = useRef<HTMLDivElement>(null)
  const [points, setPoints] = useState<MapPoint[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [hidden, setHidden] = useState<Set<Family>>(new Set())
  const [size, setSize] = useState({ w: 0, h: 0 })
  const [moved, setView] = useState<View | null>(null)
  const [inView, setInView] = useState(false)
  const [hovered, setHovered] = useState<number | null>(null)
  const [hoverDetail, setHoverDetail] = useState<PerfumeDetail | null>(null)
  const [selected, setSelected] = useState<number | null>(null)
  const [detail, setDetail] = useState<PerfumeDetail | { failedId: number } | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [detailHeight, setDetailHeight] = useState(0)
  const colours = useMemo(() => familyColours(), [])

  useEffect(() => {
    api.points().then(setPoints, () => setFailed(true))
  }, [])

  useLayoutEffect(() => {
    const el = stageRef.current!
    const ro = new ResizeObserver(([e]) => setSize({ w: e.contentRect.width, h: e.contentRect.height }))
    ro.observe(el)
    const io = new IntersectionObserver(([e]) => setInView(e.intersectionRatio > 0.9), { threshold: [0, 0.9, 1] })
    io.observe(el)
    return () => {
      ro.disconnect()
      io.disconnect()
    }
  }, [])

  const fit = size.w ? fitZoom(size.w, size.h) : 0
  const wide = size.w >= NARROW
  const view = useMemo<View | null>(() => moved ?? (size.w ? { target: [500, 500], zoom: fit } : null), [moved, size.w, fit])
  const current =
    selected == null || !detail
      ? null
      : 'failedId' in detail
        ? detail.failedId === selected ? 'failed' : null
        : detail.id === selected ? detail : null

  useEffect(() => {
    if (hovered == null || hovered === selected) return
    let live = true
    const t = setTimeout(() => api.detail(hovered).then((d) => live && setHoverDetail(d), () => {}), 150)
    return () => {
      live = false
      clearTimeout(t)
    }
  }, [hovered, selected])

  useEffect(() => {
    if (selected == null) return
    let live = true
    api.detail(selected).then(
      (d) => live && setDetail(d),
      () => live && setDetail({ failedId: selected }),
    )
    return () => {
      live = false
    }
  }, [selected, attempt])

  useLayoutEffect(() => {
    setDetailHeight(detailRef.current?.offsetHeight ?? 0)
    if (current && !wide) detailRef.current?.scrollIntoView({ block: 'start', behavior: reducedMotion() ? 'auto' : 'smooth' })
  }, [current, wide])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setSelected(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const byId = useMemo(() => new Map((points ?? []).map((p) => [p.id, p])), [points])
  const visible = useMemo(() => (points ?? []).filter((p) => !hidden.has(p.family)), [points, hidden])
  const byRank = useMemo(() => [...visible].sort((a, b) => a.id - b.id), [visible])
  const anchors = useMemo(() => familyAnchors(points ?? []), [points])

  const selPoint = selected != null ? byId.get(selected) : undefined
  const hovPoint = hovered != null ? byId.get(hovered) : undefined
  const selNeighbours: Neighbour[] = current && current !== 'failed' ? current.neighbours : NONE
  const hovNeighbours: Neighbour[] = hoverDetail && hoverDetail.id === hovered ? hoverDetail.neighbours : NONE
  const near = useMemo(() => selNeighbours.slice(0, 2), [selNeighbours])

  const lit = useMemo(() => {
    const ids = [selected, hovered, ...selNeighbours.map((n) => n.id), ...hovNeighbours.map((n) => n.id)]
    return new Set(ids.filter((id): id is number => id != null))
  }, [selected, hovered, selNeighbours, hovNeighbours])
  const litKey = [...lit].join(',')

  const reserved = useMemo(() => new Set([...lit, ...near.map((n) => n.id)]), [lit, near])

  const s = view ? 2 ** view.zoom : 1
  const project = (x: number, y: number): [number, number] =>
    view ? [(x - view.target[0]) * s + size.w / 2, (y - view.target[1]) * s + size.h / 2] : [0, 0]

  const nameLabels = useMemo(() => {
    if (!view || !size.w) return []
    const depth = view.zoom - fit
    if (depth < 1) return []
    const max = Math.min(60, Math.round(6 * 2 ** depth))
    const scale = 2 ** view.zoom
    const taken: number[][] = []
    const out: { p: MapPoint; x: number; y: number }[] = []
    for (const p of byRank) {
      if (reserved.has(p.id)) continue
      const x = (p.x - view.target[0]) * scale + size.w / 2
      const y = (p.y - view.target[1]) * scale + size.h / 2
      if (x < 0 || y < 70 || x > size.w - 90 || y > size.h - 20) continue
      const box = [x - 4, y - 11, x + 12 + p.name.length * 6.6, y + 11]
      if (taken.some((b) => box[0] < b[2] && box[2] > b[0] && box[1] < b[3] && box[3] > b[1])) continue
      taken.push(box)
      out.push({ p, x, y })
      if (out.length >= max) break
    }
    return out
  }, [view, size, fit, byRank, reserved])

  const lines = useMemo(() => {
    const out: Line[] = []
    const add = (from: MapPoint | undefined, ns: Neighbour[], alpha: number) => {
      if (!from) return
      for (const n of ns) {
        const to = byId.get(n.id)
        if (to && !hidden.has(to.family)) out.push({ from: [from.x, from.y], to: [to.x, to.y], alpha })
      }
    }
    add(selPoint, selNeighbours, 150)
    if (hovered !== selected) add(hovPoint, hovNeighbours, 70)
    if (selPoint) {
      out.push({ from: [selPoint.x, -4000], to: [selPoint.x, 5000], alpha: 26 })
      out.push({ from: [-4000, selPoint.y], to: [5000, selPoint.y], alpha: 26 })
    }
    return out
  }, [byId, hidden, selPoint, hovPoint, selNeighbours, hovNeighbours, hovered, selected])

  const radius = view ? clamp(2 * 1.3 ** (view.zoom - fit), 2, 5) : 2

  const layers = [
    new LineLayer<Line>({
      id: 'lines',
      data: lines,
      getSourcePosition: (l) => l.from,
      getTargetPosition: (l) => l.to,
      getColor: (l) => [...BONE, l.alpha],
      getWidth: 1,
      widthUnits: 'pixels',
    }),
    new ScatterplotLayer<MapPoint>({
      id: 'points',
      data: visible,
      getPosition: (p) => [p.x, p.y],
      radiusUnits: 'pixels',
      radiusScale: radius,
      getRadius: (p) => (lit.has(p.id) ? 1.6 : 1),
      getFillColor: (p) => [...colours[p.family], lit.size === 0 || lit.has(p.id) ? 235 : 70],
      pickable: true,
      updateTriggers: { getRadius: litKey, getFillColor: litKey },
    }),
    new ScatterplotLayer<MapPoint>({
      id: 'ring',
      data: selPoint && !hidden.has(selPoint.family) ? [selPoint] : [],
      getPosition: (p) => [p.x, p.y],
      radiusUnits: 'pixels',
      getRadius: radius * 1.6 + 7,
      stroked: true,
      filled: false,
      lineWidthUnits: 'pixels',
      getLineWidth: 1,
      getLineColor: BONE,
    }),
  ]

  const select = (id: number | null) => {
    setSelected(id)
    setAttempt(0)
  }

  const flyTo = (x: number, y: number) => {
    if (!view) return
    const zoom = Math.max(view.zoom, fit + 2)
    const offset = wide ? (size.w * 0.15) / 2 ** zoom : 0
    setView({
      target: [x + offset, y],
      zoom,
      transitionDuration: reducedMotion() ? 0 : 800,
      transitionInterpolator: FLY,
    })
  }

  const goTo = (id: number) => {
    const p = byId.get(id)
    if (!p) return
    if (hidden.has(p.family)) setHidden((h) => new Set([...h].filter((f) => f !== p.family)))
    select(id)
    flyTo(p.x, p.y)
  }

  const onPick = (r: SearchResult) => goTo(r.id)

  const toggle = (f: Family) =>
    setHidden((h) => {
      const next = new Set(h)
      if (!next.delete(f)) next.add(f)
      return next
    })

  let detailStyle: React.CSSProperties | undefined
  if (wide && selPoint) {
    const [sx, sy] = project(selPoint.x, selPoint.y)
    const width = Math.min(420, size.w * 0.34)
    const left = sx < size.w * 0.58 ? sx + 36 : sx - 36 - width
    detailStyle = {
      left: clamp(left, 24, size.w - width - 24),
      top: clamp(sy - 48, 96, Math.max(96, size.h - detailHeight - 130)),
      width,
    }
  }

  const status = failed
    ? "The map didn't load. Reload the page to try again."
    : points === null
      ? 'Loading the map.'
      : points.length === 0
        ? 'No perfumes placed yet. The map fills in once the catalogue is imported.'
        : null

  return (
    <section id="map" className="map-frame" aria-label="The map">
      <div ref={stageRef} className="map-stage">
        {view && (
          <DeckGL
            views={VIEW}
            viewState={{ ...view, minZoom: fit - 0.5, maxZoom: fit + 5 }}
            onViewStateChange={({ viewState: v }) => setView(v as View)}
            controller={{
              scrollZoom: inView,
              dragPan: true,
              doubleClickZoom: true,
              keyboard: true,
            }}
            touchAction={inView ? 'none' : 'pan-y'}
            layers={layers}
            pickingRadius={6}
            onHover={(info: PickingInfo<MapPoint>) => setHovered(info.object?.id ?? null)}
            onClick={(info: PickingInfo<MapPoint>) => select(info.object?.id ?? null)}
            getCursor={({ isDragging, isHovering }) => (isDragging ? 'grabbing' : isHovering ? 'pointer' : 'grab')}
          />
        )}

        <div className="map-labels" aria-hidden="true">
          {anchors
            .filter((a) => !hidden.has(a.id))
            .map((a) => {
              const [x, y] = project(a.x, a.y)
              return (
                <span key={a.id} className="family-label" style={{ transform: `translate(${x}px, ${y}px) translate(-50%, -50%)` }}>
                  {a.name}
                </span>
              )
            })}
          {nameLabels.map(({ p, x, y }) => (
            <span key={p.id} className="map-label" style={{ transform: `translate(${x + 8}px, ${y - 10}px)` }}>
              {p.name}
            </span>
          ))}
          {near.map((n) => {
            const p = byId.get(n.id)
            if (!p || hidden.has(p.family)) return null
            const [x, y] = project(p.x, p.y)
            return (
              <span key={n.id} className="map-label near" style={{ transform: `translate(${x + 10}px, ${y - 10}px)` }}>
                {n.name}
                <span className="map-label-aside">{shares(n.sharedNotes)}</span>
              </span>
            )
          })}
          {hovPoint && hovered !== selected && (
            <span
              className="map-label hover"
              style={{ transform: `translate(${project(hovPoint.x, hovPoint.y)[0] + 10}px, ${project(hovPoint.x, hovPoint.y)[1] - 10}px)` }}
            >
              {hovPoint.name}
              <span className="map-label-aside">{hovPoint.house}</span>
            </span>
          )}
        </div>

        <header>
          <a href="#" className="wordmark">
            scentmap
          </a>
          <Search onPick={onPick} />
        </header>

        {status && <p className="map-empty">{status}</p>}

        <ul className="family-key" aria-label="Scent families">
          {FAMILIES.map((f) => (
            <li key={f.id} data-family={f.id}>
              <button type="button" aria-pressed={!hidden.has(f.id)} onClick={() => toggle(f.id)}>
                <span className="swatch" aria-hidden="true" style={{ '--c': `var(--fam-${f.id})` } as React.CSSProperties} />
                {f.name}
              </button>
            </li>
          ))}
        </ul>
      </div>

      {current && (
        <div ref={detailRef} className="detail-place" style={detailStyle}>
          <Detail detail={current} onSelect={goTo} onRetry={() => setAttempt((a) => a + 1)} onClose={() => select(null)} />
        </div>
      )}

      <p className="data-note">
        Notes as published by the house. Distance comes from shared notes and accords, not from reviews.
      </p>
    </section>
  )
}
