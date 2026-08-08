import { useEffect, useState } from 'react'
import { PhosphorStreams } from './PhosphorStreams'


type BrowseEntry = { name: string; path: string; type: string }

type Props = {
  onComplete: () => void
}

export function FirstRunWizard({ onComplete }: Props) {
  const [step, setStep] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Vault
  const [vaultPath, setVaultPath] = useState('')
  const [browsePath, setBrowsePath] = useState('')
  const [browseParent, setBrowseParent] = useState<string | null>(null)
  const [browseEntries, setBrowseEntries] = useState<BrowseEntry[]>([])

  // Admin
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')

  // Network
  const [bindMode, setBindMode] = useState<'loopback' | 'lan'>('loopback')
  const [port, setPort] = useState(5180)

  useEffect(() => {
    void openBrowse()
    fetch('/api/settings/network')
      .then((r) => r.json())
      .then((n) => {
        if (n.bindMode === 'lan') setBindMode('lan')
        if (typeof n.port === 'number') setPort(n.port)
      })
      .catch(() => {})
    fetch('/api/settings/vault')
      .then((r) => r.json())
      .then((v) => {
        if (v.vaultPath) setVaultPath(v.vaultPath)
      })
      .catch(() => {})
  }, [])

  async function openBrowse(path?: string) {
    const q = path ? `?path=${encodeURIComponent(path)}` : ''
    const res = await fetch(`/api/settings/browse${q}`)
    const data = await res.json()
    setBrowsePath(data.path ?? '')
    setBrowseParent(data.parent ?? null)
    setBrowseEntries(data.entries ?? [])
  }

  async function saveVault() {
    setError(null)
    if (!vaultPath.trim()) {
      setError('Choose a vault folder.')
      return false
    }
    setBusy(true)
    try {
      const res = await fetch('/api/settings/vault', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ vaultPath }),
      })
      const data = await res.json()
      if (!res.ok) {
        setError(data.error ?? 'Could not set vault path')
        return false
      }
      return true
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not set vault path')
      return false
    } finally {
      setBusy(false)
    }
  }

  async function saveNetwork() {
    setError(null)
    setBusy(true)
    try {
      const res = await fetch('/api/settings/network', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ bindMode, port }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setError(data.error ?? 'Could not save network settings')
        return false
      }
      return true
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save network settings')
      return false
    } finally {
      setBusy(false)
    }
  }

  async function createAdmin() {
    setError(null)
    if (password !== confirm) {
      setError('Passwords do not match.')
      return false
    }
    if (password.length < 6) {
      setError('Password must be at least 6 characters.')
      return false
    }
    setBusy(true)
    try {
      const res = await fetch('/api/auth/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ password }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setError(data.error ?? 'Could not set password')
        return false
      }
      return true
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not set password')
      return false
    } finally {
      setBusy(false)
    }
  }

  async function next() {
    if (step === 0) {
      if (!(await saveVault())) return
      setStep(1)
      return
    }
    if (step === 1) {
      if (password.trim() || confirm.trim()) {
        if (!(await createAdmin())) return
      }
      setStep(2)
      return
    }
    if (step === 2) {
      if (!(await saveNetwork())) return
      onComplete()
    }
  }

  return (
    <div className="setup-screen">
      <div className="setup-card">
        <p className="brand">Jotdex</p>
        <h1>First-run setup</h1>
        <p className="step-indicator">Step {step + 1} of 3</p>

        {step === 0 && (
          <>
            <p>Choose the folder that holds your Markdown vault. Prefer local disk — not iCloud — for the live vault.</p>
            <label className="field">
              Vault path
              <input value={vaultPath} onChange={(e) => setVaultPath(e.target.value)} placeholder="C:\JotdexVault" />
            </label>
            <div className="browser">
              <div className="browser-bar">
                <button type="button" className="ghost" disabled={!browseParent} onClick={() => browseParent && void openBrowse(browseParent)}>
                  Up
                </button>
                <code>{browsePath || 'Drives'}</code>
                {browsePath && (
                  <button type="button" className="ghost" onClick={() => setVaultPath(browsePath)}>
                    Use current
                  </button>
                )}
              </div>
              <ul>
                {browseEntries.map((e) => (
                  <li key={e.path}>
                    <button type="button" onClick={() => void openBrowse(e.path)}>
                      {e.type === 'drive' ? e.name : e.name}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          </>
        )}

        {step === 1 && (
          <>
            <p>
              Optional: set a password (no username). Leave blank to skip — you can add or remove it later in Settings →
              Security.
            </p>
            <div className="auth-form">
              <label>
                Password (at least 6 characters)
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  autoComplete="new-password"
                  minLength={6}
                />
              </label>
              <label>
                Confirm password
                <input
                  type="password"
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                  autoComplete="new-password"
                  minLength={6}
                />
              </label>
            </div>
          </>
        )}

        {step === 2 && (
          <>
            <p>Network access defaults to this PC only. LAN binding is opt-in.</p>
            <div className="auth-form">
              <label>
                Binding
                <select value={bindMode} onChange={(e) => setBindMode(e.target.value as 'loopback' | 'lan')}>
                  <option value="loopback">This PC only (127.0.0.1)</option>
                  <option value="lan">LAN (all interfaces)</option>
                </select>
              </label>
              <label>
                Port
                <input
                  type="number"
                  min={1}
                  max={65535}
                  value={port}
                  onChange={(e) => setPort(Number(e.target.value) || 5180)}
                />
              </label>
              {bindMode === 'lan' && (
                <p className="warn">
                  LAN HTTP sends credentials on your local network. Prefer a VPN or HTTPS certificate when exposing beyond this machine.
                </p>
              )}
              <p className="muted">Bind/port changes apply on the next server start.</p>
            </div>
          </>
        )}

        {error && <p className="error">{error}</p>}
        <div className="wizard-actions">
          {step > 0 && (
            <button type="button" className="ghost" disabled={busy} onClick={() => { setError(null); setStep((s) => s - 1) }}>
              Back
            </button>
          )}
          <button type="button" className="primary" disabled={busy} onClick={() => void next()}>
            {busy
              ? 'Saving…'
              : step === 2
                ? 'Finish setup'
                : step === 1 && !password.trim() && !confirm.trim()
                  ? 'Skip password'
                  : 'Continue'}
          </button>
        </div>
      </div>
    </div>
  )
}

type LoginProps = {
  onLoggedIn: () => void
}

export function LoginScreen({ onLoggedIn }: LoginProps) {
  const [password, setPassword] = useState('')
  const [totpCode, setTotpCode] = useState('')
  const [needsTotp, setNeedsTotp] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          password,
          totpCode: needsTotp || totpCode.trim() ? totpCode.trim() : undefined,
        }),
      })
      const data = await res.json()
      if (data.requiresTotp) {
        setNeedsTotp(true)
        setError(null)
        return
      }
      if (!res.ok || !data.success) {
        setError(data.error ?? 'Login failed')
        return
      }
      onLoggedIn()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="setup-screen auth-stage">
      <PhosphorStreams />
      <div className="setup-card auth-stage-card">
        <h1>Jotdex</h1>
        <p>Enter your password to open your vault.</p>
        <form className="auth-form" onSubmit={(e) => void submit(e)}>
          <label>
            Password
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
              autoFocus={!needsTotp}
            />
          </label>
          {needsTotp && (
            <label>
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
          <button type="submit" disabled={busy}>
            {busy ? 'Unlocking…' : needsTotp ? 'Verify & unlock' : 'Unlock'}
          </button>
        </form>
      </div>
    </div>
  )
}
