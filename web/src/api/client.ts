import type { MapPoint, PerfumeDetail, SearchResult, Suggestion } from './types'

const base = import.meta.env.VITE_API_URL ?? ''

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(base + path, { signal })
  if (!res.ok) throw new Error(`${res.status} ${path}`)
  return res.json() as Promise<T>
}

async function snapshot() {
  const res = await fetch('/points.json')
  if (!res.ok || !res.headers.get('content-type')?.includes('json')) throw new Error('no snapshot')
  const points = (await res.json()) as MapPoint[]
  if (!points.length) throw new Error('empty snapshot')
  return points
}

let points: Promise<MapPoint[]> | null = null
const details = new Map<number, Promise<PerfumeDetail>>()

export const api = {
  points: () =>
    (points ??= snapshot()
      .catch(() => get<MapPoint[]>('/api/perfumes'))
      .catch((e) => {
        points = null
        throw e
      })),
  detail: (id: number) => {
    let p = details.get(id)
    if (!p) {
      p = get<PerfumeDetail>(`/api/perfumes/${id}`)
      p.catch(() => details.delete(id))
      details.set(id, p)
    }
    return p
  },
  search: (q: string, signal?: AbortSignal) =>
    get<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}`, signal),
  suggest: async (text: string, signal?: AbortSignal): Promise<Suggestion[]> => {
    const res = await fetch(`${base}/api/suggest`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
      signal,
    })
    if (!res.ok) throw new Error(`${res.status} /api/suggest`)
    return res.json() as Promise<Suggestion[]>
  },
  warm: () => {
    fetch(`${base}/api/perfumes/0`).catch(() => {})
  },
}
