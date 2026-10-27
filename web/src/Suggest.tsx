import { useEffect, useRef, useState } from 'react'
import { api } from './api/client'
import type { Suggestion } from './api/types'
import { list } from './text'
import { loadTurnstile, SITE_KEY } from './turnstile'

type State = 'idle' | 'pending' | 'done' | 'empty' | 'unavailable' | 'failed'

export default function Suggest({ onPick }: { onPick: (id: number) => void }) {
  const [q, setQ] = useState('')
  const [state, setState] = useState<State>('idle')
  const [picks, setPicks] = useState<Suggestion[]>([])
  const [token, setToken] = useState<string | null>(null)
  const [blocked, setBlocked] = useState(false)
  const ctrl = useRef<AbortController | null>(null)
  const box = useRef<HTMLDivElement>(null)
  const widget = useRef<string | null>(null)

  useEffect(() => {
    let live = true
    loadTurnstile().then(
      () => {
        if (!live || !box.current) return
        widget.current = window.turnstile.render(box.current, {
          sitekey: SITE_KEY,
          appearance: 'interaction-only',
          callback: (t) => {
            setToken(t)
            setBlocked(false)
          },
          'expired-callback': () => setToken(null),
          'error-callback': () => {
            setToken(null)
            setBlocked(true)
          },
        })
      },
      () => live && setBlocked(true),
    )
    return () => {
      live = false
      if (widget.current) window.turnstile.remove(widget.current)
      widget.current = null
    }
  }, [])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const text = q.trim()
    if (text.length < 3 || !token) return
    ctrl.current?.abort()
    const c = new AbortController()
    ctrl.current = c
    setState('pending')
    setToken(null)
    if (widget.current) window.turnstile.reset(widget.current)
    api.suggest(text, token, c.signal).then(
      (r) => {
        setPicks(r)
        setState(r.length ? 'done' : 'empty')
      },
      (err: Error) => {
        if (c.signal.aborted) return
        setState(err.message.startsWith('503') ? 'unavailable' : 'failed')
      },
    )
  }

  const pick = (s: Suggestion) => {
    onPick(s.id)
    setState('idle')
    setPicks([])
  }

  return (
    <div className="suggest">
      <form onSubmit={submit}>
        <label htmlFor="suggest" className="sr">
          Describe a mood, a place or a moment
        </label>
        <div className="suggest-row">
          <input
            id="suggest"
            type="text"
            placeholder="Rainy day in a library, reading philosophy"
            autoComplete="off"
            spellCheck={false}
            maxLength={500}
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
          <button type="submit" disabled={state === 'pending' || !token || q.trim().length < 3}>
            Suggest
          </button>
        </div>
        <div ref={box} className="suggest-check" />
      </form>

      <p className="suggest-note" role="status">
        {state === 'pending'
          ? 'Looking for something close to that.'
          : blocked || state === 'unavailable'
            ? "Suggestions aren't available right now."
            : state === 'failed'
              ? "Suggestions didn't respond. Try again."
              : state === 'empty'
                ? 'No perfume sits close to that yet.'
                : ''}
      </p>

      {state === 'done' && picks.length > 0 && (
        <div className="suggest-results">
          <p className="suggest-heading">Closest to that</p>
          <button type="button" className="suggest-main" onClick={() => pick(picks[0])}>
            <span className="suggest-name main">{picks[0].name}</span>
            <span className="suggest-aside">
              {[picks[0].house, list(picks[0].accords)].filter(Boolean).join(', ')}
            </span>
          </button>
          {picks.slice(1).map((s) => (
            <button key={s.id} type="button" className="suggest-alt" onClick={() => pick(s)}>
              <span className="suggest-name">{s.name}</span>
              <span className="suggest-aside">{[s.house, list(s.accords)].filter(Boolean).join(', ')}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
