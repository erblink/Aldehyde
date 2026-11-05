import type { Family, MapPoint, Neighbour } from './api/types'
import { FAMILIES } from './families'

type Camera = { target: number[]; zoom: number }
type Size = { w: number; h: number }

export function familyAnchors(points: MapPoint[]) {
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

export function placeNameLabels(points: MapPoint[], skip: Set<number>, view: Camera, size: Size, fit: number, edge: number) {
  const depth = view.zoom - fit
  if (depth < 1) return []
  const max = Math.min(60, Math.round(6 * 2 ** depth))
  const scale = 2 ** view.zoom
  const taken: number[][] = []
  const out: { p: MapPoint; x: number; y: number }[] = []
  for (const p of points) {
    if (skip.has(p.id)) continue
    const x = (p.x - view.target[0]) * scale + size.w / 2
    const y = (p.y - view.target[1]) * scale + size.h / 2
    if (x < 0 || y < 70 || x > edge || y > size.h - 20 || (x < 200 && y > size.h - 230)) continue
    const box = [x - 4, y - 11, x + 12 + p.name.length * 6.6, y + 11]
    if (taken.some((b) => box[0] < b[2] && box[2] > b[0] && box[1] < b[3] && box[3] > b[1])) continue
    taken.push(box)
    out.push({ p, x, y })
    if (out.length >= max) break
  }
  return out
}

export function placeNearLabels(
  near: Neighbour[],
  byId: Map<number, MapPoint>,
  hidden: Set<Family>,
  project: (x: number, y: number) => [number, number],
) {
  const out: { n: Neighbour; x: number; y: number }[] = []
  for (const n of near) {
    const p = byId.get(n.id)
    if (!p || hidden.has(p.family)) continue
    const [x, py] = project(p.x, p.y)
    let y = py
    for (const o of out) if (Math.abs(o.x - x) < 240 && Math.abs(o.y - y) < 42) y = o.y + 42
    out.push({ n, x, y })
  }
  return out
}
