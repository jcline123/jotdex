import { useCallback, useEffect, useState } from 'react'

type TokenPublic = {
  id: string
  name: string
  scopes: string[]
  allowedFolderRoots: string[]
  wholeVault: boolean
  createdAt: string
  expiresAt: string
  neverExpires?: boolean
  revokedAt?: string | null
  lastUsedAt?: string | null
  enabled: boolean
  expired: boolean
  active: boolean
}

type AdminState = {
  enabled: boolean
  tokens: TokenPublic[]
  setupGuide?: { headers: string[]; basePath: string; docs: string }
}

const PRESETS: { id: string; label: string; scopes: string[] }[] = [
  { id: 'read', label: 'Read only', scopes: ['notes:read'] },
  { id: 'capture', label: 'Capture and append', scopes: ['notes:read', 'notes:create', 'notes:append'] },
  { id: 'edit', label: 'Read and edit', scopes: ['notes:read', 'notes:create', 'notes:append', 'notes:update'] },
]

type Props = {
  onHint: (msg: string | null) => void
  onError: (msg: string | null) => void
  folders: string[]
}

async function csrfToken(): Promise<string> {
  const r = await fetch('/api/admin/integrations/csrf', { credentials: 'same-origin' })
  if (!r.ok) throw new Error('CSRF token unavailable')
  const j = (await r.json()) as { token?: string }
  if (!j.token) throw new Error('CSRF token missing')
  return j.token
}

function buildScopes(
  presetId: string,
  attach: boolean,
  tasksRead: boolean,
  tasksWrite: boolean,
  notesInsert: boolean,
): string[] {
  const scopes = [...(PRESETS.find((p) => p.id === presetId)?.scopes ?? ['notes:read'])]
  if (attach) scopes.push('attachments:read')
  if (tasksRead) scopes.push('tasks:read')
  if (tasksWrite) {
    scopes.push('tasks:write')
    if (!scopes.includes('tasks:read')) scopes.push('tasks:read')
  }
  if (notesInsert) scopes.push('notes:insert')
  return [...new Set(scopes)]
}

function applyTokenToForm(t: TokenPublic): {
  name: string
  preset: string
  attach: boolean
  tasksRead: boolean
  tasksWrite: boolean
  notesInsert: boolean
  wholeVault: boolean
  selectedFolders: string[]
  neverExpires: boolean
} {
  const s = new Set(t.scopes)
  let preset = 'read'
  if (s.has('notes:update')) preset = 'edit'
  else if (s.has('notes:create') || s.has('notes:append')) preset = 'capture'
  return {
    name: t.name,
    preset,
    attach: s.has('attachments:read'),
    tasksRead: s.has('tasks:read') || s.has('tasks:write'),
    tasksWrite: s.has('tasks:write'),
    notesInsert: s.has('notes:insert'),
    wholeVault: t.wholeVault,
    selectedFolders: [...t.allowedFolderRoots],
    neverExpires: !!t.neverExpires,
  }
}

