import { useCallback, useEffect, useState } from 'react'

type CalendarStatus = {
  enabled: boolean
  serverUrl: string
  username: string
  calendarUrl: string
  calendarName: string
  publicBaseUrl: string
  hasPassword: boolean
  lastSyncUtc?: string | null
  lastError?: string | null
  lastUpserted: number
  lastRemoved: number
  hint?: string
}

type CalendarChoice = { url: string; name: string }

export function CalendarSettings() {
  const [status, setStatus] = useState<CalendarStatus | null>(null)
  const [enabled, setEnabled] = useState(false)
  const [serverUrl, setServerUrl] = useState('https://caldav.icloud.com/')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [calendarUrl, setCalendarUrl] = useState('')
  const [calendarName, setCalendarName] = useState('')
  const [publicBaseUrl, setPublicBaseUrl] = useState('')
  const [choices, setChoices] = useState<CalendarChoice[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const apply = useCallback((s: CalendarStatus) => {
    setStatus(s)
    setEnabled(s.enabled)
    setServerUrl(s.serverUrl || 'https://caldav.icloud.com/')
    setUsername(s.username || '')
    setCalendarUrl(s.calendarUrl || '')
    setCalendarName(s.calendarName || '')
    setPublicBaseUrl(s.publicBaseUrl || '')
  }, [])

  useEffect(() => {
    let cancelled = false
    fetch('/api/settings/calendar', { credentials: 'same-origin' })
      .then(async (r) => {
        if (!r.ok) throw new Error('Could not load calendar settings')
        return (await r.json()) as CalendarStatus
      })
      .then((s) => {
        if (!cancelled) apply(s)
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message)
      })
    return () => {
      cancelled = true
    }
  }, [apply])

  function payload() {
    return {
      enabled,
      serverUrl,
      username,
      calendarUrl,
      calendarName,
      publicBaseUrl,
      password: password.trim() ? password : undefined,
    }
  }

  async function findCalendars() {
    setBusy('find')
    setError(null)
    setMessage(null)
    try {
      const res = await fetch('/api/settings/calendar/discover', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ serverUrl, username, password: password.trim() ? password : undefined }),
      })
      const body = (await res.json()) as { error?: string; calendars?: CalendarChoice[] }
      if (!res.ok) throw new Error(body.error || 'Could not list calendars')
      const list = body.calendars ?? []
      if (!calendarUrl && list.length === 1) {
        setCalendarUrl(list[0].url)
        setCalendarName(list[0].name)
        setChoices(list)
      } else if (calendarUrl && !list.some((c) => c.url === calendarUrl)) {
        setChoices([{ url: calendarUrl, name: calendarName || 'Saved calendar' }, ...list])
      } else {
        setChoices(list)
      }
      setMessage(list.length === 1 ? 'Found 1 calendar.' : `Found ${list.length} calendars. Pick the one that should receive todos.`)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not list calendars')
    } finally {
      setBusy(null)
    }
  }

  async function save() {
    setBusy('save')
    setError(null)
    setMessage(null)
    try {
      const res = await fetch('/api/settings/calendar', {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload()),
      })
      const body = (await res.json()) as { error?: string; status?: CalendarStatus }
      if (!res.ok) throw new Error(body.error || 'Could not save')
      if (body.status) apply(body.status)
      setPassword('')
      setMessage('Saved.')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save')
    } finally {
      setBusy(null)
    }
  }

  async function syncNow() {
    setBusy('sync')
    setError(null)
    setMessage(null)
    try {
      const saved = await fetch('/api/settings/calendar', {
        method: 'PUT',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload()),
      })
      const savedBody = (await saved.json()) as { error?: string; status?: CalendarStatus }
      if (!saved.ok) throw new Error(savedBody.error || 'Could not save')
      if (savedBody.status) apply(savedBody.status)
      setPassword('')
      if (!enabled) {
        setMessage('Saved. Turn on “Sync open todos” when you want items on the calendar.')
        return
      }
      const res = await fetch('/api/settings/calendar/sync', { method: 'POST', credentials: 'same-origin' })
      const body = (await res.json()) as { error?: string; upserted?: number; removed?: number; status?: CalendarStatus }
      if (body.status) apply(body.status)
      if (!res.ok) throw new Error(body.error || 'Sync failed')
      setMessage(`Synced ${body.upserted ?? 0} open todos. Removed ${body.removed ?? 0}.`)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sync failed')
    } finally {
      setBusy(null)
    }
  }

  const selected = choices.find((c) => c.url === calendarUrl)
  const last = status?.lastSyncUtc ? new Date(status.lastSyncUtc).toLocaleString() : null

  return (
    <>
      <h2 className="settings-section settings-section-first">Apple Calendar</h2>
      <p className="lede">
        Open todos become all-day events titled <code>todo:</code> on the calendar you pick. Todos without a future due date stay on today and move to the next day
        until you complete or delete them. A due date in the future stays on that day. An all-day event you add on your phone titled <code>todo: something</code> is
        picked up on the next sync and shows a Calendar chip. Create an app-specific password at appleid.apple.com (Sign-In and Security). Jotdex stores it with the
        other app secrets, not in your notes.
      </p>
      <label className="field">
        <span>
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Sync open todos
        </span>
      </label>
      <label className="field">
        Apple ID
        <input value={username} autoComplete="username" onChange={(e) => setUsername(e.target.value)} placeholder="name@icloud.com" />
      </label>
      <label className="field">
        App-specific password
        <input
          type="password"
          value={password}
          autoComplete="new-password"
          onChange={(e) => setPassword(e.target.value)}
          placeholder={status?.hasPassword ? 'Saved — leave blank to keep it' : 'xxxx-xxxx-xxxx-xxxx'}
        />
      </label>
      <label className="field">
        CalDAV server
        <input value={serverUrl} onChange={(e) => setServerUrl(e.target.value)} spellCheck={false} />
      </label>
      <p className="field-hint">Leave the server as caldav.icloud.com for iCloud. Another CalDAV host works if it accepts this same sign-in.</p>
      <div className="row" style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', margin: '0.5rem 0 0.75rem' }}>
        <button type="button" className="ghost" disabled={busy !== null} onClick={() => void findCalendars()}>
          {busy === 'find' ? 'Looking…' : 'Find calendars'}
        </button>
      </div>
      <label className="field">
        Calendar for todos
        <select
          value={calendarUrl}
          onChange={(e) => {
            const url = e.target.value
            setCalendarUrl(url)
            const choice = choices.find((c) => c.url === url)
            setCalendarName(choice?.name || (url === status?.calendarUrl ? status.calendarName : ''))
          }}
        >
          <option value="">Choose a calendar</option>
          {(choices.length > 0 ? choices : calendarUrl ? [{ url: calendarUrl, name: calendarName || 'Saved calendar' }] : []).map((c) => (
            <option key={c.url} value={c.url}>
              {c.name}
            </option>
          ))}
        </select>
      </label>
      {selected && <p className="field-hint">{selected.name}</p>}
      <label className="field">
        Jotdex address
        <input
          value={publicBaseUrl}
          onChange={(e) => setPublicBaseUrl(e.target.value)}
          placeholder="jotdex.cline.xyz"
          spellCheck={false}
          autoCapitalize="off"
        />
      </label>
      <p className="field-hint">
        Todos that live in a note get a link to that note on the calendar event. Use the address you open in the browser, without a path.
      </p>
      <div className="row" style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginTop: '0.75rem' }}>
        <button type="button" className="ghost" disabled={busy !== null} onClick={() => void save()}>
          {busy === 'save' ? 'Saving…' : 'Save'}
        </button>
        <button type="button" className="ghost" disabled={busy !== null} onClick={() => void syncNow()}>
          {busy === 'sync' ? 'Syncing…' : 'Sync now'}
        </button>
      </div>
      {last && (
        <p className="field-hint">
          Last sync {last}
          {status?.lastError ? '' : ` — ${status?.lastUpserted ?? 0} on the calendar, ${status?.lastRemoved ?? 0} removed.`}
        </p>
      )}
      {message && <p className="field-hint">{message}</p>}
      {error && <p className="warn">{error}</p>}
      {status?.lastError && !error && <p className="warn">{status.lastError}</p>}
    </>
  )
}
