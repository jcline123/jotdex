import { useEffect, useState } from 'react'
import {
  type CloudBackupHealth,
  type CloudBackupHealthLevel,
  fetchCloudBackupHealth,
  healthLabel,
  runCloudBackup,
} from './cloudBackupApi'

type Props = {
  onOpenSettings?: () => void
  onRetry?: () => void | Promise<void>
}

function shouldShow(level: CloudBackupHealthLevel | undefined): boolean {
  if (!level) return false
  return level !== 'healthy' && level !== 'notConfigured'
}

function bannerTone(level: CloudBackupHealthLevel): 'warn' | 'error' | 'info' {
  if (level === 'error') return 'error'
  if (level === 'warning') return 'warn'
  return 'info'
}

export function CloudBackupHealthBanner({ onOpenSettings, onRetry }: Props) {
  const [health, setHealth] = useState<CloudBackupHealth | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    async function poll() {
      try {
        const h = await fetchCloudBackupHealth()
        if (!cancelled) setHealth(h)
      } catch {
        if (!cancelled) setHealth(null)
      }
    }
    void poll()
    const id = window.setInterval(() => void poll(), 60_000)
    return () => {
      cancelled = true
      window.clearInterval(id)
    }
  }, [])

  if (!health || !shouldShow(health.aggregateHealth)) return null

  const tone = bannerTone(health.aggregateHealth)
  const problems = health.providers.filter(
    (p) => p.health === 'error' || p.health === 'warning' || p.connectionState === 'reconnectRequired',
  )
  const detail =
    problems.length > 0
      ? problems
          .map((p) => {
            const name =
              p.provider === 'googleDrive' ? 'Google Drive' : p.provider === 'oneDrive' ? 'OneDrive' : 'Dropbox'
            return `${name}: ${p.message || healthLabel(p.health)}`
          })
          .join(' · ')
      : health.running
        ? 'A cloud backup is in progress.'
        : 'Cloud backup needs attention.'

  async function retry() {
    setBusy(true)
    try {
      if (onRetry) await onRetry()
      else await runCloudBackup()
      const h = await fetchCloudBackupHealth()
      setHealth(h)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      className={`cloud-backup-health-banner cloud-backup-health-banner-${tone}`}
      role="status"
      aria-live="polite"
    >
      <p>
        <strong>Cloud backup: {healthLabel(health.aggregateHealth)}</strong>
        {detail ? ` — ${detail}` : ''}
      </p>
      <div className="modal-actions">
        <button type="button" className="primary" onClick={() => onOpenSettings?.()}>
          Open settings
        </button>
        <button type="button" className="ghost" disabled={busy || health.running} onClick={() => void retry()}>
          Retry
        </button>
      </div>
    </div>
  )
}
