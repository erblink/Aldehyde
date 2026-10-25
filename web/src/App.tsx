import { Fragment, lazy, Suspense, useEffect } from 'react'
import { api } from './api/client'
import Drops from './Drops'
import Story from './Story'

const MapFrame = lazy(() => import('./MapFrame'))

const HEADLINE = 'Your ultimate scent guide.'

const GRAIN = (() => {
  const size = 160
  const c = document.createElement('canvas')
  c.width = c.height = size
  const ctx = c.getContext('2d')
  if (!ctx) return ''
  const img = ctx.createImageData(size, size)
  for (let i = 0; i < img.data.length; i += 4) {
    img.data[i] = 232
    img.data[i + 1] = 226
    img.data[i + 2] = 214
    img.data[i + 3] = Math.random() * 15
  }
  ctx.putImageData(img, 0, 0)
  return c.toDataURL()
})()

function Opening() {
  return (
    <section className="opening" aria-labelledby="opening-title">
      <header>
        <div className="wordmark">Aldehyde</div>
        <a href="#map">Skip to the map</a>
      </header>
      <div className="opening-foot">
        <h1 id="opening-title" className="opening-title">
          {HEADLINE.split(' ').map((word, i) => (
            <Fragment key={i}>
              <span className="word">
                <span style={{ '--i': i } as React.CSSProperties}>{word}</span>
              </span>{' '}
            </Fragment>
          ))}
        </h1>
        <div className="opening-hint">
          <p className="scroll-cue" aria-hidden="true">
            Scroll to explore
          </p>
        </div>
      </div>
    </section>
  )
}

export default function App() {
  useEffect(() => api.warm(), [])

  return (
    <>
      <div className="grain" aria-hidden="true" style={{ backgroundImage: GRAIN ? `url(${GRAIN})` : undefined }} />
      <main>
        <Opening />
        <Story />
        <Suspense fallback={<section id="map" className="map-frame map-pending" aria-label="The map" />}>
          <MapFrame />
        </Suspense>
        <Drops />
      </main>
    </>
  )
}
