import Drops from './Drops'
import MapFrame from './MapFrame'

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
