// Response shapes the frontend expects from /api. src/Aldehyde.Api/Contracts.cs must match;
// change both sides together.

export type Family = 'citrus' | 'green' | 'aquatic' | 'floral' | 'woody' | 'amber'

// GET /api/perfumes: every point on the map. x and y are precomputed at import time.
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

// GET /api/perfumes/{id}. Optional fields may be missing from the dataset.
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

// GET /api/search?q=
export interface SearchResult {
  id: number
  name: string
  house: string
  x: number
  y: number
}
