import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { createPortal } from 'react-dom'
import { PhosphorStreams } from './PhosphorStreams'

const STORAGE_ENABLED = 'jotdex.idleLockEnabled'
const STORAGE_MINUTES = 'jotdex.idleLockMinutes'
export const AUTH_REQUIRED_EVENT = 'jotdex:auth-required'

export function loadIdleLockEnabled(): boolean {
  try {
    // Opt-in only — never lock until the user enables it (and a password exists).
    return localStorage.getItem(STORAGE_ENABLED) === '1'
  } catch {
    return false
  }
}

export function loadIdleLockMinutes(): number {
  try {
    const n = Number(localStorage.getItem(STORAGE_MINUTES))
    if (Number.isFinite(n) && n >= 1 && n <= 240) return Math.floor(n)
  } catch {
    /* ignore */
  }
  return 15
}

export function saveIdleLockPrefs(enabled: boolean, minutes: number) {
  try {
    localStorage.setItem(STORAGE_ENABLED, enabled ? '1' : '0')
    localStorage.setItem(STORAGE_MINUTES, String(Math.max(1, Math.min(240, Math.floor(minutes)))))
  } catch {
    /* ignore */
  }
}

/** Notify the lock gate that the session is gone (e.g. HTTP 401). */
export function signalAuthRequired() {
  try {
    window.dispatchEvent(new Event(AUTH_REQUIRED_EVENT))
  } catch {
    /* ignore */
  }
}

/** Thrown when an API returns 401; the lock overlay handles it — don't paint a feature error. */
export class SessionGoneError extends Error {
  constructor() {
    super('Session expired')
    this.name = 'SessionGoneError'
  }
}

export function throwIfUnauthorized(res: Response): void {
  if (res.status === 401) throw new SessionGoneError()
}

export function isSessionGone(error: unknown): boolean {
  return error instanceof SessionGoneError
}

type Props = {
  enabled: boolean
  minutes: number
  /** When false (no password), never show the lock overlay. */
  authAvailable: boolean
  /** Kept for callers; MFA is revealed after password, not shown up front. */
  totpEnabled?: boolean
  onLockedChange?: (locked: boolean) => void
  /** After a successful password unlock (not when idle lock is merely disabled). */
  onUnlocked?: () => void
}

/**
 * Full-screen lock after N minutes of no pointer/keyboard activity (and when the
 * tab stays hidden for that long). Also locks on session 401. Unlock with the
 * Jotdex password (and TOTP if enabled).
 */
