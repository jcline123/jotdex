import { useCallback, useEffect, useState } from 'react'
import {
  CLOUD_PROVIDERS,
  type CloudBackupSummary,
  type CloudProviderKind,
  completeCloudProviderConnect,
  connectCloudProvider,
  connectionLabel,
  disconnectCloudProvider,
  extractAuthorizationCode,
  fetchCloudBackupSummary,
  findProviderSettings,
  findProviderStatus,
  formatBytes,
  formatUtc,
  fetchCloudBackupOperation,
  getCloudProviderConnectAttempt,
  healthLabel,
  retryCloudProvider,
  runCloudBackup,
  saveCloudBackupSettings,
} from './cloudBackupApi'

type Props = {
  onHint?: (message: string | null) => void
  onError?: (message: string | null) => void
}

const PLAIN_ZIP_CONFIRM =
  'Readable vault ZIP is unencrypted and contains only your Markdown notes and .assets — anyone with access to the cloud folder can read them.\n\n' +
  'It never includes auth, history, secrets, or cloud credentials. The encrypted Move Kit remains the primary recovery path.\n\n' +
  'Enable readable vault ZIP?'

type DraftOauth = {
  clientId: string
  clientSecret: string
}

function artifactLine(
  label: string,
  art:
    | {
        lastVerifiedUtc?: string | null
        lastUploadUtc?: string | null
        lastFileName?: string | null
        lastRemoteSizeBytes?: number | null
        lastFailureCode?: string
        lastFailureMessage?: string | null
        required?: boolean
      }
    | undefined,
  enabled: boolean,
  connected: boolean,
): string {
  if (!enabled) return `${label}: off`
  if (!connected) return `${label}: connect first`
  if (!art) return `${label}: no status yet`
  const when = formatUtc(art.lastVerifiedUtc || art.lastUploadUtc)
  const size = formatBytes(art.lastRemoteSizeBytes)
  const name = art.lastFileName ? ` · ${art.lastFileName}` : ''
  const fail =
    art.lastFailureCode && art.lastFailureCode !== 'none'
      ? ` · ${art.lastFailureMessage || art.lastFailureCode}`
      : ''
  if (when === '—' && !size && !fail) return `${label}: waiting for first upload`
  return `${label}: ${when}${size ? ` · ${size}` : ''}${name}${fail}`
}

