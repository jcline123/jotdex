/** Typed helpers for `/api/cloud-backup` (camelCase JSON + string enums). */

export type CloudProviderKind = 'oneDrive' | 'googleDrive' | 'dropbox'

export type CloudConnectionState =
  | 'notConfigured'
  | 'connecting'
  | 'connected'
  | 'reconnectRequired'
  | 'disconnected'
  | 'configurationUnavailable'

export type CloudBackupHealthLevel =
  | 'notConfigured'
  | 'pending'
  | 'healthy'
  | 'warning'
  | 'error'
  | 'running'

export type CloudBackupFailureCode =
  | 'none'
  | 'encryptionRequired'
  | 'localArtifactCreationFailed'
  | 'providerConfigurationMissing'
  | 'authenticationRequired'
  | 'authorizationDenied'
  | 'tokenRefreshFailed'
  | 'quotaExceeded'
  | 'networkUnavailable'
  | 'rateLimited'
  | 'providerUnavailable'
  | 'uploadFailed'
  | 'remoteFileMissing'
  | 'remoteSizeMismatch'
  | 'remoteChecksumMismatch'
  | 'fullVerificationFailed'
  | 'retentionFailed'
  | 'cancelled'
  | 'unknown'
  | 'snapshotFailed'
  | 'vaultZipValidationFailed'

export type CloudProviderSettings = {
  provider: CloudProviderKind
  enabled: boolean
  accountId?: string | null
  accountDisplayName?: string | null
  accountEmail?: string | null
  remoteRootId?: string | null
  remoteRootDisplayPath?: string | null
  oauthClientId?: string | null
  oauthClientSecret?: string | null
  oauthRedirectUri?: string | null
}

export type CloudBackupSettings = {
  schemaVersion: number
  backupSetId: string
  backupSetName: string
  intervalHours: number
  versionsToKeep: number
  fullVerificationIntervalDays: number
  includePlainVaultZip: boolean
  providers: CloudProviderSettings[]
}

export type CloudArtifactBackupStatus = {
  artifactType: string
  required: boolean
  lastAttemptUtc?: string | null
  lastUploadUtc?: string | null
  lastVerifiedUtc?: string | null
  lastFullVerificationUtc?: string | null
  lastFileName?: string | null
  lastRemoteFileId?: string | null
  lastRemoteSizeBytes?: number | null
  lastSha256?: string | null
  lastFailureCode?: CloudBackupFailureCode
  lastFailureMessage?: string | null
}

export type CloudProviderBackupStatus = {
  provider: CloudProviderKind
  connectionState: CloudConnectionState
  health: CloudBackupHealthLevel
  lastAttemptUtc?: string | null
  lastUploadUtc?: string | null
  lastVerifiedUtc?: string | null
  lastFullVerificationUtc?: string | null
  nextDueUtc?: string | null
  lastArtifactName?: string | null
  lastRemoteFileId?: string | null
  lastRemoteSizeBytes?: number | null
  lastFailureCode?: CloudBackupFailureCode
  lastFailureMessage?: string | null
  consecutiveFailures?: number
  quotaTotalBytes?: number | null
  quotaUsedBytes?: number | null
  quotaRemainingBytes?: number | null
  moveKit?: CloudArtifactBackupStatus
  vaultZip?: CloudArtifactBackupStatus
}

export type CloudBackupRuntimeState = {
  schemaVersion: number
  activeOperationId?: string | null
  lastRunStartedUtc?: string | null
  lastRunFinishedUtc?: string | null
  lastRunId?: string | null
  providers: CloudProviderBackupStatus[]
}

export type CloudBackupOperation = {
  operationId: string
  runId: string
  trigger: string
  providerFilter?: CloudProviderKind | null
  startedUtc: string
  finishedUtc?: string | null
  running: boolean
  phase: string
  error?: string | null
  providerPhases?: Record<string, string>
}

export type CloudProviderHealthItem = {
  provider: CloudProviderKind
  health: CloudBackupHealthLevel
  connectionState: CloudConnectionState
  failureCode: CloudBackupFailureCode
  message?: string | null
  lastVerifiedUtc?: string | null
}