export function IdleLockGate({ enabled, minutes, authAvailable, onLockedChange, onUnlocked }: Props) {
  const [locked, setLocked] = useState(false)
  const [password, setPassword] = useState('')
  const [totpCode, setTotpCode] = useState('')
  const [mfaStep, setMfaStep] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const lastActiveRef = useRef(Date.now())
  const hiddenSinceRef = useRef<number | null>(null)
  const lockedRef = useRef(false)
  const minutesRef = useRef(minutes)

  useEffect(() => {
    minutesRef.current = minutes
  }, [minutes])

  useEffect(() => {
    lockedRef.current = locked
  }, [locked])

  const markActive = useCallback(() => {
    lastActiveRef.current = Date.now()
  }, [])

  const lock = useCallback(() => {
    if (lockedRef.current) return
    lockedRef.current = true
    setLocked(true)
    setPassword('')
    setTotpCode('')
    setMfaStep(false)
    setError(null)
    onLockedChange?.(true)
    // Drop the cookie so the UI lock matches server auth (clicks can't half-work).
    void fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => {
      /* ignore */
    })
  }, [onLockedChange])

  // Idle timer + activity (only when idle lock is enabled).
  useEffect(() => {
    if (!enabled || !authAvailable) return

    const limitMs = () => Math.max(1, minutesRef.current) * 60_000

    const onActivity = () => {
      if (lockedRef.current) return
      // First click after idle must lock — do not reset the timer and leave the UI open.
      if (Date.now() - lastActiveRef.current >= limitMs()) {
        lock()
        return
      }
      markActive()
    }
    const events: (keyof WindowEventMap)[] = ['pointerdown', 'keydown', 'mousemove', 'wheel', 'touchstart']
    for (const ev of events) window.addEventListener(ev, onActivity, { passive: true })

    const onVis = () => {
      if (document.visibilityState === 'hidden') {
        hiddenSinceRef.current = Date.now()
        return
      }
      const since = hiddenSinceRef.current
      hiddenSinceRef.current = null
      if (lockedRef.current) return
      const limit = limitMs()
      if ((since != null && Date.now() - since >= limit) || Date.now() - lastActiveRef.current >= limit) {
        lock()
      } else {
        markActive()
      }
    }
    document.addEventListener('visibilitychange', onVis)

    const tick = window.setInterval(() => {
      if (lockedRef.current) return
      if (Date.now() - lastActiveRef.current >= limitMs()) lock()
    }, 2_000)

    return () => {
      for (const ev of events) window.removeEventListener(ev, onActivity)
      document.removeEventListener('visibilitychange', onVis)
      window.clearInterval(tick)
    }
  }, [enabled, authAvailable, lock, markActive])

  // Session expired / 401 → always show lock when a password exists.
  useEffect(() => {
    if (!authAvailable) return

    const onAuthRequired = () => lock()
    window.addEventListener(AUTH_REQUIRED_EVENT, onAuthRequired)

    const origFetch = window.fetch.bind(window)
    window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
      const res = await origFetch(input, init)
      if (res.status !== 401) return res
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.href
            : input.url
      if (
        url.includes('/api/') &&
        !url.includes('/api/auth/login') &&
        !url.includes('/api/auth/status') &&
        !url.includes('/api/auth/logout') &&
        !url.includes('/api/health')
      ) {
        signalAuthRequired()
      }
      return res
    }

    return () => {
      window.removeEventListener(AUTH_REQUIRED_EVENT, onAuthRequired)
      window.fetch = origFetch
    }
  }, [authAvailable, lock])

  // If password is removed, clear the overlay.
  useEffect(() => {
    if (!authAvailable && locked) {
      lockedRef.current = false
      setLocked(false)
      onLockedChange?.(false)
    }
  }, [authAvailable, locked, onLockedChange])

  async function unlock(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const code = totpCode.trim()
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          password,
          totpCode: mfaStep && code ? code : undefined,
        }),
      })
      const data = await res.json()
      if (data.requiresTotp) {
        if (!mfaStep) {
          setMfaStep(true)
          setTotpCode('')
          setError(null)
        } else {
          setError(data.error ?? 'Invalid authenticator or recovery code.')
        }
        return
      }
      if (!res.ok || !data.success) {
        setError(data.error ?? 'Incorrect password')
        return
      }
      setPassword('')
      setTotpCode('')
      setMfaStep(false)
      lockedRef.current = false
      setLocked(false)
      lastActiveRef.current = Date.now()
      onLockedChange?.(false)
      onUnlocked?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unlock failed')
    } finally {
      setBusy(false)
    }
  }

  if (!locked) return null

  return createPortal(
    <div className="idle-lock-overlay auth-stage" role="dialog" aria-modal="true" aria-label="Jotdex is locked">
      <PhosphorStreams />
      <div className="idle-lock-card auth-stage-card">
        <h1>Jotdex locked</h1>
        <p>
          {mfaStep
            ? 'Enter the code from your authenticator app (or a recovery code).'
            : 'Enter your password to continue.'}
        </p>
        <form className="auth-form" onSubmit={(e) => void unlock(e)}>
          {!mfaStep ? (
            <label>
              Password
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
                autoFocus
              />
            </label>
          ) : (
            <label className="auth-totp-field">
              Authenticator code
              <input
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                value={totpCode}
                onChange={(e) => setTotpCode(e.target.value)}
                placeholder="6-digit or recovery code"
                required
                autoFocus
              />
            </label>
          )}
          {error && <p className="error">{error}</p>}
          <button type="submit" className="primary" disabled={busy}>
            {busy ? (mfaStep ? 'Verifying…' : 'Checking…') : mfaStep ? 'Verify & unlock' : 'Continue'}
          </button>
        </form>
      </div>
    </div>,
    document.body,
  )
}
