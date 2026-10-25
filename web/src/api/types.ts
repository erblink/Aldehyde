export type Family = 'citrus' | 'green' | 'aquatic' | 'floral' | 'woody' | 'amber'

export interface MapPoint {
  id: number
  name: string
  house: string
  family: Family
  x: number
  y: number
}

export interface Neighbour {
  id: number
  name: string
  sharedNotes: string[]
}

export type DupeSource = 'house' | 'community' | 'suggested'

export interface Dupe {
  id: number
  name: string
  source: DupeSource
  votes: number
  sharedNotes: string[]
}

export interface PerfumeDetail {
  id: number
  name: string
  house: string
  year?: number
  strength?: string
  perfumers: string[]
  notes: { top: string[]; heart: string[]; base: string[] }
  accords: string[]
  family: Family
  neighbours: Neighbour[]
  dupes: Dupe[]
}

export interface SearchResult {
  id: number
  name: string
  house: string
  x: number
  y: number
}