export type CloudBackupHealth = {
  aggregateHealth: CloudBackupHealthLevel
  enabledProviderCount: number
  running: boolean
  providers: CloudProviderHealthItem[]
}

export type CloudBackupSummary = {
  settings: CloudBackupSettings
  health: CloudBackupHealth
  state: CloudBackupRuntimeState
  activeOperation?: CloudBackupOperation | null
  providerAvailableInBuild: Record<string, boolean>
  encryptionReady: boolean
  passwordRequired: boolean
}

export type CloudOAuthAttempt = {
  attemptId: string
  provider: CloudProviderKind
  createdUtc: string
  expiresUtc: string
  completed: boolean
  success: boolean
  error?: string | null
  authorizeUrl?: string | null
  accountDisplayName?: string | null
  accountEmail?: string | null
}

export const CLOUD_PROVIDERS: {
  kind: CloudProviderKind
  label: string
  path: string
  clientIdLabel: string
  clientIdPlaceholder: string
  setupUrl: string
  setupHint: string
  redirectUri: string
  permissionsUrl?: string
}[] = [
  {
    kind: 'dropbox',
    label: 'Dropbox',
    path: 'dropbox',
    clientIdLabel: 'App key',
    clientIdPlaceholder: 'Paste Dropbox app key',
    setupUrl: 'https://www.dropbox.com/developers/apps',
    setupHint:
      'Create app → Scoped access → App folder. Under OAuth 2, add the redirect URI below. Copy the App key here.',
    redirectUri: 'http://127.0.0.1:5180/oauth/dropbox',
  },
  {
    kind: 'googleDrive',
    label: 'Google Drive',
    path: 'googleDrive',
    clientIdLabel: 'Client ID',
    clientIdPlaceholder: 'Paste Google OAuth client ID',
    setupUrl: 'https://console.cloud.google.com/apis/credentials',
    setupHint:
      'Create OAuth client (Desktop or Web) with the redirect URI below. Enable Google Drive API. Copy the Client ID here. Use a public client with PKCE when possible (secret optional).',
    redirectUri: 'http://127.0.0.1:5180/oauth/google',
  },
  {
    kind: 'oneDrive',
    label: 'OneDrive',
    path: 'oneDrive',
    clientIdLabel: 'Application (client) ID',
    clientIdPlaceholder: 'Paste Azure app client ID',
    setupUrl: 'https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade',
      setupHint:
      'Register an app (personal Microsoft accounts). Add the redirect URI as Mobile and desktop / public client. API permissions → Microsoft Graph → delegated: Files.ReadWrite.AppFolder, User.Read, offline_access — then Grant admin consent if shown. Copy the Application (client) ID here.',
    redirectUri: 'http://127.0.0.1:5180/oauth/onedrive',
    permissionsUrl:
      'https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade',
  },
]

const jsonHeaders = { 'Content-Type': 'application/json' }

export function providerAvailableInBuild(
  map: Record<string, boolean> | undefined,
  kind: CloudProviderKind,
): boolean {
  if (!map) return false
  for (const [k, v] of Object.entries(map)) {
    if (k.toLowerCase() === kind.toLowerCase()) return v === true
  }
  return false
}

export function findProviderStatus(
  state: CloudBackupRuntimeState | undefined,
  kind: CloudProviderKind,
): CloudProviderBackupStatus | undefined {
  return state?.providers?.find((p) => p.provider === kind)
}

export function findProviderSettings(
  settings: CloudBackupSettings | undefined,
  kind: CloudProviderKind,
): CloudProviderSettings | undefined {
  return settings?.providers?.find((p) => p.provider === kind)
}

