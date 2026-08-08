import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { PhosphorStreams } from './PhosphorStreams'


const STORAGE_ENABLED = 'jotdex.idleLockEnabled'
const STORAGE_MINUTES = 'jotdex.idleLockMinutes'

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

type Props = {
  enabled: boolean
  minutes: number
  /** When false (no password), never show the lock overlay. */
  authAvailable: boolean
  /** Kept for callers; MFA is revealed after password, not shown up front. */
  totpEnabled?: boolean
  onLockedChange?: (locked: boolean) => void
}

/**
 * Full-screen lock after N minutes of no pointer/keyboard activity (and when the
 * tab stays hidden for that long). Unlock with the Jotdex password (and TOTP if enabled).
 */
export function IdleLockGate({ enabled, minutes, authAvailable, onLockedChange }: Props) {
  const [locked, setLocked] = useState(false)
  const [password, setPassword] = useState('')
  const [totpCode, setTotpCode] = useState('')
  const [mfaStep, setMfaStep] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const lastActiveRef = useRef(Date.now())
  const hiddenSinceRef = useRef<number | null>(null)

  const markActive = useCallback(() => {
    lastActiveRef.current = Date.now()
  }, [])

  const lock = useCallback(() => {
    setLocked(true)
    setPassword('')
    setTotpCode('')
    setMfaStep(false)
    setError(null)
    onLockedChange?.(true)
  }, [onLockedChange])

  useEffect(() => {
    if (!enabled || !authAvailable) {
      setLocked(false)
      onLockedChange?.(false)
      return
    }

    const onActivity = () => {
      if (!locked) markActive()
    }
    const events: (keyof WindowEventMap)[] = ['pointerdown', 'keydown', 'mousemove', 'wheel', 'touchstart']
    for (const ev of events) window.addEventListener(ev, onActivity, { passive: true })

    const onVis = () => {
      if (document.visibilityState === 'hidden') {
        hiddenSinceRef.current = Date.now()
      } else {
        const since = hiddenSinceRef.current
        hiddenSinceRef.current = null
        const limitMs = Math.max(1, minutes) * 60_000
        if (since != null && Date.now() - since >= limitMs) {
          lock()
        } else {
          markActive()
        }
      }
    }
    document.addEventListener('visibilitychange', onVis)

    const tick = window.setInterval(() => {
      if (locked) return
      const limitMs = Math.max(1, minutes) * 60_000
      if (Date.now() - lastActiveRef.current >= limitMs) lock()
    }, 5_000)

    return () => {
      for (const ev of events) window.removeEventListener(ev, onActivity)
      document.removeEventListener('visibilitychange', onVis)
      window.clearInterval(tick)
    }
  }, [enabled, authAvailable, minutes, locked, lock, markActive, onLockedChange])

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
      setLocked(false)
      lastActiveRef.current = Date.now()
      onLockedChange?.(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unlock failed')
    } finally {
      setBusy(false)
    }
  }

  if (!locked) return null

  return (
    <div className="idle-lock-overlay auth-stage" role="dialog" aria-modal="true" aria-label="Jotdex is locked">
      <PhosphorStreams />
      <div className="idle-lock-card auth-stage-card">
        <h1>Jotdex locked</h1>
        <p>
          {mfaStep
            ? 'Enter the code from your authenticator app (or a recovery code).'
            : 'Enter your password to continue. Notes stay on this PC — this only unlocks the app.'}
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
    </div>
  )
}
