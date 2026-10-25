import type { RGB } from './families'

const BONE: RGB = [232, 226, 214]

function seeded(seed: number) {
  let s = seed | 0
  return () => {
    s = (s + 0x6d2b79f5) | 0
    let t = Math.imul(s ^ (s >>> 15), 1 | s)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

export function paintMist(canvas: HTMLCanvasElement, size: number, rgb: RGB, angle: number, seed: number) {
  const dpr = Math.min(2, window.devicePixelRatio || 1)
  const px = Math.max(1, Math.round(size * dpr))
  canvas.width = canvas.height = px
  const ctx = canvas.getContext('2d')
  if (!ctx) return

  const rand = seeded(seed)
  const gauss = () => {
    let u = 0
    while (!u) u = rand()
    return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * rand())
  }
  const tint = (k: number, a: number) =>
    `rgba(${rgb.map((c, i) => Math.round(c + (BONE[i] - c) * k)).join(',')},${a.toFixed(3)})`

  const length = px * 0.62
  const [dx, dy] = [Math.cos(angle), Math.sin(angle)]
  const [ox, oy] = [px / 2 - dx * length * 0.5, px / 2 - dy * length * 0.5]
  const at = (t: number, side: number) => [ox + (dx * t - dy * side) * length, oy + (dy * t + dx * side) * length]
  const spreadAt = (t: number) => 0.025 + 0.34 * Math.sqrt(t) + 0.12 * t ** 3
  const phase = rand() * Math.PI * 2
  const sway = (t: number) => 0.05 * t * Math.sin(t * 7 + phase) + 0.025 * Math.sin(t * 17 + phase * 2)
  const ease = (t: number) => Math.min(1, t / 0.09) ** 2

  for (let i = 0; i < 34; i++) {
    const t = 0.25 + rand() ** 0.7 * 0.7
    const [x, y] = at(t, sway(t) + gauss() * spreadAt(t) * 0.35)
    const r = length * (0.08 + 0.26 * t) * (0.7 + rand() * 0.6)
    const g = ctx.createRadialGradient(x, y, 0, x, y, r)
    g.addColorStop(0, tint(0.1, 0.04 * (0.4 + t * 0.6)))
    g.addColorStop(1, tint(0, 0))
    ctx.fillStyle = g
    ctx.fillRect(x - r, y - r, r * 2, r * 2)
  }

  const count = Math.round(Math.min(5200, Math.max(900, 2600 * (px / 420) ** 2)))
  for (let i = 0; i < count; i++) {
    const t = rand() ** 0.8
    const spread = spreadAt(t)
    const side = gauss() * spread * 0.5
    const edge = Math.exp(-((side / spread) ** 2) * 1.6)
    const alpha = ease(t) * (1 - t) ** 0.7 * edge * (0.2 + 0.8 * rand()) * 0.8
    if (alpha < 0.015) continue
    const [x, y] = at(t + gauss() * 0.012, side + sway(t))
    const r = (rand() < 0.02 ? 0.9 + rand() * 0.9 : 0.3 + rand() ** 2.2 * 0.75) * dpr
    ctx.fillStyle = tint(0.35 * (1 - t), alpha)
    ctx.beginPath()
    ctx.arc(x, y, r, 0, Math.PI * 2)
    ctx.fill()
  }

  for (let i = 0; i < count * 1.4; i++) {
    const t = 0.2 + rand() * 0.8
    const spread = spreadAt(t)
    const side = gauss() * spread * 0.55
    const alpha = (1 - t) ** 0.4 * Math.exp(-((side / spread) ** 2) * 1.2) * (0.1 + 0.25 * rand())
    if (alpha < 0.02) continue
    const [x, y] = at(t + gauss() * 0.02, side + sway(t))
    ctx.fillStyle = tint(0.2, alpha)
    ctx.fillRect(x, y, 0.7 * dpr, 0.7 * dpr)
  }
}