export function CloudBackupSettings({ onHint, onError }: Props) {
  const [summary, setSummary] = useState<CloudBackupSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [intervalHours, setIntervalHours] = useState(24)
  const [versionsToKeep, setVersionsToKeep] = useState(3)
  const [includePlainVaultZip, setIncludePlainVaultZip] = useState(false)
  const [oauthDraft, setOauthDraft] = useState<Record<CloudProviderKind, DraftOauth>>({
    dropbox: { clientId: '', clientSecret: '' },
    googleDrive: { clientId: '', clientSecret: '' },
    oneDrive: { clientId: '', clientSecret: '' },
  })

  const refresh = useCallback(async () => {
    try {
      const s = await fetchCloudBackupSummary()
      setSummary(s)
      setIntervalHours(s.settings.intervalHours)
      setVersionsToKeep(s.settings.versionsToKeep)
      setIncludePlainVaultZip(s.settings.includePlainVaultZip)
      setOauthDraft({
        dropbox: {
          clientId: findProviderSettings(s.settings, 'dropbox')?.oauthClientId ?? '',
          clientSecret: '',
        },
        googleDrive: {
          clientId: findProviderSettings(s.settings, 'googleDrive')?.oauthClientId ?? '',
          clientSecret: '',
        },
        oneDrive: {
          clientId: findProviderSettings(s.settings, 'oneDrive')?.oauthClientId ?? '',
          clientSecret: '',
        },
      })
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Could not load cloud backup')
    } finally {
      setLoading(false)
    }
  }, [onError])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    if (!summary?.activeOperation?.running) return
    const id = window.setInterval(() => void refresh(), 2500)
    return () => window.clearInterval(id)
  }, [summary?.activeOperation?.running, refresh])

  function updateDraft(kind: CloudProviderKind, patch: Partial<DraftOauth>) {
    setOauthDraft((prev) => ({ ...prev, [kind]: { ...prev[kind], ...patch } }))
  }

  async function persistProviderOauth(kind: CloudProviderKind) {
    const meta = CLOUD_PROVIDERS.find((p) => p.kind === kind)!
    const draft = oauthDraft[kind]
    const existing = findProviderSettings(summary?.settings, kind)
    const clientId = draft.clientId.trim() || existing?.oauthClientId?.trim() || ''
    if (!clientId) throw new Error('Paste the Application (client) ID / App key first.')
    const body = {
      intervalHours,
      versionsToKeep,
      includePlainVaultZip,
      providers: [
        {
          provider: kind,
          enabled: existing?.enabled ?? false,
          oauthClientId: clientId,
          oauthRedirectUri: meta.redirectUri,
          ...(draft.clientSecret.trim() ? { oauthClientSecret: draft.clientSecret.trim() } : {}),
        },
      ],
    }
    const res = await saveCloudBackupSettings(body)
    if (!res.success) throw new Error(res.error ?? 'Could not save app settings')
  }

  async function save() {
    setBusy(true)
    onHint?.('Saving cloud backup settings…')
    onError?.(null)
    try {
      const providers = CLOUD_PROVIDERS.map((p) => {
        const draft = oauthDraft[p.kind]
        const existing = findProviderSettings(summary?.settings, p.kind)
        const st = findProviderStatus(summary?.state, p.kind)
        const clientId = draft.clientId.trim() || existing?.oauthClientId?.trim() || ''
        const connected = st?.connectionState === 'connected' || st?.connectionState === 'reconnectRequired'
        return {
          provider: p.kind,
          enabled: Boolean(existing?.enabled || connected),
          oauthClientId: clientId,
          oauthRedirectUri: p.redirectUri,
          ...(draft.clientSecret.trim() ? { oauthClientSecret: draft.clientSecret.trim() } : {}),
        }
      })
      const res = await saveCloudBackupSettings({
        intervalHours,
        versionsToKeep,
        includePlainVaultZip,
        providers,
      })
      if (!res.success) {
        onError?.(res.error ?? 'Could not save')
        onHint?.(null)
        return
      }
      onHint?.('Cloud backup settings saved.')
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  async function saveProviderOauth(kind: CloudProviderKind) {
    setBusy(true)
    onHint?.(`Saving ${kind} app settings…`)
    onError?.(null)
    try {
      await persistProviderOauth(kind)
      onHint?.(`${CLOUD_PROVIDERS.find((p) => p.kind === kind)?.label} app settings saved. You can Connect now.`)
      await refresh()
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Could not save app settings')
      onHint?.(null)
    } finally {
      setBusy(false)
    }
  }

  async function runNow(provider?: CloudProviderKind) {
    setBusy(true)
    onError?.(null)
    onHint?.(provider ? `Starting ${provider} backup…` : 'Starting cloud backup…')
    try {
      const res = provider ? await retryCloudProvider(provider) : await runCloudBackup()
      if (!res.accepted || !res.operationId) {
        onError?.(res.error ?? 'Could not start backup')
        onHint?.(null)
        return
      }

      onHint?.('Backup running… creating files and uploading. This can take a few minutes for large vaults.')
      const deadline = Date.now() + 30 * 60_000
      let finalPhase = 'running'
      let finalError: string | null = null
      let providerPhases: Record<string, string> | undefined
      while (Date.now() < deadline) {
        await new Promise((r) => window.setTimeout(r, 2000))
        const snap = await fetchCloudBackupSummary()
        setSummary(snap)
        const op =
          snap.activeOperation?.operationId === res.operationId
            ? snap.activeOperation
            : null
        if (op?.running) {
          onHint?.(
            `Backup running… ${op.phase || 'working'}${
              op.providerPhases
                ? ` (${Object.entries(op.providerPhases)
                    .map(([k, v]) => `${k}: ${v}`)
                    .join(', ')})`
                : ''
            }`,
          )
          continue
        }

        // Prefer finished op from summary; fall back to fetch by id
        let finished = op
        if (!finished && res.operationId) {
          finished = await fetchCloudBackupOperation(res.operationId)
        }
        finalPhase = finished?.phase || snap.activeOperation?.phase || 'finished'
        finalError = finished?.error || null
        providerPhases = finished?.providerPhases
        break
      }

      await refresh()
      if (finalError) {
        onError?.(finalError)
        onHint?.(null)
      } else if (providerPhases && Object.values(providerPhases).some((p) => p === 'failed')) {
        const failed = Object.entries(providerPhases)
          .filter(([, v]) => v === 'failed')
          .map(([k]) => k)
          .join(', ')
        onError?.(`Backup finished with provider failures: ${failed}. See status on the provider card.`)
        onHint?.(null)
      } else if (providerPhases && Object.values(providerPhases).some((p) => p === 'done')) {
        onHint?.('Backup finished successfully. Check the provider card for upload time/size.')
      } else {
        onHint?.(`Backup finished (${finalPhase}).`)
      }
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Backup failed')
      onHint?.(null)
    } finally {
      setBusy(false)
    }
  }

  async function connect(kind: CloudProviderKind) {
    const draft = oauthDraft[kind]
    if (!draft.clientId.trim()) {
      onError?.('Paste the App key / Client ID first, then Connect.')
      return
    }

    setBusy(true)
    onError?.(null)
    onHint?.('Saving app settings, then opening sign-in…')
    try {
      await persistProviderOauth(kind)
      await refresh()

      const began = await connectCloudProvider(kind)
      if (!began.success || !began.attempt) {
        onError?.(
          began.error === 'Provider unavailable in this build.'
            ? 'Paste a valid App key / Client ID, then Connect again.'
            : (began.error ?? 'Connect failed'),
        )
        onHint?.(null)
        await refresh()
        return
      }
      const attemptId = began.attempt.attemptId
      if (began.attempt.completed) {
        if (began.attempt.success) onHint?.('Provider connected — backup starting.')
        else onError?.(began.attempt.error ?? 'Connect failed')
        await refresh()
        return
      }

      const url = began.attempt.authorizeUrl
      if (url) window.open(url, '_blank', 'noopener,noreferrer')
      onHint?.('Approve access in the browser… waiting for connection.')

      const deadline = Date.now() + 3 * 60_000
      let finished = false
      while (Date.now() < deadline) {
        await new Promise((r) => window.setTimeout(r, 1500))
        const attempt = await getCloudProviderConnectAttempt(kind, attemptId)
        if (attempt?.completed) {
          finished = true
          if (attempt.success) onHint?.('Provider connected — backup starting.')
          else onError?.(attempt.error ?? 'Connect failed')
          break
        }
        const snap = await fetchCloudBackupSummary()
        const st = findProviderStatus(snap.state, kind)
        if (st?.connectionState === 'connected') {
          finished = true
          onHint?.('Provider connected — backup starting.')
          setSummary(snap)
          break
        }
      }

      if (!finished) {
        const raw = window.prompt(
          'Still waiting. If the browser showed a success page, click Cancel. Otherwise paste the authorization code (or redirect URL):',
        )
        if (raw) {
          const code = extractAuthorizationCode(raw)
          if (code) {
            const done = await completeCloudProviderConnect(kind, attemptId, code)
            if (!done.success) onError?.(done.error ?? 'Connect failed')
            else onHint?.('Provider connected — backup starting.')
          }
        } else {
          onHint?.('Connect still pending — click Connect again if needed.')
        }
      }
      await refresh()
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Connect failed')
      onHint?.(null)
    } finally {
      setBusy(false)
    }
  }

  async function disconnect(kind: CloudProviderKind) {
    if (!window.confirm('Disconnect this cloud provider? Scheduled backups to it will stop until you reconnect.')) {
      return
    }
    setBusy(true)
    onError?.(null)
    try {
      const res = await disconnectCloudProvider(kind)
      if (!res.success) onError?.(res.error ?? 'Disconnect failed')
      else onHint?.('Provider disconnected.')
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  function onPlainZipToggle(checked: boolean) {
    if (checked && !includePlainVaultZip) {
      if (!window.confirm(PLAIN_ZIP_CONFIRM)) return
    }
    setIncludePlainVaultZip(checked)
  }

  if (loading && !summary) {
    return (
      <>
        <h2 className="settings-section">Cloud backups</h2>
        <p className="muted">Loading cloud backup status…</p>
      </>
    )
  }

  const agg = summary?.health.aggregateHealth
  const running = summary?.health.running || summary?.activeOperation?.running

  return (
    <>
      <h2 className="settings-section">Cloud backups</h2>
      <p className="lede">
        Upload encrypted Move Kits on a schedule to Dropbox, Google Drive, and/or OneDrive via their APIs. This is
        separate from the filesystem <strong>Cloud backup mirror</strong> below. Live vault stays on local disk.
      </p>
      <p className="muted">
        For each provider: open the setup link, create an app, paste the App key / Client ID here, then Connect to approve
        access in your browser.
      </p>
      <p className="muted">
        Status: {healthLabel(agg)}
        {summary?.health.enabledProviderCount
          ? ` · ${summary.health.enabledProviderCount} provider${summary.health.enabledProviderCount === 1 ? '' : 's'} enabled`
          : ' · no providers enabled'}
        {running ? ' · backup running…' : ''}
        {summary?.passwordRequired ? ' · set an unlock password before encrypted kits can upload' : ''}
        {!summary?.encryptionReady && !summary?.passwordRequired
          ? ' · unlock password recommended for encrypted Move Kits'
          : ''}
      </p>
      {(onHint || summary?.activeOperation) && (
        <p className="muted" role="status">
          {running
            ? `Run in progress: ${summary?.activeOperation?.phase || 'starting'}…`
            : summary?.state.lastRunFinishedUtc
              ? `Last run finished: ${formatUtc(summary.state.lastRunFinishedUtc)}`
              : null}
        </p>
      )}

      <label className="field">
        Interval (hours)
        <input
          type="number"
          min={1}
          max={168}
          value={intervalHours}
          disabled={busy}
          onChange={(e) => setIntervalHours(Number(e.target.value) || 24)}
        />
      </label>
      <label className="field">
        Versions to keep (per provider)
        <input
          type="number"
          min={1}
          max={30}
          value={versionsToKeep}
          disabled={busy}
          onChange={(e) => setVersionsToKeep(Number(e.target.value) || 3)}
        />
      </label>
      <label className="field checkbox-row">
        <input
          type="checkbox"
          checked={includePlainVaultZip}
          disabled={busy}
          onChange={(e) => onPlainZipToggle(e.target.checked)}
        />
        Also upload a readable vault-only ZIP (unencrypted Markdown + assets)
      </label>
      <p className="muted">
        Default off. Readable ZIPs roughly double cloud storage per generation and are for emergency note recovery only —
        restore of a full install still uses the encrypted Move Kit.
      </p>

      <div className="cloud-backup-providers" role="list">
        {CLOUD_PROVIDERS.map((p) => {
          const st = findProviderStatus(summary?.state, p.kind)
          const cfg = findProviderSettings(summary?.settings, p.kind)
          const draft = oauthDraft[p.kind]
          const hasClientId = Boolean(draft.clientId.trim() || cfg?.oauthClientId)
          const connected = st?.connectionState === 'connected'
          const needsReconnect = st?.connectionState === 'reconnectRequired'
          const account =
            cfg?.accountDisplayName || cfg?.accountEmail
              ? [cfg?.accountDisplayName, cfg?.accountEmail].filter(Boolean).join(' · ')
              : ''
          const quotaParts: string[] = []
          const used = formatBytes(st?.quotaUsedBytes)
          const total = formatBytes(st?.quotaTotalBytes)
          const remaining = formatBytes(st?.quotaRemainingBytes)
          if (used && total) quotaParts.push(`${used} used of ${total}`)
          else if (remaining) quotaParts.push(`${remaining} free`)
          else if (total) quotaParts.push(`${total} quota`)
          const hasStoredSecret = Boolean(cfg?.oauthClientSecret)

          return (
            <div key={p.kind} className="cloud-backup-provider-card" role="listitem">
              <div className="cloud-backup-provider-head">
                <strong>{p.label}</strong>
                <span className="muted">
                  {connectionLabel(st?.connectionState)} · {healthLabel(st?.health)}
                </span>
              </div>
              <p className="muted">
                <a href={p.setupUrl} target="_blank" rel="noopener noreferrer">
                  Open {p.label} app setup
                </a>
                {p.permissionsUrl && (
                  <>
                    {' · '}
                    <a href={p.permissionsUrl} target="_blank" rel="noopener noreferrer">
                      API permissions
                    </a>
                  </>
                )}
                {' · '}
                register redirect URI <code>{p.redirectUri}</code>
              </p>
              <p className="muted">{p.setupHint}</p>
              <label className="field">
                {p.clientIdLabel}
                <input
                  value={draft.clientId}
                  disabled={busy}
                  autoComplete="off"
                  spellCheck={false}
                  placeholder={p.clientIdPlaceholder}
                  onChange={(e) => updateDraft(p.kind, { clientId: e.target.value })}
                />
              </label>
              <label className="field">
                Client secret (optional)
                <input
                  type="password"
                  value={draft.clientSecret}
                  disabled={busy}
                  autoComplete="off"
                  placeholder={hasStoredSecret ? '(saved — leave blank to keep)' : 'Usually blank for public/PKCE apps'}
                  onChange={(e) => updateDraft(p.kind, { clientSecret: e.target.value })}
                />
              </label>
              {account && <p className="muted">Account: {account}</p>}
              {st?.lastFailureMessage && st.lastFailureCode && st.lastFailureCode !== 'none' && (
                <p className="warn" role="status">
                  {st.lastFailureMessage}
                </p>
              )}
              <p className="muted">{artifactLine('Move Kit', st?.moveKit, true, connected)}</p>
              <p className="muted">{artifactLine('Vault ZIP', st?.vaultZip, includePlainVaultZip, connected)}</p>
              {quotaParts.length > 0 && <p className="muted">Storage: {quotaParts.join(' · ')}</p>}
              {st?.nextDueUtc && connected && <p className="muted">Next due: {formatUtc(st.nextDueUtc)}</p>}
              <div className="modal-actions">
                <button type="button" className="ghost" disabled={busy} onClick={() => void saveProviderOauth(p.kind)}>
                  Save app settings
                </button>
                {!connected && !needsReconnect && (
                  <button
                    type="button"
                    className="primary"
                    disabled={busy || !hasClientId}
                    onClick={() => void connect(p.kind)}
                  >
                    Connect
                  </button>
                )}
                {needsReconnect && (
                  <button
                    type="button"
                    className="primary"
                    disabled={busy || !hasClientId}
                    onClick={() => void connect(p.kind)}
                  >
                    Reconnect
                  </button>
                )}
                {(connected || needsReconnect) && (
                  <>
                    <button type="button" className="ghost" disabled={busy} onClick={() => void runNow(p.kind)}>
                      Backup now
                    </button>
                    <button type="button" className="ghost" disabled={busy} onClick={() => void runNow(p.kind)}>
                      Retry
                    </button>
                    <button type="button" className="ghost" disabled={busy} onClick={() => void disconnect(p.kind)}>
                      Disconnect
                    </button>
                  </>
                )}
              </div>
            </div>
          )
        })}
      </div>

      <div className="modal-actions">
        <button type="button" className="primary" disabled={busy} onClick={() => void save()}>
          Save cloud backup
        </button>
        <button type="button" className="ghost" disabled={busy} onClick={() => void runNow()}>
          Run now
        </button>
      </div>
    </>
  )
}
