import { useCallback, useEffect, useRef, useState } from 'react'
import DOMPurify from 'dompurify'
import './App.css'
import { NoteEditor } from './NoteEditor'
import { joinFrontMatter, sameMarkdown, splitFrontMatter } from './frontMatter'
import { looksUnsafeForVisual } from './unsafeMarkdown'
import { FirstRunWizard, LoginScreen } from './AuthScreens'

function countRemoteImages(markdown: string): number {
  const re = /!\[[^\]]*\]\((https?:\/\/[^)\s]+)\)/gi
  let n = 0
  while (re.exec(markdown) !== null) n++
  return n
}

type FolderNode = {
  id: string
  name: string
  relativePath: string
  children: FolderNode[]
}

type NoteSummary = {
  id: string
  title: string
  relativePath: string
  folderPath: string
  tags: string[]
  modified?: string
  hasAttachments: boolean
}

type NoteDetail = {
  id: string
  title: string
  relativePath: string
  folderPath: string
  markdown: string
  html: string
  etag: string
  tags: string[]
  modified?: string
  attachments: { id: string; fileName: string; contentType: string }[]
  htmlSidecars: { fileName: string; attachmentId: string }[]
}

type VaultInfo = {
  configured: boolean
  name?: string
  noteCount?: number
  folderCount?: number
}

type SearchHit = {
  noteId: string
  title: string
  folderPath: string
  relativePath: string
  snippet?: string
  tags: string[]
}

type BrowseEntry = { name: string; path: string; type: string }

type AuthInfo = {
  setupComplete: boolean
  authenticated: boolean
  authRequired: boolean
  setupRequired?: boolean
  authEnforced?: boolean
  username?: string
  displayName?: string
  developmentBypass?: boolean
}

function loadCollapsedFolders(): Set<string> {
  try {
    const raw = localStorage.getItem('jotdex.collapsedFolders')
    if (!raw) return new Set()
    const arr = JSON.parse(raw) as unknown
    return Array.isArray(arr) ? new Set(arr.filter((x): x is string => typeof x === 'string')) : new Set()
  } catch {
    return new Set()
  }
}

function saveCollapsedFolders(paths: Set<string>) {
  localStorage.setItem('jotdex.collapsedFolders', JSON.stringify([...paths]))
}

function FolderTree({
  node,
  depth,
  selected,
  onSelect,
  collapsed,
  onToggle,
}: {
  node: FolderNode
  depth: number
  selected: string
  onSelect: (path: string) => void
  collapsed: Set<string>
  onToggle: (path: string) => void
}) {
  const hasKids = node.children.length > 0
  // Root (empty path) stays expanded; collapse applies to real folders
  const isCollapsed = node.relativePath !== '' && collapsed.has(node.relativePath)
  const showKids = hasKids && !isCollapsed

  return (
    <div className="tree-branch">
      <div className={`tree-row${selected === node.relativePath ? ' active' : ''}`} style={{ paddingLeft: `${0.35 + depth * 0.85}rem` }}>
        {hasKids && node.relativePath !== '' ? (
          <button
            type="button"
            className="tree-twist"
            aria-label={isCollapsed ? 'Expand folder' : 'Collapse folder'}
            aria-expanded={!isCollapsed}
            onClick={(e) => {
              e.stopPropagation()
              onToggle(node.relativePath)
            }}
          >
            {isCollapsed ? '▸' : '▾'}
          </button>
        ) : (
          <span className="tree-twist spacer" aria-hidden />
        )}
        <button type="button" className="tree-item" onClick={() => onSelect(node.relativePath)}>
          {node.name}
        </button>
      </div>
      {showKids &&
        node.children.map((c) => (
          <FolderTree
            key={c.id}
            node={c}
            depth={depth + 1}
            selected={selected}
            onSelect={onSelect}
            collapsed={collapsed}
            onToggle={onToggle}
          />
        ))}
    </div>
  )
}

