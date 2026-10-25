import type { MapPoint, PerfumeDetail, SearchResult } from './types'

const base = import.meta.env.VITE_API_URL ?? ''

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(base + path, { signal })
  if (!res.ok) throw new Error(`${res.status} ${path}`)
  return res.json() as Promise<T>
}

const details = new Map<number, Promise<PerfumeDetail>>()

export const api = {
  points: () => get<MapPoint[]>('/api/perfumes'),
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
}
