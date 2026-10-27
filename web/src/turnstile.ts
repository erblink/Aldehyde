export const SITE_KEY: string = import.meta.env.VITE_TURNSTILE_SITE_KEY ?? '1x00000000000000000000AA'

type Options = {
  sitekey: string
  appearance: 'always' | 'execute' | 'interaction-only'
  callback: (token: string) => void
  'expired-callback': () => void
  'error-callback': () => void
}

declare global {
  interface Window {
    turnstile: {
      render: (el: HTMLElement, options: Options) => string
      reset: (id: string) => void
      remove: (id: string) => void
    }
  }
}

let script: Promise<void> | null = null

export const loadTurnstile = () =>
  (script ??= new Promise<void>((resolve, reject) => {
    const s = document.createElement('script')
    s.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'
    s.async = true
    s.onload = () => resolve()
    s.onerror = () => {
      script = null
      s.remove()
      reject(new Error('turnstile'))
    }
    document.head.appendChild(s)
  }))
