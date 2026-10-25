import type { Dupe, PerfumeDetail } from './api/types'
import { list, note, shares } from './text'

type Props = {
  detail: PerfumeDetail | 'failed'
  onSelect: (id: number) => void
  onRetry: () => void
  onClose: () => void
}

function notesSentence({ top, heart, base }: PerfumeDetail['notes']) {
  const [t, h, b] = [top, heart, base].map((tier) => tier.map(note))
  const parts: string[] = []
  if (t.length) parts.push(h.length || b.length ? `opens on ${list(t)}` : `notes of ${list(t)}`)
  if (h.length) parts.push(`${parts.length ? 'with ' : ''}${list(h)} at the heart`)
  if (b.length) parts.push(`${parts.length ? 'and ' : ''}settles on ${list(b)}`)
  if (!parts.length) return 'No notes listed.'
  const s = parts.join(', ')
  return `${s[0].toUpperCase()}${s.slice(1)}.`
}

function facts(d: PerfumeDetail): [string, string][] {
  const { top, heart, base } = d.notes
  const tiers: [string, string[]][] =
    heart.length || base.length ? [['Top', top], ['Heart', heart], ['Base', base]] : [['Notes', top]]
  const rows: [string, string][] = [
    ['Concentration', d.strength ?? ''],
    [d.perfumers.length > 1 ? 'Perfumers' : 'Perfumer', list(d.perfumers)],
    ...tiers.map(([label, notes]): [string, string] => [label, list(notes.map(note))]),
    ['Accords', list(d.accords.slice(0, 6))],
  ]
  return rows.filter(([, value]) => value).map(([label, value]) => [label, value[0].toUpperCase() + value.slice(1)])
}

const sourceText = (d: Dupe) =>
  d.source === 'house'
    ? 'Named by the house'
    : d.source === 'community'
      ? `Confirmed by ${d.votes} ${d.votes === 1 ? 'person' : 'people'}`
      : 'Similar notes, unconfirmed'

export default function Detail({ detail, onSelect, onRetry, onClose }: Props) {
  const close = (
    <button type="button" className="detail-close" onClick={onClose}>
      Close
    </button>
  )

  if (detail === 'failed')
    return (
      <article className="detail" aria-live="polite">
        <p className="detail-notes">This perfume didn't load.</p>
        <button type="button" className="link" onClick={onRetry}>
          Try again
        </button>
        {close}
      </article>
    )

  const meta = [
    detail.house,
    detail.year,
  ]
    .filter(Boolean)
    .join(', ')

  return (
    <article className="detail" aria-labelledby="detail-name" aria-live="polite">
      <h2 id="detail-name" className="detail-name">
        {detail.name}
      </h2>
      <p className="detail-meta">{meta}</p>
      <p className="detail-notes">{notesSentence(detail.notes)}</p>
      <dl className="detail-facts">
        {facts(detail).map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>

      {detail.neighbours.length > 0 && (
        <section className="detail-part">
          <h3>Smells close to</h3>
          <ul>
            {detail.neighbours.map((n) => (
              <li key={n.id}>
                <button type="button" className="link" onClick={() => onSelect(n.id)}>
                  {n.name}
                </button>
                <span className="detail-aside">{shares(n.sharedNotes)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <section className="detail-part">
        <h3>Dupes</h3>
        {detail.dupes.length ? (
          <ul>
            {detail.dupes.map((d) => (
              <li key={d.id}>
                <button type="button" className="link" onClick={() => onSelect(d.id)}>
                  {d.name}
                </button>
                <span className={d.source === 'suggested' ? 'detail-aside' : 'detail-aside confirmed'}>
                  {[sourceText(d), shares(d.sharedNotes)].filter(Boolean).join(', ')}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="detail-aside">None confirmed yet.</p>
        )}
      </section>
      {close}
    </article>
  )
}
