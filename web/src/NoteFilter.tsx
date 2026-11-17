import { useEffect, useEffectEvent, useState } from 'react'
import { api } from './api/client'

type Props = { onChange: (ids: Set<number> | null) => void; shown: number }

export default function NoteFilter({ onChange, shown }: Props) {
  const [q, setQ] = useState('')
  const [found, setFound] = useState<number | null>(null)
  const [failed, setFailed] = useState(false)
  const term = q.trim()
  const report = useEffectEvent(onChange)

  useEffect(() => {
    if (term.length < 2) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      api.byNote(term, ctrl.signal).then(
        (ids) => {
          report(ids.length ? new Set(ids) : null)
          setFound(ids.length)
          setFailed(false)
        },
        () => {
          if (!ctrl.signal.aborted) setFailed(true)
        },
      )
    }, 250)
    return () => {
      clearTimeout(t)
      ctrl.abort()
    }
  }, [term])

  const change = (value: string) => {
    setQ(value)
    if (value.trim().length < 2) {
      onChange(null)
      setFound(null)
      setFailed(false)
    }
  }

  return (
    <div className="search note-filter">
      <label htmlFor="note-filter" className="sr">
        Show only perfumes with a note
      </label>
      <input
        id="note-filter"
        type="search"
        placeholder="Filter by note"
        autoComplete="off"
        spellCheck={false}
        value={q}
        onChange={(e) => change(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape' && q) {
            e.stopPropagation()
            change('')
          }
        }}
      />
      <p className="search-note" role="status">
        {term.length < 2
          ? ''
          : failed
            ? "The filter didn't respond. Try again."
            : found === 0
              ? 'No perfume lists that note.'
              : found
                ? `${shown} ${shown === 1 ? 'perfume' : 'perfumes'}`
                : ''}
      </p>
    </div>
  )
}