export function formatBytes(n: number | null | undefined): string {
  if (n == null || !Number.isFinite(n) || n < 0) return ''
  if (n < 1024) return `${n} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let v = n / 1024
  let i = 0
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024
    i++
  }
  return `${v < 10 ? v.toFixed(1) : Math.round(v)} ${units[i]}`
}

export function formatUtc(value?: string | null): string {
  if (!value) return '—'
  const t = Date.parse(value)
  if (!Number.isFinite(t)) return '—'
  return new Date(t).toLocaleString()
}

export function healthLabel(level: CloudBackupHealthLevel | undefined): string {
  switch (level) {
    case 'healthy':
      return 'Healthy'
    case 'warning':
      return 'Warning'
    case 'error':
      return 'Error'
    case 'pending':
      return 'Pending'
    case 'running':
      return 'Running'
    case 'notConfigured':
      return 'Not configured'
    default:
      return 'Unknown'
  }
}

export function connectionLabel(state: CloudConnectionState | undefined): string {
  switch (state) {
    case 'connected':
      return 'Connected'
    case 'connecting':
      return 'Connecting…'
    case 'reconnectRequired':
      return 'Reconnect required'
    case 'disconnected':
      return 'Disconnected'
    case 'configurationUnavailable':
      return 'Unavailable in this build'
    case 'notConfigured':
      return 'Not connected'
    default:
      return 'Unknown'
  }
}

export function extractAuthorizationCode(raw: string): string {
  const trimmed = raw.trim()
  if (!trimmed) return ''
  try {
    if (trimmed.includes('://') || trimmed.startsWith('?') || trimmed.includes('code=')) {
      const url = trimmed.includes('://')
        ? new URL(trimmed)
        : new URL(trimmed.startsWith('?') ? trimmed : `?${trimmed}`, 'http://local')
      const code = url.searchParams.get('code')
      if (code) return code
    }
  } catch {
    /* plain code */
  }
  return trimmed
}

export async function fetchCloudBackupSummary(): Promise<CloudBackupSummary> {
  const res = await fetch('/api/cloud-backup', { credentials: 'same-origin' })
  if (!res.ok) throw new Error('Could not load cloud backup status')
  return (await res.json()) as CloudBackupSummary
}

export async function fetchCloudBackupHealth(): Promise<CloudBackupHealth> {
  const res = await fetch('/api/cloud-backup/health', { credentials: 'same-origin' })
  if (!res.ok) throw new Error('Could not load cloud backup health')
  return (await res.json()) as CloudBackupHealth
}

export async function saveCloudBackupSettings(body: {
  backupSetName?: string
  intervalHours?: number
  versionsToKeep?: number
  fullVerificationIntervalDays?: number
  includePlainVaultZip?: boolean
  providers?: {
    provider: CloudProviderKind
    enabled?: boolean
    oauthClientId?: string
    oauthClientSecret?: string
    oauthRedirectUri?: string
    clearOAuthClientSecret?: boolean
  }[]
}): Promise<{ success: boolean; settings?: CloudBackupSettings; error?: string }> {
  const res = await fetch('/api/cloud-backup/settings', {
    method: 'PUT',
    credentials: 'same-origin',
    headers: jsonHeaders,
    body: JSON.stringify(body),
  })
  const data = (await res.json()) as { success?: boolean; settings?: CloudBackupSettings; error?: string }
  if (!res.ok) return { success: false, error: data.error ?? 'Could not save settings' }
  return { success: true, settings: data.settings }
}

export async function runCloudBackup(provider?: CloudProviderKind): Promise<{
  accepted: boolean
  operationId?: string
  runId?: string
  error?: string
}> {
  const res = await fetch('/api/cloud-backup/run', {
    method: 'POST',
    credentials: 'same-origin',
    headers: jsonHeaders,
    body: provider ? JSON.stringify({ provider }) : undefined,
  })
  const data = (await res.json()) as {
    accepted?: boolean
    operationId?: string
    runId?: string
    error?: string
  }
  if (!res.ok) return { accepted: false, error: data.error ?? 'Could not start backup' }
  return { accepted: true, operationId: data.operationId, runId: data.runId }
}

export async function fetchCloudBackupOperation(operationId: string): Promise<CloudBackupOperation | null> {
  const res = await fetch(`/api/cloud-backup/operations/${encodeURIComponent(operationId)}`, {
    credentials: 'same-origin',
  })
  if (res.status === 404) return null
  if (!res.ok) throw new Error('Could not load operation')
  return (await res.json()) as CloudBackupOperation
}

export async function connectCloudProvider(provider: CloudProviderKind): Promise<{
  success: boolean
  attempt?: CloudOAuthAttempt
  error?: string
  connectionState?: string
}> {
  const path = CLOUD_PROVIDERS.find((p) => p.kind === provider)?.path ?? provider
  const res = await fetch(`/api/cloud-backup/providers/${encodeURIComponent(path)}/connect`, {
    method: 'POST',
    credentials: 'same-origin',
  })
  const data = (await res.json()) as {
    success?: boolean
    attempt?: CloudOAuthAttempt
    error?: string
    connectionState?: string
  }
  if (!res.ok) {
    return {
      success: false,
      attempt: data.attempt,
      error: data.error ?? 'Connect failed',
      connectionState: data.connectionState,
    }
  }
  return { success: true, attempt: data.attempt }
}

export async function getCloudProviderConnectAttempt(
  provider: CloudProviderKind,
  attemptId: string,
): Promise<CloudOAuthAttempt | null> {
  const path = CLOUD_PROVIDERS.find((p) => p.kind === provider)?.path ?? provider
  const res = await fetch(
    `/api/cloud-backup/providers/${encodeURIComponent(path)}/connect/${encodeURIComponent(attemptId)}`,
    { credentials: 'same-origin' },
  )
  if (res.status === 404) return null
  if (!res.ok) throw new Error('Could not load connect attempt')
  const data = (await res.json()) as { attempt?: CloudOAuthAttempt }
  return data.attempt ?? null
}

export async function completeCloudProviderConnect(
  provider: CloudProviderKind,
  attemptId: string,
  code: string,
): Promise<{ success: boolean; attempt?: CloudOAuthAttempt; error?: string }> {
  const path = CLOUD_PROVIDERS.find((p) => p.kind === provider)?.path ?? provider
  const res = await fetch(
    `/api/cloud-backup/providers/${encodeURIComponent(path)}/connect/${encodeURIComponent(attemptId)}/complete`,
    {
      method: 'POST',
      credentials: 'same-origin',
      headers: jsonHeaders,
      body: JSON.stringify({ code }),
    },
  )
  const data = (await res.json()) as { success?: boolean; attempt?: CloudOAuthAttempt; error?: string }
  if (!res.ok || !data.success) {
    return {
      success: false,
      attempt: data.attempt,
      error: data.attempt?.error ?? data.error ?? 'Could not complete connect',
    }
  }
  return { success: true, attempt: data.attempt }
}

export async function disconnectCloudProvider(provider: CloudProviderKind): Promise<{ success: boolean; error?: string }> {
  const path = CLOUD_PROVIDERS.find((p) => p.kind === provider)?.path ?? provider
  const res = await fetch(`/api/cloud-backup/providers/${encodeURIComponent(path)}/disconnect`, {
    method: 'POST',
    credentials: 'same-origin',
  })
  const data = (await res.json()) as { success?: boolean; error?: string }
  if (!res.ok) return { success: false, error: data.error ?? 'Disconnect failed' }
  return { success: true }
}

export async function retryCloudProvider(provider: CloudProviderKind): Promise<{
  accepted: boolean
  operationId?: string
  runId?: string
  error?: string
}> {
  const path = CLOUD_PROVIDERS.find((p) => p.kind === provider)?.path ?? provider
  const res = await fetch(`/api/cloud-backup/providers/${encodeURIComponent(path)}/retry`, {
    method: 'POST',
    credentials: 'same-origin',
  })
  const data = (await res.json()) as {
    accepted?: boolean
    operationId?: string
    runId?: string
    error?: string
  }
  if (!res.ok) return { accepted: false, error: data.error ?? 'Retry failed' }
  return { accepted: true, operationId: data.operationId, runId: data.runId }
}

export function parseOauthCallbackPath(pathname: string): CloudProviderKind | null {
  const p = pathname.replace(/\/+$/, '').toLowerCase()
  if (p === '/oauth/dropbox') return 'dropbox'
  if (p === '/oauth/google' || p === '/oauth/googledrive') return 'googleDrive'
  if (p === '/oauth/onedrive') return 'oneDrive'
  return null
}
