import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from './api/client'
import type { Family, MapPoint, PerfumeDetail } from './api/types'
import { list, note } from './text'

type Vars = React.CSSProperties & Record<`--${string}`, string | number>

export default function Story() {
  const ref = useRef<HTMLElement>(null)
  const countRef = useRef<HTMLSpanElement>(null)
  const [points, setPoints] = useState<MapPoint[]>([])
  const [featured, setFeatured] = useState<PerfumeDetail | null>(null)
  const total = points.length

  useEffect(() => {
    let live = true
    api
      .points()
      .then((ps) => {
        if (!live || !ps.length) return
        setPoints(ps)
        const first = ps.reduce((a, b) => (b.id < a.id ? b : a))
        return api.detail(first.id).then((d) => {
          if (live) setFeatured(d)
        })
      })
      .catch(() => {})
    return () => {
      live = false
    }
  }, [])

  useEffect(() => {
    const el = ref.current
    if (!el) return
    let frame = 0
    const update = () => {
      frame = 0
      const r = el.getBoundingClientRect()
      const span = r.height - window.innerHeight
      const p = span > 0 ? Math.min(1, Math.max(0, -r.top / span)) : 1
      el.style.setProperty('--p', p.toFixed(4))
      if (countRef.current) {
        const t = window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 1 : Math.min(1, Math.max(0, (p - 0.64) / 0.22))
        countRef.current.textContent = Math.round(total * (1 - (1 - t) ** 3)).toLocaleString('en')
      }
    }
    const kick = () => {
      if (!frame) frame = requestAnimationFrame(update)
    }
    update()
    window.addEventListener('scroll', kick, { passive: true })
    window.addEventListener('resize', kick)
    return () => {
      cancelAnimationFrame(frame)
      window.removeEventListener('scroll', kick)
      window.removeEventListener('resize', kick)
    }
  }, [total])

  const sample = useMemo(() => points.filter((_, i) => i % Math.max(1, Math.ceil(points.length / 1200)) === 0), [points])
  const familyOf = useMemo(() => new Map(points.map((p) => [p.id, p.family])), [points])

  if (!total) return null

  const notes = featured?.notes
  const tiers: [string, string[]][] = !notes
    ? []
    : notes.heart.length || notes.base.length
      ? ([['Top', notes.top], ['Heart', notes.heart], ['Base', notes.base]] as [string, string[]][]).filter(([, n]) => n.length)
      : [['Notes', notes.top]]
  const neighbour = featured?.neighbours[0]
  const colour = (f: Family | undefined) => ({ '--c': `var(--fam-${f ?? 'woody'})` }) as Vars

  return (
    <section ref={ref} className="story" aria-label="How the map works">
      <div className="story-stage">
        <div className="chapter" style={{ '--s': -0.1, '--e': 0.34 } as Vars}>
          <h2 className="chapter-title">Every bottle breaks down into notes.</h2>
          <div className="pyramid">
            {featured && (
              <p className="pyramid-name">
                {featured.name}
                <span>{featured.house}</span>
              </p>
            )}
            {tiers.map(([label, notes], i) => (
              <div key={label} className="tier" style={{ '--t': 0.2 + i * 0.2 } as Vars}>
                <span className="tier-label">{label}</span>
                <span className="tier-notes">{list(notes.map(note))}</span>
              </div>
            ))}
          </div>
        </div>

        {featured && neighbour && (
          <div className="chapter" style={{ '--s': 0.33, '--e': 0.67 } as Vars}>
            <h2 className="chapter-title">Perfumes that share notes sit close together.</h2>
            <div className="pair">
              <span className="pair-dot from" style={colour(featured.family)}>
                <i />
                {featured.name}
              </span>
              <span className="pair-dot to" style={colour(familyOf.get(neighbour.id))}>
                <i />
                {neighbour.name}
              </span>
              {neighbour.sharedNotes.length > 0 && (
                <p className="pair-shared">Both carry {list(neighbour.sharedNotes.map(note))}.</p>
              )}
            </div>
          </div>
        )}

        <div className="chapter" style={{ '--s': 0.66, '--e': 1.1 } as Vars}>
          <h2 className="chapter-title"><span ref={countRef} className="count">{total.toLocaleString('en')}</span> perfumes. One map.</h2>
          <svg className="mini-map" viewBox="-20 -20 1040 1040" aria-hidden="true">
            {sample.map((p) => (
              <circle key={p.id} cx={p.x} cy={p.y} r={5} style={{ fill: `var(--fam-${p.family})` }} />
            ))}
          </svg>
        </div>
      </div>
    </section>
  )
}