export function IntegrationSettings({ onHint, onError, folders }: Props) {
  const [state, setState] = useState<AdminState | null>(null)
  const [name, setName] = useState('Grok Bot')
  const [preset, setPreset] = useState('read')
  const [attach, setAttach] = useState(false)
  const [tasksRead, setTasksRead] = useState(false)
  const [tasksWrite, setTasksWrite] = useState(false)
  const [notesInsert, setNotesInsert] = useState(false)
  const [wholeVault, setWholeVault] = useState(false)
  const [selectedFolders, setSelectedFolders] = useState<string[]>([])
  const [days, setDays] = useState(90)
  const [neverExpires, setNeverExpires] = useState(false)
  const [adminPassword, setAdminPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [plaintext, setPlaintext] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const r = await fetch('/api/admin/integrations', { credentials: 'same-origin' })
      if (r.status === 401) {
        onError('Sign in with a local password to manage Integrations.')
        return
      }
      if (!r.ok) throw new Error('Failed to load integrations')
      setState((await r.json()) as AdminState)
      onError(null)
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Failed to load integrations')
    }
  }, [onError])

  useEffect(() => {
    void load()
  }, [load])

  const setEnabled = async (enabled: boolean) => {
    setBusy(true)
    try {
      const token = await csrfToken()
      const r = await fetch('/api/admin/integrations/config', {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
        body: JSON.stringify({ enabled }),
      })
      if (!r.ok) {
        const j = (await r.json().catch(() => ({}))) as { error?: string }
        throw new Error(j.error || 'Update failed')
      }
      onHint(enabled ? 'Integrations enabled.' : 'Integrations disabled.')
      await load()
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Update failed')
    } finally {
      setBusy(false)
    }
  }

  const saveToken = async () => {
    setBusy(true)
    setPlaintext(null)
    try {
      const scopes = buildScopes(preset, attach, tasksRead, tasksWrite, notesInsert)
      const token = await csrfToken()
      const path = editingId
        ? `/api/admin/integrations/tokens/${editingId}/rotate`
        : '/api/admin/integrations/tokens'
      const r = await fetch(path, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
        body: JSON.stringify({
          name,
          scopes,
          wholeVault,
          allowedFolderRoots: wholeVault ? [] : selectedFolders,
          expirationDays: neverExpires ? 0 : days,
          neverExpires,
          adminPassword,
          totpCode: totp || undefined,
        }),
      })
      const j = (await r.json()) as { error?: string; plaintextSecret?: string; warning?: string }
      if (!r.ok) throw new Error(j.error || (editingId ? 'Rotate failed' : 'Create failed'))
      const wasEdit = !!editingId
      setPlaintext(j.plaintextSecret ?? null)
      setAdminPassword('')
      setTotp('')
      setEditingId(null)
      onHint(j.warning || (wasEdit ? 'Token rotated — copy the new secret.' : 'Token created.'))
      await load()
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Save failed')
    } finally {
      setBusy(false)
    }
  }

  const beginEdit = (t: TokenPublic) => {
    const f = applyTokenToForm(t)
    setEditingId(t.id)
    setName(f.name)
    setPreset(f.preset)
    setAttach(f.attach)
    setTasksRead(f.tasksRead)
    setTasksWrite(f.tasksWrite)
    setNotesInsert(f.notesInsert)
    setWholeVault(f.wholeVault)
    setSelectedFolders(f.selectedFolders)
    setNeverExpires(f.neverExpires)
    setPlaintext(null)
    onHint(`Editing “${t.name}” — saving rotates the secret and applies new scopes.`)
  }

  const cancelEdit = () => {
    setEditingId(null)
    onHint(null)
  }

  const revoke = async (id: string) => {
    if (!confirm('Revoke this token immediately?')) return
    setBusy(true)
    try {
      const token = await csrfToken()
      const r = await fetch(`/api/admin/integrations/tokens/${id}/revoke`, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'X-CSRF-TOKEN': token },
      })
      if (!r.ok) throw new Error('Revoke failed')
      onHint('Token revoked.')
      await load()
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Revoke failed')
    } finally {
      setBusy(false)
    }
  }

  const toggleFolder = (f: string) => {
    setSelectedFolders((prev) => (prev.includes(f) ? prev.filter((x) => x !== f) : [...prev, f]))
  }

  if (!state) {
    return <p className="muted">Loading integrations…</p>
  }

  return (
    <div className="settings-section">
      <h3 className="settings-subhead">Integrations API</h3>
      <p className="muted">
        Dedicated bearer tokens for automation (for example Grok Bot) through Cloudflare Tunnel. Default off. Browser
        login is unchanged.
      </p>
      <label className="field checkbox">
        <input
          type="checkbox"
          checked={state.enabled}
          disabled={busy}
          onChange={(e) => void setEnabled(e.target.checked)}
        />
        Enable integrations
      </label>

      <h4 className="settings-subhead">{editingId ? 'Edit / rotate token' : 'Create token'}</h4>
      {editingId && (
        <p className="muted">
          Saving issues a new secret and updates scopes/folders. Update your bot with the new token.
        </p>
      )}
      <label className="field">
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} />
      </label>
      <label className="field">
        Preset
        <select value={preset} onChange={(e) => setPreset(e.target.value)}>
          {PRESETS.map((p) => (
            <option key={p.id} value={p.id}>
              {p.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field checkbox">
        <input type="checkbox" checked={attach} onChange={(e) => setAttach(e.target.checked)} />
        Allow attachment downloads
      </label>
      <label className="field checkbox">
        <input type="checkbox" checked={tasksRead} onChange={(e) => setTasksRead(e.target.checked)} />
        Read tasks (tasks:read)
      </label>
      <label className="field checkbox">
        <input
          type="checkbox"
          checked={tasksWrite}
          onChange={(e) => {
            setTasksWrite(e.target.checked)
            if (e.target.checked) setTasksRead(true)
          }}
        />
        Write tasks (tasks:write) — standalone Todos.md needs whole vault
      </label>
      <label className="field checkbox">
        <input type="checkbox" checked={notesInsert} onChange={(e) => setNotesInsert(e.target.checked)} />
        Insert into notes (notes:insert)
      </label>
      <label className="field checkbox">
        <input type="checkbox" checked={wholeVault} onChange={(e) => setWholeVault(e.target.checked)} />
        Whole vault (deliberate)
      </label>
      {!wholeVault && (
        <div className="field">
          <span>Folders (and descendants)</span>
          <div className="integration-folder-list">
            {folders.length === 0 && <p className="muted">No folders listed yet.</p>}
            {folders.map((f) => (
              <label key={f} className="checkbox">
                <input type="checkbox" checked={selectedFolders.includes(f)} onChange={() => toggleFolder(f)} />
                {f}
              </label>
            ))}
          </div>
        </div>
      )}
      <label className="field checkbox">
        <input type="checkbox" checked={neverExpires} onChange={(e) => setNeverExpires(e.target.checked)} />
        Never expire (revoke manually when done)
      </label>
      {!neverExpires && (
        <label className="field">
          Expiration (days, max 365)
          <input
            type="number"
            min={1}
            max={365}
            value={days}
            onChange={(e) => setDays(Number(e.target.value) || 90)}
          />
        </label>
      )}
      <label className="field">
        Confirm admin password
        <input
          type="password"
          autoComplete="current-password"
          value={adminPassword}
          onChange={(e) => setAdminPassword(e.target.value)}
        />
      </label>
      <label className="field">
        Authenticator code (if TOTP enabled)
        <input value={totp} onChange={(e) => setTotp(e.target.value)} autoComplete="one-time-code" />
      </label>
      <div className="modal-actions">
        {editingId && (
          <button type="button" className="ghost" disabled={busy} onClick={cancelEdit}>
            Cancel edit
          </button>
        )}
        <button type="button" disabled={busy || !adminPassword} onClick={() => void saveToken()}>
          {editingId ? 'Save and rotate' : 'Create token'}
        </button>
      </div>

      {plaintext && (
        <div className="integration-secret-once">
          <p>
            <strong>Copy now — shown once:</strong>
          </p>
          <code className="integration-secret">{plaintext}</code>
          <button
            type="button"
            className="ghost"
            onClick={() => {
              void navigator.clipboard.writeText(plaintext)
              onHint('Secret copied to clipboard.')
            }}
          >
            Copy
          </button>
        </div>
      )}

      <h4 className="settings-subhead">Tokens</h4>
      {state.tokens.length === 0 && <p className="muted">No tokens yet.</p>}
      <ul className="integration-token-list">
        {state.tokens.map((t) => (
          <li key={t.id}>
            <div>
              <strong>{t.name}</strong> · {t.active ? 'active' : t.revokedAt ? 'revoked' : t.expired ? 'expired' : 'off'}
              <br />
              <span className="muted">
                {t.scopes.join(', ')} ·{' '}
                {t.wholeVault ? 'whole vault' : t.allowedFolderRoots.join(', ') || 'no folders'} ·{' '}
                {t.neverExpires ? 'never expires' : `expires ${new Date(t.expiresAt).toLocaleDateString()}`}
                {t.lastUsedAt ? ` · last used ${new Date(t.lastUsedAt).toLocaleString()}` : ''}
              </span>
            </div>
            {t.active && (
              <div className="modal-actions">
                <button type="button" className="ghost" disabled={busy} onClick={() => beginEdit(t)}>
                  Edit
                </button>
                <button type="button" className="ghost" disabled={busy} onClick={() => void revoke(t.id)}>
                  Revoke
                </button>
              </div>
            )}
          </li>
        ))}
      </ul>

      <h4 className="settings-subhead">Connection headers</h4>
      <pre className="integration-guide">
        {`CF-Access-Client-Id: <cloudflare-service-client-id>
CF-Access-Client-Secret: <cloudflare-service-client-secret>
Authorization: Bearer <jotdex-api-token>

Base path: /api/integrations/v1
Docs: docs/integrations-api.md`}
      </pre>
    </div>
  )
}
