import { useEffect, useEffectEvent, useLayoutEffect, useMemo, useRef, useState } from 'react'
import DeckGL from '@deck.gl/react'
import { LinearInterpolator, OrthographicView, type PickingInfo } from '@deck.gl/core'
import { LineLayer, ScatterplotLayer } from '@deck.gl/layers'
import { api } from './api/client'
import type { Family, MapPoint, Neighbour, PerfumeDetail, SearchResult } from './api/types'
import Detail from './Detail'
import { FAMILIES, familyColours, type RGB } from './families'
import { familyAnchors, placeNameLabels, placeNearLabels } from './mapLabels'
import Search from './Search'
import Suggest from './Suggest'
import { shares } from './text'

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
const COLUMN = 0.58
const NONE: Neighbour[] = []

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))
const fitZoom = (w: number, h: number) => Math.log2(Math.max(1, Math.min(w * 0.92, h - 130)) / 1000)
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches
const coarse = () => window.matchMedia('(pointer: coarse)').matches

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
  const [home, setHome] = useState<View | null>(null)
  const [revealed, setRevealed] = useState(false)
  const [settled, setSettled] = useState(false)
  const colours = useMemo(() => familyColours(), [])

  useEffect(() => {
    api.points().then(setPoints, () => setFailed(true))
  }, [])

  useLayoutEffect(() => {
    const el = stageRef.current!
    const ro = new ResizeObserver(([e]) => setSize({ w: e.contentRect.width, h: e.contentRect.height }))
    ro.observe(el)
    const io = new IntersectionObserver(
      ([e]) => {
        setInView(e.intersectionRatio > 0.9)
        if (e.intersectionRatio > 0.25) setRevealed(true)
      },
      { threshold: [0, 0.25, 0.9, 1] },
    )
    io.observe(el)
    return () => {
      ro.disconnect()
      io.disconnect()
    }
  }, [])

  useEffect(() => {
    if (!revealed) return
    const t = setTimeout(() => setSettled(true), 1500)
    return () => clearTimeout(t)
  }, [revealed])

  const fit = size.w ? fitZoom(size.w, size.h) : 0
  const wide = size.w >= NARROW
  const split = wide && selected != null
  const edge = split ? size.w * COLUMN : size.w - 90
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

  useEffect(() => {
    if (current && !wide) detailRef.current?.scrollIntoView({ block: 'start', behavior: reducedMotion() ? 'auto' : 'smooth' })
  }, [current, wide])

  const byId = useMemo(() => new Map((points ?? []).map((p) => [p.id, p])), [points])
  const visible = useMemo(() => (points ?? []).filter((p) => !hidden.has(p.family)), [points, hidden])
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

  const nameLabels = useMemo(
    () => (view && size.w ? placeNameLabels(visible, reserved, view, size, fit, edge) : []),
    [view, size, fit, visible, reserved, edge],
  )

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

  const radius = view ? clamp(1.7 * 1.3 ** (view.zoom - fit), 1.7, 4.5) : 1.7

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
      getRadius: (p) => (!revealed ? 0 : lit.has(p.id) ? 1.6 : 1),
      getFillColor: (p) => [...colours[p.family], lit.size === 0 || lit.has(p.id) ? 215 : 85],
      pickable: true,
      updateTriggers: { getRadius: [litKey, revealed], getFillColor: litKey },
      transitions: {
        getRadius: reducedMotion() ? 0 : settled ? 180 : { duration: 1400, easing: (t: number) => 1 - (1 - t) ** 3 },
      },
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
    if (id != null && selected == null && view) setHome({ target: view.target, zoom: view.zoom })
    if (id == null && home) {
      setView({ ...home, transitionDuration: reducedMotion() ? 0 : 500, transitionInterpolator: FLY })
      setHome(null)
    }
    setSelected(id)
    setAttempt(0)
  }
  const onEscape = useEffectEvent(() => select(null))
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onEscape()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const offsetAt = (zoom: number) => (wide ? (size.w * (0.5 - COLUMN / 2)) / 2 ** zoom : 0)

  const flyTo = (x: number, y: number, zoomIn: boolean) => {
    if (!view) return
    const zoom = zoomIn ? Math.max(view.zoom, fit + 2) : view.zoom
    setView({
      target: [x + offsetAt(zoom), y],
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
    flyTo(p.x, p.y, true)
  }

  const onMapClick = (p: MapPoint | undefined) => {
    select(p?.id ?? null)
    if (p && wide && project(p.x, p.y)[0] > size.w * (COLUMN - 0.04)) flyTo(p.x, p.y, false)
  }

  const onPick = (r: SearchResult) => goTo(r.id)

  const toggle = (f: Family) =>
    setHidden((h) => {
      const next = new Set(h)
      if (!next.delete(f)) next.add(f)
      return next
    })

  const nearLabels = placeNearLabels(near, byId, hidden, project)

  const status = failed
    ? "The map didn't load. Reload the page to try again."
    : points === null
      ? 'Loading the map.'
      : points.length === 0
        ? 'No perfumes placed yet. The map fills in once the catalogue is imported.'
        : null

  return (
    <section id="map" className="map-frame" aria-label="The map">
      <div ref={stageRef} className={['map-stage', split && 'split', revealed && 'revealed'].filter(Boolean).join(' ')}>
        {view && (
          <div className="map-canvas">
          <DeckGL
            views={VIEW}
            viewState={{ ...view, minZoom: fit - 0.5, maxZoom: fit + 5 }}
            onViewStateChange={({ viewState, interactionState }) => {
              const v = viewState as View
              if (interactionState.inTransition) return setView(v)
              const m = offsetAt(v.zoom)
              setView({ ...v, target: [clamp(v.target[0], -m, 1000 + m), clamp(v.target[1], 0, 1000)] })
            }}
            controller={{
              scrollZoom: inView,
              dragPan: inView || !coarse(),
              doubleClickZoom: true,
              keyboard: true,
            }}
            touchAction={inView ? 'none' : 'pan-y'}
            layers={layers}
            pickingRadius={coarse() ? 14 : 6}
            useDevicePixels={Math.min(2, window.devicePixelRatio || 1)}
            onHover={(info: PickingInfo<MapPoint>) => setHovered(info.object?.id ?? null)}
            onClick={(info: PickingInfo<MapPoint>) => onMapClick(info.object)}
            getCursor={({ isDragging, isHovering }) => (isDragging ? 'grabbing' : isHovering ? 'pointer' : 'grab')}
          />
          </div>
        )}

        <div className="map-labels" aria-hidden="true">
          {anchors
            .filter((a) => !hidden.has(a.id))
            .map((a) => {
              const [x, y] = project(a.x, a.y)
              if (y < 84 || y > size.h - 24) return null
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
          {nearLabels.map(({ n, x, y }) => (
            <span key={n.id} className="map-label near" style={{ transform: `translate(${x + 10}px, ${y - 10}px)` }}>
              {n.name}
              <span className="map-label-aside">{shares(n.sharedNotes)}</span>
            </span>
          ))}
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
            Aldehyde
          </a>
          <div className="map-tools">
            <Search onPick={onPick} />
            <Suggest onPick={goTo} />
          </div>
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
        <div ref={detailRef} className="detail-place">
          <Detail key={current === 'failed' ? 'failed' : current.id} detail={current} onSelect={goTo} onRetry={() => setAttempt((a) => a + 1)} onClose={() => select(null)} />
        </div>
      )}

      <p className="data-note">
        Notes as published by the house. Distance comes from shared notes and accords, not from reviews.
      </p>
    </section>
  )
}
