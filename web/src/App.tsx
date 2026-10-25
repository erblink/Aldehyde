import type { Family } from './api/types'
import Drops from './Drops'

const FAMILIES: { id: Family; name: string }[] = [
  { id: 'citrus', name: 'Citrus' },
  { id: 'floral', name: 'Floral' },
  { id: 'amber', name: 'Amber' },
  { id: 'green', name: 'Green' },
  { id: 'aquatic', name: 'Aquatic' },
  { id: 'woody', name: 'Woody' },
]

function Grain() {
  return (
    <svg className="grain" aria-hidden="true">
      <filter id="grain">
        <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves={2} stitchTiles="stitch" />
        <feColorMatrix values="0 0 0 0 0.91  0 0 0 0 0.89  0 0 0 0 0.84  0 0 0 0.08 0" />
      </filter>
      <rect width="100%" height="100%" filter="url(#grain)" />
    </svg>
  )
}

function Opening() {
  return (
    <section className="opening" aria-labelledby="opening-title">
      <header>
        <div className="wordmark">scentmap</div>
        <a href="#map">Skip to the map</a>
      </header>
      <div className="opening-foot">
        <h1 id="opening-title" className="opening-title">
          Every perfume, placed by what is in the bottle.
        </h1>
        <div className="opening-hint">
          <p>Drag the families around. Where they overlap, they mix.</p>
          <p aria-hidden="true">Scroll to pull back</p>
        </div>
      </div>
    </section>
  )
}

function FamilyKey() {
  return (
    <ul className="family-key" aria-label="Scent families">
      {FAMILIES.map((f) => (
        <li key={f.id} data-family={f.id}>
          <span className="swatch" aria-hidden="true" style={{ '--c': `var(--fam-${f.id})` } as React.CSSProperties} />
          {f.name}
        </li>
      ))}
    </ul>
  )
}

function MapFrame() {
  return (
    <section id="map" className="map-frame" aria-label="The map">
      <div className="map-canvas" />
      <header>
        <div className="wordmark">scentmap</div>
      </header>
      <p className="map-empty">No perfumes placed yet. The map fills in once the catalogue is imported.</p>
      <FamilyKey />
      <p className="data-note">
        Notes as published by the house. Distance comes from shared notes and accords, not from reviews.
      </p>
    </section>
  )
}

export default function App() {
  return (
    <>
      <Grain />
      <main>
        <Opening />
        <MapFrame />
        <Drops />
      </main>
    </>
  )
}