function App() {
  const [auth, setAuth] = useState<AuthInfo | null>(null)
  const [vault, setVault] = useState<VaultInfo | null>(null)
  const [tree, setTree] = useState<FolderNode | null>(null)
  const [folder, setFolder] = useState('')
  const [collapsedFolders, setCollapsedFolders] = useState<Set<string>>(() => loadCollapsedFolders())
  const [notes, setNotes] = useState<NoteSummary[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [note, setNote] = useState<NoteDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showSource, setShowSource] = useState(false)
  const [draft, setDraft] = useState('')
  const [frontMatter, setFrontMatter] = useState('')
  const [etag, setEtag] = useState('')
  const [saveStatus, setSaveStatus] = useState<'saved' | 'editing' | 'saving' | 'conflict' | 'error'>('saved')
  const [history, setHistory] = useState<
    { snapshotId: string; createdUtc: string; summary?: string; preview?: string; sizeBytes?: number }[]
  >([])
  const [historyOpen, setHistoryOpen] = useState(false)
  const saveTimer = useRef<number | null>(null)
  const [conflictDisk, setConflictDisk] = useState<NoteDetail | null>(null)
  const [sourceForced, setSourceForced] = useState<string | null>(null)
  const [localizeStatus, setLocalizeStatus] = useState<string | null>(null)
  const etagRef = useRef('')
  const draftRef = useRef('')
  const frontMatterRef = useRef('')
  const baselineRef = useRef('')
  const savingRef = useRef(false)
  const saveStatusRef = useRef<'saved' | 'editing' | 'saving' | 'conflict' | 'error'>('saved')
  const [editorEpoch, setEditorEpoch] = useState(0)
  saveStatusRef.current = saveStatus

  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<SearchHit[]>([])
  const [searchOpen, setSearchOpen] = useState(false)
  const [searchMeta, setSearchMeta] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)

  const [settingsOpen, setSettingsOpen] = useState(false)
  const [vaultPathInput, setVaultPathInput] = useState('')
  const [browsePath, setBrowsePath] = useState('')
  const [browseParent, setBrowseParent] = useState<string | null>(null)
  const [browseEntries, setBrowseEntries] = useState<BrowseEntry[]>([])
  const [bindMode, setBindMode] = useState<'loopback' | 'lan'>('loopback')
  const [listenPort, setListenPort] = useState(5180)
  const [httpsPfxPath, setHttpsPfxPath] = useState('')
  const [httpsPfxPassword, setHttpsPfxPassword] = useState('')
  const [httpsPasswordConfigured, setHttpsPasswordConfigured] = useState(false)
  const [networkHint, setNetworkHint] = useState<string | null>(null)
  const [restartNeeded, setRestartNeeded] = useState(false)
  const [restartBusy, setRestartBusy] = useState(false)
  const [mirrorEnabled, setMirrorEnabled] = useState(false)
  const [mirrorDest, setMirrorDest] = useState('')
  const [mirrorInterval, setMirrorInterval] = useState(15)
  const [mirrorStatus, setMirrorStatus] = useState<string | null>(null)
  const [integrityReport, setIntegrityReport] = useState<string | null>(null)
  const [diagText, setDiagText] = useState<string | null>(null)
  const [logText, setLogText] = useState<string | null>(null)
  const [logPath, setLogPath] = useState<string | null>(null)
  const [autostartInfo, setAutostartInfo] = useState<{
    userStartupEnabled?: boolean
    userStartupPath?: string | null
    windowsService?: { installed?: boolean; status?: string | null; startType?: string | null }
    hint?: string
  } | null>(null)

  const loadAuth = useCallback(async () => {
    const a = (await fetch('/api/auth/status', { credentials: 'same-origin' }).then((r) => r.json())) as AuthInfo
    setAuth(a)
    return a
  }, [])

  const loadVault = useCallback(async () => {
    setError(null)
    const v = (await fetch('/api/vault', { credentials: 'same-origin' }).then((r) => r.json())) as VaultInfo
    setVault(v)
    const settings = (await fetch('/api/settings/vault', { credentials: 'same-origin' }).then((r) => r.json())) as {
      vaultPath?: string
    }
    if (settings.vaultPath) setVaultPathInput(settings.vaultPath)
    if (!v.configured) {
      setTree(null)
      setNotes([])
      return
    }
    const t = (await fetch('/api/tree', { credentials: 'same-origin' }).then((r) => r.json())) as FolderNode
    setTree(t)
  }, [])

  useEffect(() => {
    loadAuth()
      .then((a) => {
        if (a.setupRequired) return
        if (!a.authRequired || a.authenticated || a.developmentBypass) {
          return loadVault()
        }
      })
      .catch((e: Error) => setError(e.message))
  }, [loadAuth, loadVault])

  useEffect(() => {
    if (!vault?.configured) return
    const q = folder ? `?folder=${encodeURIComponent(folder)}` : ''
    fetch(`/api/notes${q}`)
      .then((r) => r.json())
      .then((data: NoteSummary[]) => {
        setNotes(data)
        if (data.length && !selectedId) setSelectedId(data[0].id)
      })
      .catch((e: Error) => setError(e.message))
  }, [folder, vault, selectedId])

  useEffect(() => {
    if (!selectedId) {
      setNote(null)
      return
    }
    fetch(`/api/notes/${selectedId}`)
      .then(async (r) => {
        if (!r.ok) throw new Error(`Note ${r.status}`)
        return r.json() as Promise<NoteDetail>
      })
      .then((n) => {
        setNote(n)
        const split = splitFrontMatter(n.markdown)
        setFrontMatter(split.frontMatter)
        setDraft(split.body)
        setEtag(n.etag)
        etagRef.current = n.etag
        draftRef.current = split.body
        frontMatterRef.current = split.frontMatter
        // Baseline must equal what the autosave effect computes (join of split parts),
        // so opening a note never looks like an unsaved edit.
        baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
        const unsafe = looksUnsafeForVisual(split.body)
        if (unsafe.unsafe) {
          setShowSource(true)
          setSourceForced(unsafe.reason ?? 'Opened in Source for safety.')
        } else {
          setShowSource(false)
          setSourceForced(null)
        }
        setSaveStatus('saved')
        setEditorEpoch((e) => e + 1)
        setSearchOpen(false)
        setConflictDisk(null)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [selectedId])

  useEffect(() => {
    if (!query.trim()) {
      setHits([])
      setSearchMeta('')
      return
    }
    const handle = window.setTimeout(() => {
      fetch(`/api/search?q=${encodeURIComponent(query)}`)
        .then((r) => r.json())
        .then((data: { mode: string; hits: SearchHit[]; warning?: string }) => {
          setHits(data.hits)
          setSearchMeta(`${data.mode} · ${data.hits.length} results${data.warning ? ` · ${data.warning}` : ''}`)
          setSearchOpen(true)
        })
        .catch(() => setError('Search failed'))
    }, 160)
    return () => window.clearTimeout(handle)
  }, [query])

  const saveNote = useCallback(
    async (bodyMarkdown: string, currentEtag: string, force = false, retry = 0) => {
      if (!selectedId) return
      if (savingRef.current && !force) {
        // A save is in flight — re-check once it should be done, don't stack saves
        if (saveTimer.current) window.clearTimeout(saveTimer.current)
        saveTimer.current = window.setTimeout(() => {
          const pending = joinFrontMatter(frontMatterRef.current, draftRef.current)
          if (sameMarkdown(pending, baselineRef.current)) {
            baselineRef.current = pending
            setSaveStatus('saved')
            return
          }
          void saveNote(draftRef.current, etagRef.current, false, 0)
        }, 800)
        return
      }
      savingRef.current = true
      setSaveStatus('saving')
      // Always use the latest known ETag — callers may pass a stale value
      const etagToSend = etagRef.current || currentEtag
      const markdown = joinFrontMatter(frontMatterRef.current, bodyMarkdown)
      try {
        const res = await fetch(`/api/notes/${selectedId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ markdown, etag: etagToSend, force }),
        })
        const data = await res.json()
        if (res.status === 409 || data.conflict) {
          const diskMarkdown = (data.note?.markdown as string | undefined) ?? ''
          const diskEtag = (data.etag as string) || data.note?.etag

          // Same document (exact or cosmetic TipTap differences) — adopt quietly
          if (
            sameMarkdown(diskMarkdown, markdown) ||
            sameMarkdown(diskMarkdown, baselineRef.current)
          ) {
            if (diskEtag) {
              setEtag(diskEtag)
              etagRef.current = diskEtag
            }
            baselineRef.current = diskMarkdown || markdown
            if (data.note) setNote({ ...data.note, markdown: diskMarkdown || markdown })
            setSaveStatus('saved')
            setConflictDisk(null)
            setError(null)

            // If the editor has moved on, save again with the fresh ETag — but only
            // if we actually got one, and never more than a few times (no hot loops).
            const latest = joinFrontMatter(frontMatterRef.current, draftRef.current)
            if (!sameMarkdown(latest, baselineRef.current) && diskEtag && retry < 3) {
              savingRef.current = false
              return await saveNote(draftRef.current, diskEtag, false, retry + 1)
            }
            return
          }

          // Stale ETag from overlapping autosave — retry with disk ETag (last-write-wins)
          if (!force && retry < 3 && diskEtag) {
            if (diskEtag) {
              setEtag(diskEtag)
              etagRef.current = diskEtag
            }
            savingRef.current = false
            return await saveNote(draftRef.current, diskEtag, false, retry + 1)
          }

          setSaveStatus('conflict')
          setConflictDisk(data.note ?? null)
          setError(data.error ?? 'Note changed on disk.')
          return
        }
        if (!res.ok || !data.success) {
          setSaveStatus('error')
          setError(data.error ?? 'Save failed')
          return
        }
        const newEtag = data.etag as string
        setEtag(newEtag)
        etagRef.current = newEtag
        baselineRef.current = markdown
        if (data.note) {
          setNote({ ...data.note, markdown })
        }
        setSaveStatus('saved')
        setConflictDisk(null)
        setError(null)

        // If the user typed something meaningfully different while we saved,
        // let the normal debounce pick it up — do not chain rapid saves here.
        const latest = joinFrontMatter(frontMatterRef.current, draftRef.current)
        if (sameMarkdown(latest, markdown) && latest !== markdown) {
          baselineRef.current = latest
        }
      } finally {
        savingRef.current = false
      }
    },
    [selectedId],
  )

  useEffect(() => {
    draftRef.current = draft
    frontMatterRef.current = frontMatter
    // Do not sync etag → etagRef here. A draft keystroke can re-run this with stale
    // etag state and wipe the ETag from a save that just finished (false conflicts).
  }, [draft, frontMatter])

  useEffect(() => {
    if (!note) return
    if (saveStatusRef.current === 'conflict') return

    const full = joinFrontMatter(frontMatter, draft)

    // Identical (or cosmetically identical) to what's on disk — nothing to save.
    // Adopt the editor's exact bytes as baseline so future compares are cheap.
    if (sameMarkdown(full, baselineRef.current)) {
      baselineRef.current = full
      if (saveStatusRef.current === 'editing') setSaveStatus('saved')
      return
    }

    // Real edit: single debounced save. While typing, the timer keeps resetting,
    // so the chip stays on "editing" and one save fires after the pause.
    if (saveStatusRef.current !== 'saving') setSaveStatus('editing')
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = window.setTimeout(() => {
      if (saveStatusRef.current === 'conflict') return
      const pending = joinFrontMatter(frontMatterRef.current, draftRef.current)
      if (sameMarkdown(pending, baselineRef.current)) {
        baselineRef.current = pending
        setSaveStatus('saved')
        return
      }
      void saveNote(draftRef.current, etagRef.current)
    }, 1000)
    return () => {
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
    }
  }, [draft, note, frontMatter, saveNote])

  useEffect(() => {
    setHistoryOpen(false)
    setHistory([])
  }, [selectedId])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'p')) {
        e.preventDefault()
        searchRef.current?.focus()
        setSearchOpen(true)
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault()
        if (saveTimer.current) window.clearTimeout(saveTimer.current)
        void saveNote(draftRef.current, etagRef.current)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [draft, etag, saveNote])

  async function toggleHistory() {
    if (!selectedId) return
    if (historyOpen) {
      setHistoryOpen(false)
      return
    }
    const rows = await fetch(`/api/notes/${selectedId}/history`).then((r) => r.json())
    setHistory(rows)
    setHistoryOpen(true)
  }

  async function restoreSnapshot(snapshotId: string) {
    if (!selectedId) return
    const res = await fetch(`/api/notes/${selectedId}/history/${snapshotId}/restore`, { method: 'POST' })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Restore failed')
      return
    }
    if (data.note) {
      setNote(data.note)
      const split = splitFrontMatter(data.note.markdown)
      setFrontMatter(split.frontMatter)
      setDraft(split.body)
      setEtag(data.etag)
      etagRef.current = data.etag
      draftRef.current = split.body
      frontMatterRef.current = split.frontMatter
      baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
      setEditorEpoch((e) => e + 1)
      setSaveStatus('saved')
    }
    const rows = await fetch(`/api/notes/${selectedId}/history`).then((r) => r.json())
    setHistory(rows)
    setHistoryOpen(true)
  }

  async function loadLogs() {
    const data = await fetch('/api/admin/logs?lines=300').then((r) => r.json())
    setLogText(data.text ?? '')
    setLogPath(data.latestLogPath ?? data.logsDirectory ?? null)
  }

  async function loadAutostart() {
    setAutostartInfo(await fetch('/api/admin/autostart').then((r) => r.json()))
  }

  async function setAutostart(enabled: boolean) {
    const res = await fetch('/api/admin/autostart', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not update Start with Windows')
      return
    }
    setAutostartInfo(data.status ?? data)
    setNetworkHint(enabled ? 'Start with Windows enabled for this user.' : 'Start with Windows disabled.')
  }

  async function openBrowse(path?: string) {
    const url = path ? `/api/settings/browse?path=${encodeURIComponent(path)}` : '/api/settings/browse'
    const data = await fetch(url).then((r) => r.json())
    if (data.error) {
      setError(data.error)
      return
    }
    setBrowsePath(data.path ?? '')
    setBrowseParent(data.parent ?? null)
    setBrowseEntries(data.entries ?? [])
  }

  async function loadNetworkSettings() {
    try {
      const n = await fetch('/api/settings/network').then((r) => r.json())
      if (n.bindMode === 'lan' || n.bindMode === 'loopback') setBindMode(n.bindMode)
      if (typeof n.port === 'number') setListenPort(n.port)
      setHttpsPfxPath(n.httpsPfxPath ?? '')
      setHttpsPasswordConfigured(!!n.httpsPasswordConfigured)
      setHttpsPfxPassword('')
    } catch {
      /* ignore */
    }
  }

  async function loadMirrorSettings() {
    try {
      const m = await fetch('/api/settings/mirror').then((r) => r.json())
      setMirrorEnabled(!!m.enabled)
      setMirrorDest(m.destinationPath ?? '')
      if (typeof m.intervalMinutes === 'number') setMirrorInterval(m.intervalMinutes)
      const st = m.status
      if (st?.lastSucceededUtc) {
        setMirrorStatus(`Last OK: ${new Date(st.lastSucceededUtc).toLocaleString()}`)
      } else if (st?.lastError) {
        setMirrorStatus(`Last error: ${st.lastError}`)
      } else {
        setMirrorStatus(st?.hint ?? null)
      }
    } catch {
      /* ignore */
    }
  }

  async function saveNetworkSettings() {
    setNetworkHint(null)
    const body: Record<string, unknown> = {
      bindMode,
      port: listenPort,
      httpsPfxPath: httpsPfxPath.trim() || null,
    }
    // Only send password when the user typed one (omit keeps existing)
    if (httpsPfxPassword.length > 0) body.httpsPfxPassword = httpsPfxPassword
    const res = await fetch('/api/settings/network', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setError(data.error ?? 'Could not save network settings')
      return
    }
    setHttpsPfxPassword('')
    setHttpsPasswordConfigured(!!data.httpsEnabled)
    setRestartNeeded(true)
    setNetworkHint(`Saved ${data.listenUrl}. Restart the server for bind/port/HTTPS changes to take effect.`)
  }

  async function restartServer() {
    setRestartBusy(true)
    setError(null)
    setNetworkHint('Restarting server… this page will reconnect shortly.')

    let nextOrigin = window.location.origin
    try {
      const n = await fetch('/api/settings/network').then((r) => r.json())
      if (typeof n.listenUrl === 'string') {
        try {
          const u = new URL(n.listenUrl)
          if (u.hostname === '0.0.0.0' || u.hostname === '::') {
            u.hostname = window.location.hostname || '127.0.0.1'
          }
          nextOrigin = u.origin
        } catch {
          /* keep current */
        }
      }
    } catch {
      /* ignore */
    }

    try {
      const res = await fetch('/api/admin/restart', { method: 'POST' })
      const data = await res.json().catch(() => ({}))
      if (res.ok === false && data.error) {
        setError(data.error)
        setRestartBusy(false)
        return
      }
      if (data.message) setNetworkHint(data.message)
    } catch {
      // Connection often drops as the process exits — expected
    }

    let attempts = 0
    const poll = window.setInterval(() => {
      attempts++
      void fetch(`${nextOrigin}/api/health`, { cache: 'no-store' })
        .then((r) => {
          if (!r.ok) return
          window.clearInterval(poll)
          setRestartNeeded(false)
          window.location.href = `${nextOrigin}/`
        })
        .catch(() => {
          if (attempts > 45) {
            window.clearInterval(poll)
            setRestartBusy(false)
            setNetworkHint(`If the UI does not reload, open ${nextOrigin} manually.`)
          }
        })
    }, 500)
  }

  async function saveMirrorSettings() {
    setMirrorStatus(null)
    const res = await fetch('/api/settings/mirror', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: mirrorEnabled,
        destinationPath: mirrorDest,
        intervalMinutes: mirrorInterval,
      }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setError(data.error ?? 'Could not save mirror settings')
      return
    }
    setMirrorStatus(
      data.enabled
        ? `Mirror enabled → ${data.destinationPath} (every ${data.intervalMinutes} min)`
        : 'Mirror disabled',
    )
  }

  async function runMirrorNow() {
    setMirrorStatus('Mirroring…')
    const res = await fetch('/api/settings/mirror/run', { method: 'POST' })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setMirrorStatus(data.error ?? 'Mirror failed')
      setError(data.error ?? 'Mirror failed')
      return
    }
    const st = data.status
    setMirrorStatus(
      st?.lastSucceededUtc
        ? `Mirror OK at ${new Date(st.lastSucceededUtc).toLocaleString()}`
        : 'Mirror finished',
    )
  }

  async function applyVaultPath(path: string) {
    const res = await fetch('/api/settings/vault', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ vaultPath: path }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not set vault path')
      return
    }
    setVaultPathInput(data.vaultPath)
    setSettingsOpen(false)
    setSelectedId(null)
    await loadVault()
  }

  function toggleFolderCollapsed(path: string) {
    setCollapsedFolders((prev) => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      saveCollapsedFolders(next)
      return next
    })
  }

  async function createFolder() {
    const name = window.prompt(
      folder
        ? `New folder inside "${folder}"`
        : 'New folder at vault root (top level)',
    )
    if (!name?.trim()) return
    const res = await fetch('/api/folders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: name.trim(), parent: folder }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not create folder')
      return
    }
    await loadVault()
    if (data.path) setFolder(data.path)
  }

  async function renameFolder() {
    if (!folder) {
      setError('Select a folder first')
      return
    }
    const name = window.prompt('Rename folder to', folder.split('/').pop() ?? '')
    if (!name?.trim()) return
    const res = await fetch('/api/folders', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: folder, newName: name.trim() }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not rename folder')
      return
    }
    setFolder(data.path ?? '')
    setSelectedId(null)
    await loadVault()
  }

  async function moveFolder() {
    if (!folder) {
      setError('Select a folder first')
      return
    }
    const dest = window.prompt(
      `Move "${folder}" into which folder?\n\nLeave blank to put it at the vault root (top level).\nExample: Joshua's Notebook`,
      '',
    )
    if (dest === null) return
    const res = await fetch('/api/folders', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: folder, newParent: dest.trim() }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not move folder')
      return
    }
    setFolder(data.path ?? '')
    setSelectedId(null)
    await loadVault()
  }

  async function deleteFolder() {
    if (!folder) return
    if (!window.confirm(`Move folder "${folder}" to trash?`)) return
    const res = await fetch(`/api/folders?path=${encodeURIComponent(folder)}`, { method: 'DELETE' })
    const data = await res.json().catch(() => ({}))
    if (!res.ok) {
      setError(data.error ?? 'Could not delete folder')
      return
    }
    setFolder('')
    setSelectedId(null)
    await loadVault()
  }

  async function createNote() {
    const title = window.prompt('New note title', 'Untitled')
    if (!title?.trim()) return
    const res = await fetch('/api/notes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: title.trim(), folder }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not create note')
      return
    }
    await loadVault()
    setSelectedId(data.id)
  }

  async function trashNote() {
    if (!selectedId) return
    if (!window.confirm('Move this note to trash?')) return
    const res = await fetch(`/api/notes/${selectedId}`, { method: 'DELETE' })
    if (!res.ok) {
      setError('Could not trash note')
      return
    }
    setSelectedId(null)
    setNote(null)
    await loadVault()
    const q = folder ? `?folder=${encodeURIComponent(folder)}` : ''
    const list = (await fetch(`/api/notes${q}`).then((r) => r.json())) as NoteSummary[]
    setNotes(list)
  }

  async function renameNote() {
    if (!selectedId || !note) return
    const title = window.prompt('Rename note to', note.title)
    if (title === null || !title.trim() || title.trim() === note.title) return
    const res = await fetch(`/api/notes/${selectedId}/move`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: title.trim(), folder: note.folderPath }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setError(data.error ?? 'Rename failed')
      return
    }
    if (data.note) {
      setNote(data.note)
      const split = splitFrontMatter(data.note.markdown)
      setFrontMatter(split.frontMatter)
      setDraft(split.body)
      setEtag(data.note.etag)
      etagRef.current = data.note.etag
      draftRef.current = split.body
      frontMatterRef.current = split.frontMatter
      baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
    }
    await loadVault()
  }

  async function moveNote() {
    if (!selectedId || !note) return
    const dest = window.prompt(
      `Move note into which folder?\n\nLeave blank for vault root.\nCurrent: ${note.folderPath || '(root)'}`,
      note.folderPath,
    )
    if (dest === null) return
    const res = await fetch(`/api/notes/${selectedId}/move`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: note.title, folder: dest.trim() }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setError(data.error ?? 'Move failed')
      return
    }
    if (data.note) {
      setNote(data.note)
      const split = splitFrontMatter(data.note.markdown)
      setFrontMatter(split.frontMatter)
      setDraft(split.body)
      setEtag(data.note.etag)
      etagRef.current = data.note.etag
      setFolder(data.note.folderPath)
      draftRef.current = split.body
      frontMatterRef.current = split.frontMatter
      baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
    }
    await loadVault()
  }

  async function duplicateNote() {
    if (!selectedId) return
    const res = await fetch(`/api/notes/${selectedId}/duplicate`, { method: 'POST' })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Duplicate failed')
      return
    }
    await loadVault()
    setSelectedId(data.id)
  }

  async function exportNoteHtml() {
    if (!selectedId || !note) return
    setError(null)
    try {
      const res = await fetch(`/api/notes/${selectedId}/export-html`)
      if (!res.ok) {
        const data = await res.json().catch(() => ({}))
        setError((data as { error?: string }).error ?? 'Could not export note')
        return
      }
      const blob = await res.blob()
      const cd = res.headers.get('Content-Disposition') ?? ''
      const match = /filename\*?=(?:UTF-8''|")?([^\";]+)/i.exec(cd)
      const rawName = match?.[1] ? decodeURIComponent(match[1].replace(/"/g, '')) : `${note.title || 'note'}.html`
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = rawName
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch {
      setError('Could not export note')
    }
  }

  function reloadFromDisk() {
    if (!conflictDisk) return
    setNote(conflictDisk)
    const split = splitFrontMatter(conflictDisk.markdown)
    setFrontMatter(split.frontMatter)
    setDraft(split.body)
    setEtag(conflictDisk.etag)
    etagRef.current = conflictDisk.etag
    draftRef.current = split.body
    frontMatterRef.current = split.frontMatter
    baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
    setEditorEpoch((e) => e + 1)
    setSaveStatus('saved')
    setConflictDisk(null)
    setError(null)
  }

  function overwriteDisk() {
    void saveNote(draftRef.current, etagRef.current, true)
  }

  async function localizeRemoteImages() {
    if (!selectedId || !note) return
    setLocalizeStatus('Downloading remote images…')
    const res = await fetch(`/api/notes/${selectedId}/localize-images`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ etag: etagRef.current }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setLocalizeStatus(null)
      setError(data.error ?? 'Could not localize images')
      return
    }
    if (data.note) {
      setNote(data.note)
      const split = splitFrontMatter(data.note.markdown)
      setFrontMatter(split.frontMatter)
      setDraft(split.body)
      draftRef.current = split.body
      frontMatterRef.current = split.frontMatter
      baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
      if (data.etag) {
        setEtag(data.etag)
        etagRef.current = data.etag
      }
    }
    setLocalizeStatus(`Localized ${data.localized} image(s)`)
    window.setTimeout(() => setLocalizeStatus(null), 2500)
  }

  if (auth && auth.setupRequired) {
    return (
      <FirstRunWizard
        onComplete={() => {
          void loadAuth().then(() => loadVault())
        }}
      />
    )
  }

  if (auth && auth.authRequired && !auth.authenticated) {
    return (
      <LoginScreen
        onLoggedIn={() => {
          void loadAuth().then(() => loadVault())
        }}
      />
    )
  }

  if (vault && !vault.configured) {
    return (
      <>
        <main className="setup-screen">
          <p className="brand">Jotdex</p>
          <h1>Point Jotdex at your notes folder</h1>
          <p className="lede">
            Choose the folder that holds your Markdown vault (notebooks as folders, notes as .md files). Prefer local disk — not iCloud — for the live vault.
          </p>
          <button
            type="button"
            className="primary"
            onClick={() => {
              setSettingsOpen(true)
              void openBrowse()
            }}
          >
            Choose vault folder
          </button>
          {error && <p className="err">{error}</p>}
        </main>
        {settingsOpen && (
          <div className="modal-backdrop" onClick={() => setSettingsOpen(false)} role="presentation">
            <div className="modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Vault settings">
              <h2>Vault location</h2>
              <p className="lede">Pick the folder that contains your .md notes.</p>
              <label className="field">
                Path
                <input value={vaultPathInput} onChange={(e) => setVaultPathInput(e.target.value)} placeholder="C:\JotdexVault" />
              </label>
              <div className="modal-actions">
                <button type="button" className="primary" onClick={() => void applyVaultPath(vaultPathInput)}>
                  Use this folder
                </button>
                <button type="button" className="ghost" onClick={() => setSettingsOpen(false)}>
                  Cancel
                </button>
              </div>
              <div className="browser">
                <div className="browser-bar">
                  <button type="button" className="ghost" disabled={!browseParent} onClick={() => browseParent && void openBrowse(browseParent)}>
                    Up
                  </button>
                  <code>{browsePath || 'Drives'}</code>
                  {browsePath && (
                    <button type="button" className="ghost" onClick={() => void applyVaultPath(browsePath)}>
                      Select current
                    </button>
                  )}
                </div>
                <ul>
                  {browseEntries.map((e) => (
                    <li key={e.path}>
                      <button type="button" onClick={() => void openBrowse(e.path)}>
                        {e.type === 'drive' ? e.name : `📁 ${e.name}`}
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          </div>
        )}
      </>
    )
  }
  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-brand">
          <span className="brand">Jotdex</span>
          {vault?.configured && (
            <span className="vault-pill">{vault.name} · {vault.noteCount}</span>
          )}
        </div>
        <div className="search-wrap">
          <input
            ref={searchRef}
            className="search-input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onFocus={() => query.trim() && setSearchOpen(true)}
            placeholder="Search titles and note text…  (Ctrl+K)"
            aria-label="Search notes"
          />
          {searchOpen && query.trim() && (
            <div className="search-dropdown">
              <div className="search-meta">{searchMeta || 'Searching…'}</div>
              <ul>
                {hits.map((h) => (
                  <li key={h.noteId}>
                    <button
                      type="button"
                      onClick={() => {
                        setSelectedId(h.noteId)
                        setQuery('')
                        setSearchOpen(false)
                      }}
                    >
                      <span className="note-title">{h.title}</span>
                      <span className="note-path">{h.folderPath || '/'}</span>
                      {h.snippet && (
                        <span
                          className="snippet"
                          dangerouslySetInnerHTML={{
                            __html: DOMPurify.sanitize(
                              h.snippet.replaceAll('«', '<mark>').replaceAll('»', '</mark>'),
                            ),
                          }}
                        />
                      )}
                    </button>
                  </li>
                ))}
                {hits.length === 0 && <li className="empty">No matches</li>}
              </ul>
            </div>
          )}
        </div>
        <div className="topbar-actions">
          <button
            type="button"
            className="ghost"
            onClick={() => {
              setSettingsOpen(true)
              setNetworkHint(null)
              void openBrowse(vaultPathInput || undefined)
              void loadNetworkSettings()
              void loadMirrorSettings()
            }}
          >
            Settings
          </button>
          <button type="button" className="ghost" onClick={() => void fetch('/api/admin/rescan', { method: 'POST' }).then(loadVault)}>
            Rescan
          </button>
          {auth && !auth.developmentBypass && auth.authenticated && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                void fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).then(() => {
                  setAuth((a) => (a ? { ...a, authenticated: false, username: undefined } : a))
                  void loadAuth()
                })
              }}
            >
              Sign out
            </button>
          )}
        </div>
      </header>

      {settingsOpen && (
        <div className="modal-backdrop" onClick={() => setSettingsOpen(false)} role="presentation">
          <div className="modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Settings">
            <h2>Vault location</h2>
            <p className="lede">Pick the folder that contains your .md notes. Use local disk for the live vault.</p>
            <label className="field">
              Path
              <input value={vaultPathInput} onChange={(e) => setVaultPathInput(e.target.value)} placeholder="C:\JotdexVault" />
            </label>
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void applyVaultPath(vaultPathInput)}>
                Use this folder
              </button>
              <button type="button" className="ghost" onClick={() => setSettingsOpen(false)}>
                Cancel
              </button>
            </div>
            <div className="browser">
              <div className="browser-bar">
                <button type="button" className="ghost" disabled={!browseParent} onClick={() => browseParent && void openBrowse(browseParent)}>
                  Up
                </button>
                <code>{browsePath || 'Drives'}</code>
                {browsePath && (
                  <button type="button" className="ghost" onClick={() => { setVaultPathInput(browsePath); void applyVaultPath(browsePath) }}>
                    Select current
                  </button>
                )}
              </div>
              <ul>
                {browseEntries.map((e) => (
                  <li key={e.path}>
                    <button type="button" onClick={() => void openBrowse(e.path)}>
                      {e.type === 'drive' ? e.name : `📁 ${e.name}`}
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <h2 className="settings-section">Network</h2>
            <p className="lede">Default is this PC only. LAN access is opt-in. Optional PFX enables HTTPS (restart required).</p>
            <label className="field">
              Binding
              <select value={bindMode} onChange={(e) => setBindMode(e.target.value as 'loopback' | 'lan')}>
                <option value="loopback">This PC only (127.0.0.1)</option>
                <option value="lan">LAN (all interfaces)</option>
              </select>
            </label>
            <label className="field">
              Port
              <input
                type="number"
                min={1}
                max={65535}
                value={listenPort}
                onChange={(e) => setListenPort(Number(e.target.value) || 5180)}
              />
            </label>
            <label className="field">
              HTTPS certificate (PFX path)
              <input
                value={httpsPfxPath}
                onChange={(e) => setHttpsPfxPath(e.target.value)}
                placeholder="C:\certs\jotdex.pfx"
              />
            </label>
            <label className="field">
              PFX password {httpsPasswordConfigured ? '(saved — leave blank to keep)' : '(optional)'}
              <input
                type="password"
                value={httpsPfxPassword}
                onChange={(e) => setHttpsPfxPassword(e.target.value)}
                autoComplete="new-password"
                placeholder={httpsPasswordConfigured ? '••••••••' : ''}
              />
            </label>
            <p className="muted">Prefer env var JOTDEX_HTTPS_PFX_PASSWORD over storing the password in config.</p>
            {bindMode === 'lan' && (
              <p className="warn">LAN without HTTPS exposes the app in cleartext on your network. Prefer a PFX or VPN.</p>
            )}
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void saveNetworkSettings()}>
                Save network
              </button>
              <button
                type="button"
                className={restartNeeded ? 'primary' : 'ghost'}
                disabled={restartBusy}
                onClick={() => void restartServer()}
                title="Apply bind/port/HTTPS by restarting Jotdex"
              >
                {restartBusy ? 'Restarting…' : 'Restart server'}
              </button>
            </div>
            {networkHint && <p className="muted">{networkHint}</p>}
            {restartNeeded && !restartBusy && (
              <p className="warn">Network settings saved — click Restart server to apply them.</p>
            )}

            <h2 className="settings-section">Cloud backup mirror</h2>
            <p className="lede">
              Keep the live vault on local disk. Optionally copy one-way to iCloud, OneDrive, etc. Never open the mirror as the live vault.
            </p>
            <label className="field checkbox-row">
              <input type="checkbox" checked={mirrorEnabled} onChange={(e) => setMirrorEnabled(e.target.checked)} />
              Enable automatic mirror
            </label>
            <label className="field">
              Destination folder
              <input
                value={mirrorDest}
                onChange={(e) => setMirrorDest(e.target.value)}
                placeholder="C:\Users\You\iCloudDrive\JotdexVault"
              />
            </label>
            <label className="field">
              Interval (minutes)
              <input
                type="number"
                min={5}
                max={1440}
                value={mirrorInterval}
                onChange={(e) => setMirrorInterval(Number(e.target.value) || 15)}
              />
            </label>
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void saveMirrorSettings()}>
                Save mirror
              </button>
              <button type="button" className="ghost" onClick={() => void runMirrorNow()}>
                Mirror now
              </button>
            </div>
            {mirrorStatus && <p className="muted">{mirrorStatus}</p>}

            <h2 className="settings-section">Start with Windows</h2>
            <p className="lede">
              After a reboot, Jotdex should come back by itself. Easiest: enable a Startup shortcut for your Windows user.
              For a always-on PC, prefer the Windows Service (run <code>install-service.ps1</code> as Administrator once).
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="ghost"
                onClick={() => void loadAutostart()}
              >
                Check status
              </button>
              <button type="button" className="primary" onClick={() => void setAutostart(true)}>
                Enable Start with Windows
              </button>
              <button type="button" className="ghost" onClick={() => void setAutostart(false)}>
                Disable
              </button>
            </div>
            {autostartInfo && (
              <div className="autostart-status muted">
                <p>{autostartInfo.hint}</p>
                <p>
                  User startup: {autostartInfo.userStartupEnabled ? 'on' : 'off'}
                  {autostartInfo.userStartupPath ? ` — ${autostartInfo.userStartupPath}` : ''}
                </p>
                <p>
                  Windows Service:{' '}
                  {autostartInfo.windowsService?.installed
                    ? `${autostartInfo.windowsService.status} (${autostartInfo.windowsService.startType})`
                    : 'not installed'}
                </p>
              </div>
            )}

            <h2 className="settings-section">Logs</h2>
            <p className="lede">
              Logs are written to a daily file under app data (readable in Notepad). Use this viewer for the latest lines.
            </p>
            <div className="modal-actions">
              <button type="button" className="ghost" onClick={() => void loadLogs()}>
                View recent log
              </button>
            </div>
            {logPath && <p className="muted">File: {logPath}</p>}
            {logText && <pre className="maint-report log-view">{logText}</pre>}

            <h2 className="settings-section">Maintenance</h2>
            <p className="lede">
              Diagnostics, integrity scan, trash cleanup, backup ZIP, and full-vault static HTML export. To share one note,
              open it and use Share HTML.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void (async () => {
                    const data = await fetch('/api/admin/diagnostics').then((r) => r.json())
                    setDiagText(JSON.stringify(data, null, 2))
                  })()
                }}
              >
                Diagnostics
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void (async () => {
                    const data = await fetch('/api/admin/integrity').then((r) => r.json())
                    if (!data.success) {
                      setError(data.error ?? 'Integrity scan failed')
                      return
                    }
                    const lines = (data.issues as { severity: string; code: string; message: string; notePath?: string }[])
                      .map((i) => `[${i.severity}] ${i.code}: ${i.message}${i.notePath ? ` (${i.notePath})` : ''}`)
                    setIntegrityReport(
                      lines.length === 0
                        ? `OK — ${data.noteCount} notes, no issues.`
                        : `${data.issueCount} issue(s) across ${data.noteCount} notes:\n` + lines.slice(0, 40).join('\n'),
                    )
                  })()
                }}
              >
                Integrity scan
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void (async () => {
                    setNetworkHint('Creating backup ZIP…')
                    const data = await fetch('/api/admin/backup', { method: 'POST' }).then((r) => r.json())
                    if (!data.success) {
                      setError(data.error ?? 'Backup failed')
                      setNetworkHint(null)
                      return
                    }
                    const mb = (data.bytes / (1024 * 1024)).toFixed(1)
                    setNetworkHint(`Backup OK (${mb} MB): ${data.bundlePath}`)
                  })()
                }}
              >
                Create backup ZIP
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  if (!window.confirm('Empty the trash folder in app data?')) return
                  void (async () => {
                    const data = await fetch('/api/admin/trash/empty', { method: 'POST' }).then((r) => r.json())
                    if (!data.success) {
                      setError(data.error ?? 'Trash cleanup failed')
                      return
                    }
                    setNetworkHint(`Trash cleaned: ${data.deletedFiles} files removed.`)
                  })()
                }}
              >
                Empty trash
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void (async () => {
                    setError(null)
                    setNetworkHint('Exporting vault to static HTML…')
                    const res = await fetch('/api/admin/export-static', { method: 'POST' })
                    const data = await res.json()
                    if (!res.ok || !data.success) {
                      setError(data.error ?? 'Static export failed')
                      setNetworkHint(null)
                      return
                    }
                    setNetworkHint(`Exported ${data.noteCount} notes to ${data.exportPath}`)
                    window.alert(`Static vault export ready:\n${data.exportPath}\n\nOpen index.html from that folder.`)
                  })()
                }}
              >
                Export vault as HTML
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void fetch('/api/admin/reindex', { method: 'POST' }).then(() => setNetworkHint('Search reindex started/finished.'))
                }}
              >
                Reindex search
              </button>
            </div>
            {integrityReport && (
              <pre className="maint-report">{integrityReport}</pre>
            )}
            {diagText && (
              <pre className="maint-report">{diagText}</pre>
            )}
          </div>
        </div>
      )}

      <div className="body">
        <aside className="pane left">
          <div className="pane-tools">
            <button type="button" className="ghost" onClick={() => void createFolder()}>
              New folder
            </button>
            <button type="button" className="ghost" disabled={!folder} onClick={() => void renameFolder()}>
              Rename
            </button>
            <button type="button" className="ghost" disabled={!folder} onClick={() => void moveFolder()}>
              Move
            </button>
            <button type="button" className="ghost" disabled={!folder} onClick={() => void deleteFolder()}>
              Trash
            </button>
          </div>
          {tree && (
            <FolderTree
              node={tree}
              depth={0}
              selected={folder}
              collapsed={collapsedFolders}
              onToggle={toggleFolderCollapsed}
              onSelect={(p) => {
                setFolder(p)
                setSelectedId(null)
              }}
            />
          )}
        </aside>

        <section className="pane middle">
          <div className="pane-head">
            <h2>{folder || 'All notes'}</h2>
            <div className="pane-tools">
              <button type="button" className="ghost" onClick={() => void createNote()}>
                New note
              </button>
            </div>
          </div>
          <ul className="note-list">
            {notes.map((n) => (
              <li key={n.id}>
                <button type="button" className={selectedId === n.id ? 'active' : ''} onClick={() => setSelectedId(n.id)}>
                  <span className="note-title">{n.title}</span>
                  <span className="note-path">{n.folderPath || '/'}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>

        <section className="pane right">
          {error && saveStatus !== 'conflict' && <p className="err">{error}</p>}
          {saveStatus === 'conflict' && (
            <div className="conflict-banner">
              <p>{error ?? 'This note changed on disk.'}</p>
              <div className="modal-actions">
                <button type="button" className="primary" onClick={reloadFromDisk}>
                  Reload from disk
                </button>
                <button type="button" className="ghost" onClick={overwriteDisk}>
                  Overwrite disk
                </button>
                <button type="button" className="ghost" onClick={() => { setSaveStatus('editing'); setError(null) }}>
                  Keep editing
                </button>
              </div>
            </div>
          )}
          {!note && <p className="muted">Select a note or search above</p>}
          {note && (
            <>
              <div className="note-head">
                <div>
                  <h1>{note.title}</h1>
                  <p className="note-path">{note.relativePath}</p>
                </div>
                <div className="actions">
                  <span className={`save-chip ${saveStatus}`}>{saveStatus}</span>
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => {
                      setShowSource((s) => {
                        if (s) setSourceForced(null)
                        return !s
                      })
                    }}
                  >
                    {showSource ? 'Visual' : 'Source'}
                  </button>
                  <button type="button" className="ghost" onClick={() => void renameNote()}>
                    Rename
                  </button>
                  <button type="button" className="ghost" onClick={() => void moveNote()}>
                    Move
                  </button>
                  <button type="button" className="ghost" onClick={() => void duplicateNote()}>
                    Duplicate
                  </button>
                  <button type="button" className="ghost" onClick={() => void trashNote()}>
                    Trash
                  </button>
                  <button
                    type="button"
                    className={`ghost${historyOpen ? ' on' : ''}`}
                    onClick={() => void toggleHistory()}
                  >
                    History
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => void exportNoteHtml()}
                    title="Download a self-contained HTML file you can send to someone"
                  >
                    Share HTML
                  </button>
                  {countRemoteImages(joinFrontMatter(frontMatter, draft)) > 0 && (
                    <button type="button" className="ghost" onClick={() => void localizeRemoteImages()}>
                      Make images local
                    </button>
                  )}
                </div>
              </div>
              {localizeStatus && <p className="upload-status">{localizeStatus}</p>}
              {sourceForced && showSource && (
                <div className="source-banner">
                  <p>{sourceForced}</p>
                  <button type="button" className="ghost" onClick={() => { setSourceForced(null); setShowSource(false) }}>
                    Try visual anyway
                  </button>
                </div>
              )}
              {note.tags?.length > 0 && (
                <div className="tags">
                  {note.tags.map((t) => (
                    <span key={t}>{t}</span>
                  ))}
                </div>
              )}
              {showSource ? (
                <textarea className="source-editor" value={draft} onChange={(e) => setDraft(e.target.value)} spellCheck={false} />
              ) : (
                <NoteEditor
                  key={note.id}
                  noteId={note.id}
                  noteStem={note.relativePath.replace(/^.*\//, '').replace(/\.md$/i, '')}
                  markdown={draft}
                  contentEpoch={editorEpoch}
                  attachments={note.attachments}
                  onChange={setDraft}
                  onError={(msg) => setError(msg)}
                  getEtag={() => etagRef.current}
                  onNoteMeta={(n) => {
                    if (n.markdown) {
                      const split = splitFrontMatter(n.markdown)
                      setFrontMatter(split.frontMatter)
                      setDraft(split.body)
                      draftRef.current = split.body
                      frontMatterRef.current = split.frontMatter
                      baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
                      setEditorEpoch((e) => e + 1)
                    }
                    if (n.etag) {
                      setEtag(n.etag)
                      etagRef.current = n.etag
                    }
                    if (n.attachments || n.markdown) {
                      setNote((prev) =>
                        prev
                          ? {
                              ...prev,
                              attachments: n.attachments ?? prev.attachments,
                              etag: n.etag ?? prev.etag,
                              markdown: n.markdown ?? prev.markdown,
                            }
                          : prev,
                      )
                    }
                  }}
                />
              )}
              {historyOpen && (
                <div className="history-panel">
                  <h3>History</h3>
                  {history.length === 0 ? (
                    <p className="muted">No earlier versions yet. History is saved when the note changes.</p>
                  ) : (
                    <ul>
                      {history.map((h) => (
                        <li key={h.snapshotId}>
                          <div className="history-meta">
                            <strong>{new Date(h.createdUtc).toLocaleString()}</strong>
                            {h.summary && <span className="history-summary">{h.summary}</span>}
                            {h.preview && <span className="history-preview">{h.preview}</span>}
                          </div>
                          <button type="button" className="ghost" onClick={() => void restoreSnapshot(h.snapshotId)}>
                            Restore
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
            </>
          )}
        </section>
      </div>
    </div>
  )
}

export default App
