import { useEffect, useState } from 'react'
import { api } from './api/client'
import type { SearchResult } from './api/types'

export default function Search({ onPick }: { onPick: (r: SearchResult) => void }) {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<SearchResult[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [active, setActive] = useState(0)
  const [open, setOpen] = useState(false)
  const term = q.trim()

  useEffect(() => {
    if (term.length < 2) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      api.search(term, ctrl.signal).then(
        (r) => {
          setResults(r)
          setFailed(false)
          setActive(0)
        },
        () => {
          if (!ctrl.signal.aborted) setFailed(true)
        },
      )
    }, 180)
    return () => {
      clearTimeout(t)
      ctrl.abort()
    }
  }, [term])

  const pick = (r: SearchResult) => {
    onPick(r)
    setQ(r.name)
    setOpen(false)
  }

  const show = open && term.length >= 2 && (failed || results !== null)
  const options = show && !failed ? (results ?? []) : []

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      setOpen(true)
      if (options.length) setActive((a) => (a + (e.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length)
    } else if (e.key === 'Enter' && options[active]) {
      e.preventDefault()
      pick(options[active])
    } else if (e.key === 'Escape') {
      e.stopPropagation()
      if (show) setOpen(false)
      else setQ('')
    }
  }

  return (
    <div className="search">
      <label htmlFor="search" className="sr">
        Search perfumes, houses and notes
      </label>
      <input
        id="search"
        type="search"
        role="combobox"
        aria-expanded={options.length > 0}
        aria-controls="search-results"
        aria-autocomplete="list"
        aria-activedescendant={options[active] ? `result-${options[active].id}` : undefined}
        placeholder="Search a perfume, house or note"
        autoComplete="off"
        spellCheck={false}
        value={q}
        onChange={(e) => {
          setQ(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={onKeyDown}
      />
      <ul id="search-results" role="listbox" className="search-results" hidden={!options.length}>
        {options.map((r, i) => (
          <li
            key={r.id}
            id={`result-${r.id}`}
            role="option"
            aria-selected={i === active}
            onMouseDown={(e) => {
              e.preventDefault()
              pick(r)
            }}
            onMouseEnter={() => setActive(i)}
          >
            <span className="result-name">{r.name}</span>
            <span className="result-house">{r.house}</span>
          </li>
        ))}
      </ul>
      <p className="search-note" role="status">
        {show && failed
          ? "Search didn't respond. Try again."
          : show && results?.length === 0
            ? 'No perfume, house or note matches that.'
            : ''}
      </p>
    </div>
  )
}
