import { useEffect, useRef } from 'react'
import type { Family } from './api/types'
import { familyColours } from './families'
import { paintMist } from './mist'

const DROPS: { id: Family; name: string; x: number; y: number; d: number; depth: number; angle: number }[] = [
  { id: 'citrus', name: 'Citrus', x: 0.6, y: 0.27, d: 0.3, depth: 0.6, angle: -0.55 },
  { id: 'green', name: 'Green', x: 0.72, y: 0.17, d: 0.22, depth: 1, angle: -0.85 },
  { id: 'aquatic', name: 'Aquatic', x: 0.85, y: 0.31, d: 0.27, depth: 0.4, angle: -0.35 },
  { id: 'floral', name: 'Floral', x: 0.68, y: 0.47, d: 0.25, depth: 0.8, angle: -0.62 },
  { id: 'woody', name: 'Woody', x: 0.88, y: 0.58, d: 0.2, depth: 1.2, angle: -0.28 },
  { id: 'amber', name: 'Amber', x: 0.78, y: 0.69, d: 0.24, depth: 0.5, angle: -0.48 },
]

const PARALLAX = 18
const KEY_STEP = 24

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v))
const lerp = (a: number, b: number, t: number) => a + (b - a) * t

export default function Drops() {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const mists = useRef<(HTMLCanvasElement | null)[]>([])

  useEffect(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)')
    const drag = DROPS.map(() => ({ x: 0, y: 0 }))
    const pointer = { x: 0, y: 0, tx: 0, ty: 0 }
    let active = -1
    let grab = { x: 0, y: 0 }
    let frame = 0
    const colours = familyColours()
    const painted = DROPS.map(() => 0)

    const kick = () => {
      if (!frame) frame = requestAnimationFrame(render)
    }

    function render() {
      frame = 0
      const opening = document.querySelector<HTMLElement>('.opening')
      if (!opening) return
      const w = window.innerWidth
      const h = opening.offsetHeight
      const narrow = w < 760
      const p = clamp(window.scrollY / h, 0, 1)
      const lift = reduced.matches ? 0 : p * h * 0.7

      pointer.x = lerp(pointer.x, pointer.tx, 0.12)
      pointer.y = lerp(pointer.y, pointer.ty, 0.12)
      const settling = Math.abs(pointer.x - pointer.tx) + Math.abs(pointer.y - pointer.ty) > 0.001

      DROPS.forEach((drop, i) => {
        const el = refs.current[i]
        if (!el) return
        const size = drop.d * 0.9 * Math.min(w, h) * (narrow ? 1.3 : 1)
        el.style.width = el.style.height = `${size}px`
        const mist = mists.current[i]
        if (mist && Math.abs(size - painted[i]) > 2) {
          paintMist(mist, size * 1.4, colours[drop.id], drop.angle, i * 7919 + 17)
          painted[i] = size
        }

        const fx = narrow ? 0.15 + (drop.x - 0.6) * 2.4 : drop.x
        const fy = narrow ? 0.1 + drop.y * 0.55 : drop.y
        const par = reduced.matches ? 0 : PARALLAX * drop.depth
        const hx = fx * w + drag[i].x + pointer.x * par
        const hy = fy * h + drag[i].y + pointer.y * par - lift * drop.depth

        el.style.transform = `translate(${hx - size / 2}px, ${hy - size / 2}px)`
        el.style.opacity = String(1 - p)
        el.style.visibility = p > 0.99 ? 'hidden' : 'visible'
        el.tabIndex = p > 0.5 ? -1 : 0
        el.classList.toggle('pulled', p > 0.15)
      })
      if (settling) kick()
    }

    const onPointerMove = (e: PointerEvent) => {
      if (active >= 0) {
        drag[active] = { x: e.clientX - grab.x, y: e.clientY - grab.y }
      } else if (e.pointerType === 'mouse') {
        pointer.tx = e.clientX / window.innerWidth - 0.5
        pointer.ty = e.clientY / window.innerHeight - 0.5
      }
      kick()
    }

    const cleanups = refs.current.map((el, i) => {
      if (!el) return () => {}
      const down = (e: PointerEvent) => {
        active = i
        grab = { x: e.clientX - drag[i].x, y: e.clientY - drag[i].y }
        el.setPointerCapture(e.pointerId)
        el.classList.add('held')
      }
      const up = () => {
        active = -1
        el.classList.remove('held')
      }
      const key = (e: KeyboardEvent) => {
        const step = e.shiftKey ? KEY_STEP * 3 : KEY_STEP
        const moves: Record<string, [number, number]> = {
          ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step],
        }
        const m = moves[e.key]
        if (!m) return
        e.preventDefault()
        drag[i] = { x: drag[i].x + m[0], y: drag[i].y + m[1] }
        kick()
      }
      el.addEventListener('pointerdown', down)
      el.addEventListener('pointerup', up)
      el.addEventListener('pointercancel', up)
      el.addEventListener('keydown', key)
      return () => {
        el.removeEventListener('pointerdown', down)
        el.removeEventListener('pointerup', up)
        el.removeEventListener('pointercancel', up)
        el.removeEventListener('keydown', key)
      }
    })

    window.addEventListener('pointermove', onPointerMove)
    window.addEventListener('scroll', kick, { passive: true })
    window.addEventListener('resize', kick)
    reduced.addEventListener('change', kick)
    kick()
    return () => {
      cancelAnimationFrame(frame)
      cleanups.forEach((c) => c())
      window.removeEventListener('pointermove', onPointerMove)
      window.removeEventListener('scroll', kick)
      window.removeEventListener('resize', kick)
      reduced.removeEventListener('change', kick)
    }
  }, [])

  return (
    <div className="drops">
      {DROPS.map((drop, i) => (
        <button
          key={drop.id}
          ref={(el) => {
            refs.current[i] = el
          }}
          type="button"
          className="drop"
          style={{ '--c': `var(--fam-${drop.id})`, '--i': i } as React.CSSProperties}
          aria-label={`${drop.name}. Drag, or use the arrow keys, to move it.`}
        >
          <canvas
            ref={(c) => {
              mists.current[i] = c
            }}
            className="mist"
            aria-hidden="true"
          />
          <span className="drop-name" aria-hidden="true">
            {drop.name}
          </span>
        </button>
      ))}
    </div>
  )
}
