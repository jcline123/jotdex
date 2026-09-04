import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import DOMPurify from 'dompurify'
import './App.css'
import { NoteEditor, type NoteCatalogItem } from './NoteEditor'
import { joinFrontMatter, sameMarkdown, setFavoriteInMarkdown, splitFrontMatter } from './frontMatter'
import { lintNoteMarkdown, type MarkdownLintIssue } from './markdownLint'
import { looksUnsafeForVisual } from './unsafeMarkdown'
import { ClipSaveModal } from './ClipSaveModal'
import { NewNoteModal, folderRailShortLabel } from './NewNoteModal'
import { FolderPickerModal } from './FolderPickerModal'
import {
  buildClipBookmarklet,
  loadClipDefaultFolder,
  parseClipHash,
  type ClipPayload,
} from './jotdexBookmarklet'
import { FirstRunWizard, LoginScreen } from './AuthScreens'
import { TrashPane } from './TrashPane'
import { SnippetsPane } from './SnippetsPane'
import { extractOutline } from './outline'
import { type LiveOutlineItem } from './editor/outline/liveOutline'
import {
  NOTE_TEMPLATES,
  isNetworkDoc,
  networkSiteMarkdown,
  nextNetworkSiteNumber,
  NETWORK_SITES_END,
  type NoteTemplate,
} from './templates'
import { copyJotdexAiPrompt } from './jotdexAiPrompt'
import { IdleLockGate, loadIdleLockEnabled, loadIdleLockMinutes } from './IdleLockGate'
import { hydrateUiPrefs, rememberViewedNoteAndSync, saveUiPrefs, type UiPrefs } from './uiPrefs'
import { TodosRail } from './TodosRail'
import { getNotificationPermission, promptTodoNotifications, type NotifyPermission } from './todoReminders'
import { HomeLanding } from './HomeLanding'
import { CloudBackupSettings } from './CloudBackupSettings'
import { runCloudBackup } from './cloudBackupApi'
import { isStandaloneTodosNote } from './systemNotes'
import { diffLines } from './diffLines'

function isCodeSnippetNote(frontMatter: string, folderPath?: string): boolean {
  if (/jotdex_type:\s*code-snippet/i.test(frontMatter)) return true
  return (folderPath ?? '').replace(/\\/g, '/').trim().toLowerCase() === 'snippets'
}


function countRemoteImages(markdown: string): number {
  const re = /!\[[^\]]*\]\((https?:\/\/[^)\s]+)\)/gi
  let n = 0
  while (re.exec(markdown) !== null) n++
  return n
}

type SaveStatus = 'saved' | 'editing' | 'saving' | 'conflict' | 'error' | 'uploading'

function saveChipLabel(status: SaveStatus): string {
  switch (status) {
    case 'saved':
      return 'Saved'
    case 'editing':
      return 'Editing'
    case 'saving':
      return 'Saving'
    case 'uploading':
      return 'Finishing paste'
    case 'conflict':
      return 'Conflict'
    case 'error':
      return 'Error'
    default:
      return status
  }
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
  created?: string
  hasAttachments: boolean
  favorite?: boolean
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
  headingFolds?: string[]
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
  totpEnabled?: boolean
  username?: string
  displayName?: string
  developmentBypass?: boolean
  ui?: UiPrefs
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

type RailDrag =
  | { kind: 'note'; id: string; title: string; folderPath: string }
  | { kind: 'folder'; path: string }

function FolderTree({
  node,
  depth,
  selected,
  onSelect,
  collapsed,
  onToggle,
  dropTargetPath,
  onFolderDragStart,
  onFolderDragOver,
  onFolderDrop,
}: {
  node: FolderNode
  depth: number
  selected: string
  onSelect: (path: string) => void
  collapsed: Set<string>
  onToggle: (path: string) => void
  dropTargetPath?: string | null
  onFolderDragStart?: (path: string, e: React.DragEvent) => void
  onFolderDragOver?: (path: string, e: React.DragEvent) => void
  onFolderDrop?: (path: string, e: React.DragEvent) => void
}) {
  const hasKids = node.children.length > 0
  // Root (empty path) stays expanded; collapse applies to real folders
  const isCollapsed = node.relativePath !== '' && collapsed.has(node.relativePath)
  const showKids = hasKids && !isCollapsed
  const dropHere = dropTargetPath === node.relativePath

  return (
    <div className="tree-branch">
      <div
        className={`tree-row${selected === node.relativePath ? ' active' : ''}${dropHere ? ' drop-target' : ''}`}
        style={{ paddingLeft: `${0.35 + depth * 0.85}rem` }}
        onDragOver={(e) => onFolderDragOver?.(node.relativePath, e)}
        onDrop={(e) => onFolderDrop?.(node.relativePath, e)}
      >
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
        <button
          type="button"
          className="tree-item"
          draggable={Boolean(onFolderDragStart) && node.relativePath !== ''}
          onDragStart={(e) => onFolderDragStart?.(node.relativePath, e)}
          onClick={() => onSelect(node.relativePath)}
        >
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
            dropTargetPath={dropTargetPath}
            onFolderDragStart={onFolderDragStart}
            onFolderDragOver={onFolderDragOver}
            onFolderDrop={onFolderDrop}
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
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('saved')
  const [history, setHistory] = useState<
    { snapshotId: string; createdUtc: string; summary?: string; preview?: string; sizeBytes?: number }[]
  >([])
  const [historyOpen, setHistoryOpen] = useState(false)
  const [historyDiff, setHistoryDiff] = useState<{
    snapshotId: string
    lines: { type: 'same' | 'add' | 'del'; text: string }[]
  } | null>(null)
  const [formatLintOpen, setFormatLintOpen] = useState(false)
  const [formatLintIssues, setFormatLintIssues] = useState<MarkdownLintIssue[]>([])
  const [formatLintBusy, setFormatLintBusy] = useState(false)
  const saveTimer = useRef<number | null>(null)
  const [conflictDisk, setConflictDisk] = useState<NoteDetail | null>(null)
  const [sourceForced, setSourceForced] = useState<string | null>(null)
  const [localizeStatus, setLocalizeStatus] = useState<string | null>(null)
  const etagRef = useRef('')
  const draftRef = useRef('')
  const frontMatterRef = useRef('')
  const baselineRef = useRef('')
  const savingRef = useRef(false)
  const saveStatusRef = useRef<SaveStatus>('saved')
  const editorRevisionRef = useRef(0)
  const savedRevisionRef = useRef(0)
  const pastePendingRef = useRef(false)
  const selectedIdRef = useRef<string | null>(null)
  const [editorEpoch, setEditorEpoch] = useState(0)
  saveStatusRef.current = saveStatus
  selectedIdRef.current = selectedId

  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<SearchHit[]>([])
  const [searchOpen, setSearchOpen] = useState(false)
  const [searchMeta, setSearchMeta] = useState('')
  const [searchIndex, setSearchIndex] = useState(0)
  const searchRef = useRef<HTMLInputElement>(null)
  const searchWrapRef = useRef<HTMLDivElement>(null)
  const searchIndexRef = useRef(0)
  searchIndexRef.current = searchIndex
  const hitsRef = useRef(hits)
  hitsRef.current = hits

  const [quickOpen, setQuickOpen] = useState(false)
  const [quickQuery, setQuickQuery] = useState('')
  const [quickIndex, setQuickIndex] = useState(0)
  const [noteCatalog, setNoteCatalog] = useState<NoteCatalogItem[]>([])
  const quickInputRef = useRef<HTMLInputElement>(null)

  const [outlineOpen, setOutlineOpen] = useState(false)
  const [backlinksOpen, setBacklinksOpen] = useState(false)
  const [backlinks, setBacklinks] = useState<
    { noteId: string; title: string; relativePath: string; folderPath: string; context?: string }[]
  >([])
  const [jumpHeading, setJumpHeading] = useState<{ text: string; nonce: number; pos?: number } | null>(null)
  const [liveOutline, setLiveOutline] = useState<LiveOutlineItem[]>([])
  const [templateMenu, setTemplateMenu] = useState(false)
  const [templateMenuPos, setTemplateMenuPos] = useState<{ top: number; left: number } | null>(null)
  const templateBtnRef = useRef<HTMLButtonElement>(null)
  const [mobilePane, setMobilePane] = useState<'folders' | 'notes' | 'editor' | 'todos' | 'trash' | 'snippets'>('editor')
  const [showTrash, setShowTrash] = useState(false)
  const [showSnippets, setShowSnippets] = useState(false)
  const [tasksRefreshKey, setTasksRefreshKey] = useState(0)
  const bumpTasksRefresh = useCallback(() => setTasksRefreshKey((k) => k + 1), [])
  const [todosCollapsed, setTodosCollapsed] = useState(() => {
    try {
      return localStorage.getItem('jotdex.todosCollapsed') === '1'
    } catch {
      return false
    }
  })
  const [foldersCollapsed, setFoldersCollapsed] = useState(() => {
    try {
      return localStorage.getItem('jotdex.foldersCollapsed') === '1'
    } catch {
      return false
    }
  })
  const [notesCollapsed, setNotesCollapsed] = useState(() => {
    try {
      return localStorage.getItem('jotdex.notesCollapsed') === '1'
    } catch {
      return false
    }
  })
  const [newNoteModalOpen, setNewNoteModalOpen] = useState(false)
  const [movePicker, setMovePicker] = useState<null | { kind: 'note' } | { kind: 'folder'; path: string }>(null)
  const [dropTargetPath, setDropTargetPath] = useState<string | null>(null)
  const railDragRef = useRef<RailDrag | null>(null)
  const folderExpandTimer = useRef<number | null>(null)
  const [titleEditing, setTitleEditing] = useState(false)
  const [titleDraft, setTitleDraft] = useState('')
  const [narrowLayout, setNarrowLayout] = useState(() => {
    try {
      return window.matchMedia('(max-width: 900px)').matches
    } catch {
      return false
    }
  })
  const [notifyPerm, setNotifyPerm] = useState<NotifyPermission>(() => getNotificationPermission())
  const [notifyHint, setNotifyHint] = useState<string | null>(null)
  const popoutNoteId = useMemo(() => {
    try {
      return new URLSearchParams(window.location.search).get('popout')
    } catch {
      return null
    }
  }, [])
  const deepLinkNoteId = useMemo(() => {
    try {
      return new URLSearchParams(window.location.search).get('note')
    } catch {
      return null
    }
  }, [])
  const [popoutChromeAutoHide, setPopoutChromeAutoHide] = useState(() => {
    try {
      return localStorage.getItem('jotdex.popoutChromeAutoHide') !== '0'
    } catch {
      return true
    }
  })
  const [aiPromptHint, setAiPromptHint] = useState<string | null>(null)
  const [idleLockEnabled, setIdleLockEnabled] = useState(loadIdleLockEnabled)
  const [idleLockMinutes, setIdleLockMinutes] = useState(loadIdleLockMinutes)
  const [secPassword, setSecPassword] = useState('')
  const [secConfirm, setSecConfirm] = useState('')
  const [secCurrentPassword, setSecCurrentPassword] = useState('')
  const [secNewPassword, setSecNewPassword] = useState('')
  const [secNewConfirm, setSecNewConfirm] = useState('')
  const [secHint, setSecHint] = useState<string | null>(null)
  const [secBusy, setSecBusy] = useState(false)

  const [settingsOpen, setSettingsOpen] = useState(false)
  const [settingsTab, setSettingsTab] = useState<
    'vault' | 'network' | 'security' | 'notifications' | 'backup' | 'updates' | 'advanced' | 'capture'
  >('vault')
  const settingsPanelRef = useRef<HTMLDivElement>(null)
  const [clipDefaultFolder, setClipDefaultFolder] = useState(() => loadClipDefaultFolder())
  const [clipFolderOptions, setClipFolderOptions] = useState<{ path: string; label: string }[]>([
    { path: 'Inbox', label: 'Inbox' },
  ])
  const [clipCopied, setClipCopied] = useState(false)
  const [pendingClip, setPendingClip] = useState<ClipPayload | null>(null)
  const [updateInfo, setUpdateInfo] = useState<{
    success?: boolean
    error?: string
    currentVersion?: string
    latestTag?: string
    updateAvailable?: boolean
    notes?: string
    htmlUrl?: string
    downloadName?: string
    installPath?: string
    updateScriptPath?: string
    backupHoldPath?: string
  } | null>(null)
  const [updateBusy, setUpdateBusy] = useState(false)
  const [vaultPathInput, setVaultPathInput] = useState('')
  const [browsePath, setBrowsePath] = useState('')
  const [browseParent, setBrowseParent] = useState<string | null>(null)
  const [browseEntries, setBrowseEntries] = useState<BrowseEntry[]>([])
  const [bindMode, setBindMode] = useState<'loopback' | 'lan'>('loopback')
  const [listenPort, setListenPort] = useState(5180)
  const [httpsSelfSigned, setHttpsSelfSigned] = useState(true)
  const [httpsPort, setHttpsPort] = useState(5181)
  const [httpsPfxPath, setHttpsPfxPath] = useState('')
  const [httpsPfxPassword, setHttpsPfxPassword] = useState('')
  const [httpsPasswordConfigured, setHttpsPasswordConfigured] = useState(false)
  const [networkHint, setNetworkHint] = useState<string | null>(null)
  const [restartNeeded, setRestartNeeded] = useState(false)
  const [restartBusy, setRestartBusy] = useState(false)
  const [mirrorEnabled, setMirrorEnabled] = useState(false)
  const [mirrorDest, setMirrorDest] = useState('')
  const [mirrorInterval, setMirrorInterval] = useState(15)
  const [mirrorDailyMoveKit, setMirrorDailyMoveKit] = useState(false)
  const [mirrorStatus, setMirrorStatus] = useState<string | null>(null)
  const [opsNotifyHint, setOpsNotifyHint] = useState<string | null>(null)
  const [smtpEnabled, setSmtpEnabled] = useState(false)
  const [smtpHost, setSmtpHost] = useState('')
  const [smtpPort, setSmtpPort] = useState(587)
  const [smtpSsl, setSmtpSsl] = useState(true)
  const [smtpUser, setSmtpUser] = useState('')
  const [smtpPassword, setSmtpPassword] = useState('')
  const [smtpFrom, setSmtpFrom] = useState('')
  const [smtpTo, setSmtpTo] = useState('')
  const [smtpPasswordSet, setSmtpPasswordSet] = useState(false)
  const [tgEnabled, setTgEnabled] = useState(false)
  const [tgChatId, setTgChatId] = useState('')
  const [tgToken, setTgToken] = useState('')
  const [tgTokenSet, setTgTokenSet] = useState(false)
  const [mirrorStaleAlert, setMirrorStaleAlert] = useState(false)
  const [mirrorStaleHours, setMirrorStaleHours] = useState(24)
  const [totpManualKey, setTotpManualKey] = useState<string | null>(null)
  const [totpUri, setTotpUri] = useState<string | null>(null)
  const [totpConfirmCode, setTotpConfirmCode] = useState('')
  const [totpRecoveryCodes, setTotpRecoveryCodes] = useState<string[] | null>(null)
  const [secTotpCode, setSecTotpCode] = useState('')
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

  const applyUiPrefs = useCallback((prefs: UiPrefs) => {
    setIdleLockEnabled(prefs.idleLockEnabled)
    setIdleLockMinutes(prefs.idleLockMinutes)
    setClipDefaultFolder(prefs.clipDefaultFolder)
  }, [])

  const loadAuth = useCallback(async () => {
    const a = (await fetch('/api/auth/status', { credentials: 'same-origin' }).then((r) => r.json())) as AuthInfo
    setAuth(a)
    const ui = await hydrateUiPrefs(a.ui)
    applyUiPrefs(ui)
    return a
  }, [applyUiPrefs])

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
    try {
      const idx = (await fetch('/api/notes/index', { credentials: 'same-origin' }).then((r) => r.json())) as {
        notes: NoteCatalogItem[]
      }
      setNoteCatalog(idx.notes ?? [])
    } catch {
      /* index optional */
    }
  }, [])

  const refreshNotes = useCallback(async () => {
    if (!vault?.configured) return
    const q = folder ? `?folder=${encodeURIComponent(folder)}` : ''
    try {
      const data = (await fetch(`/api/notes${q}`, { credentials: 'same-origin' }).then((r) => r.json())) as NoteSummary[]
      setNotes(data.filter((n) => !isStandaloneTodosNote(n.relativePath)))
    } catch {
      /* keep current list */
    }
  }, [folder, vault?.configured])

  const returnHomeAfterUnlock = useCallback(() => {
    if (saveTimer.current) {
      window.clearTimeout(saveTimer.current)
      saveTimer.current = null
    }
    savingRef.current = false
    setError(null)
    setSaveStatus('saved')
    setConflictDisk(null)
    setTitleEditing(false)
    setHistoryOpen(false)
    setSelectedId(null)
    setNote(null)
    setMobilePane('editor')
    setTasksRefreshKey((k) => k + 1)
    void loadVault()
  }, [loadVault])

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
    try {
      const mq = window.matchMedia('(max-width: 900px)')
      const onChange = () => setNarrowLayout(mq.matches)
      mq.addEventListener('change', onChange)
      return () => mq.removeEventListener('change', onChange)
    } catch {
      return undefined
    }
  }, [])

  useEffect(() => {
    if (!vault?.configured) return
    if (auth?.authRequired && !auth.authenticated) return
    const payload = parseClipHash(window.location.hash || '')
    if (!payload) return
    setPendingClip(payload)
    try {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
    } catch {
      /* ignore */
    }
  }, [vault?.configured, auth?.authenticated, auth?.authRequired])

  useEffect(() => {
    if (!vault?.configured) return
    const q = folder ? `?folder=${encodeURIComponent(folder)}` : ''
    fetch(`/api/notes${q}`)
      .then((r) => r.json())
      .then((data: NoteSummary[]) => {
        setNotes(data.filter((n) => !isStandaloneTodosNote(n.relativePath)))
      })
      .catch((e: Error) => setError(e.message))
  }, [folder, vault])

  useEffect(() => {
    if (!selectedId) {
      setNote(null)
      return
    }
    fetch(`/api/notes/${selectedId}`)
      .then(async (r) => {
        // Idle lock / session expiry — overlay handles this; don't flash "Note 401".
        if (r.status === 401) return null
        if (!r.ok) throw new Error(`Note ${r.status}`)
        return r.json() as Promise<NoteDetail>
      })
      .then((n) => {
        if (!n) return
        if (!isStandaloneTodosNote(n.relativePath)) rememberViewedNoteAndSync(selectedId)
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
      setSearchIndex(0)
      return
    }
    const handle = window.setTimeout(() => {
      fetch(`/api/search?q=${encodeURIComponent(query)}`)
        .then((r) => r.json())
        .then((data: { mode?: string; hits?: SearchHit[]; warning?: string }) => {
          const hits = Array.isArray(data.hits) ? data.hits : []
          const mode = data.mode || 'smart'
          setHits(hits)
          setSearchIndex(0)
          setSearchMeta(`${mode} · ${hits.length} results${data.warning ? ` · ${data.warning}` : ''}`)
          setSearchOpen(true)
        })
        .catch(() => setError('Search failed'))
    }, 160)
    return () => window.clearTimeout(handle)
  }, [query])

  useEffect(() => {
    if (!searchOpen) return
    const onPointerDown = (e: MouseEvent | PointerEvent) => {
      const wrap = searchWrapRef.current
      if (!wrap) return
      if (e.target instanceof Node && wrap.contains(e.target)) return
      setSearchOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [searchOpen])

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
      const revisionSent = editorRevisionRef.current
      // Always use the latest known ETag — callers may pass a stale value
      const etagToSend = etagRef.current || currentEtag
      const markdown = joinFrontMatter(frontMatterRef.current, bodyMarkdown)
      try {
        const res = await fetch(`/api/notes/${selectedId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ markdown, etag: etagToSend, force }),
        })
        if (res.status === 401) {
          // Session gone — idle lock overlay takes over; don't leave "Save failed" on the note.
          setSaveStatus('saved')
          return
        }
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
        savedRevisionRef.current = revisionSent
        if (data.note) {
          const serverMd = typeof data.note.markdown === 'string' ? data.note.markdown : markdown
          const serverSplit = splitFrontMatter(serverMd)
          const latestBody = draftRef.current
          // Adopt server front matter (bumped modified) when the body we saved is still current.
          if (sameMarkdown(joinFrontMatter(frontMatterRef.current, latestBody), markdown) ||
              sameMarkdown(joinFrontMatter(serverSplit.frontMatter, latestBody), serverMd)) {
            frontMatterRef.current = serverSplit.frontMatter
            setFrontMatter(serverSplit.frontMatter)
            baselineRef.current = joinFrontMatter(serverSplit.frontMatter, latestBody)
            setNote({ ...data.note, markdown: baselineRef.current })
          } else {
            // User typed during save — keep the draft, sync modified line into local FM.
            const mod = /^modified:\s*.*$/im.exec(serverSplit.frontMatter)?.[0]
            if (mod) {
              let fm = frontMatterRef.current
              if (/^modified:\s*.*$/im.test(fm)) fm = fm.replace(/^modified:\s*.*$/im, mod)
              else fm = fm.trimEnd() + '\n' + mod + '\n'
              frontMatterRef.current = fm
              setFrontMatter(fm)
            }
            setNote({ ...data.note, markdown: joinFrontMatter(frontMatterRef.current, latestBody) })
          }
        }
        setSaveStatus(editorRevisionRef.current === revisionSent ? 'saved' : 'editing')
        setConflictDisk(null)
        setError(null)
        setTasksRefreshKey((k) => k + 1)
        void refreshNotes()

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
    [selectedId, refreshNotes],
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

  async function toggleFavorite() {
    if (!note || !selectedId) return
    const full = joinFrontMatter(frontMatterRef.current, draftRef.current)
    const nextFav = !/favorite\s*:\s*(true|yes|1)/i.test(frontMatterRef.current)
    const next = setFavoriteInMarkdown(full, nextFav)
    const { frontMatter: fm, body } = splitFrontMatter(next)
    frontMatterRef.current = fm
    setFrontMatter(fm)
    setDraft(body)
    draftRef.current = body
    await saveNote(body, etagRef.current, true)
    void loadVault()
  }

  // Flush pending edits when the window/tab closes or hides (pop-out and main).
  // keepalive lets the PUT finish after the window is gone (Chrome + Safari).
  useEffect(() => {
    const flushPendingSave = () => {
      window.dispatchEvent(new Event('jotdex-editor-flush'))
      if (pastePendingRef.current) return
      if (saveTimer.current) {
        window.clearTimeout(saveTimer.current)
        saveTimer.current = null
      }
      const id = selectedIdRef.current
      if (!id) return
      if (saveStatusRef.current === 'conflict') return
      const pending = joinFrontMatter(frontMatterRef.current, draftRef.current)
      if (sameMarkdown(pending, baselineRef.current)) return
      const etagToSend = etagRef.current
      try {
        void fetch(`/api/notes/${id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ markdown: pending, etag: etagToSend, force: false }),
          credentials: 'same-origin',
          keepalive: true,
        })
        baselineRef.current = pending
      } catch {
        /* best-effort on unload */
      }
    }
    const onHide = () => {
      if (document.visibilityState === 'hidden') flushPendingSave()
    }
    window.addEventListener('pagehide', flushPendingSave)
    document.addEventListener('visibilitychange', onHide)
    return () => {
      window.removeEventListener('pagehide', flushPendingSave)
      document.removeEventListener('visibilitychange', onHide)
    }
  }, [])

  useEffect(() => {
    if (!templateMenu) return
    const place = () => {
      const btn = templateBtnRef.current
      if (!btn) return
      const r = btn.getBoundingClientRect()
      const menuWidth = 288
      const pad = 8
      let left = r.left
      left = Math.min(left, window.innerWidth - menuWidth - pad)
      left = Math.max(pad, left)
      setTemplateMenuPos({ top: r.bottom + 6, left })
    }
    place()
    const onPointerDown = (e: PointerEvent) => {
      const t = e.target as Node
      if (templateBtnRef.current?.contains(t)) return
      if ((e.target as HTMLElement)?.closest?.('.template-menu')) return
      setTemplateMenu(false)
    }
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => {
      window.removeEventListener('resize', place)
      window.removeEventListener('scroll', place, true)
      document.removeEventListener('pointerdown', onPointerDown, true)
    }
  }, [templateMenu])

  useEffect(() => {
    setHistoryOpen(false)
    setHistory([])
    setOutlineOpen(false)
    setBacklinksOpen(false)
    setBacklinks([])
  }, [selectedId])

  useEffect(() => {
    if (!deepLinkNoteId) return
    setSelectedId(deepLinkNoteId)
    setMobilePane('editor')
  }, [deepLinkNoteId])

  useEffect(() => {
    if (!popoutNoteId) return
    setSelectedId(popoutNoteId)
    document.documentElement.classList.add('popout-mode')
    document.title = 'Jotdex note'
    return () => document.documentElement.classList.remove('popout-mode')
  }, [popoutNoteId])

  useEffect(() => {
    if (!selectedId) return
    if (typeof window === 'undefined') return
    if (window.matchMedia('(max-width: 900px)').matches) setMobilePane('editor')
  }, [selectedId])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null
      const typing =
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.isContentEditable)

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'o') {
        e.preventDefault()
        setQuickOpen(true)
        setQuickQuery('')
        setQuickIndex(0)
        window.setTimeout(() => quickInputRef.current?.focus(), 0)
        return
      }

      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'p')) {
        e.preventDefault()
        searchRef.current?.focus()
        setSearchOpen(true)
        return
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault()
        if (pastePendingRef.current) {
          setError('Wait for image uploads to finish before saving.')
          setSaveStatus('uploading')
          return
        }
        window.dispatchEvent(new Event('jotdex-editor-flush'))
        if (saveTimer.current) window.clearTimeout(saveTimer.current)
        void saveNote(draftRef.current, etagRef.current)
        return
      }

      if (quickOpen) {
        const filtered = filterQuick(noteCatalog, quickQuery)
        if (e.key === 'Escape') {
          e.preventDefault()
          setQuickOpen(false)
          return
        }
        if (e.key === 'ArrowDown' && filtered.length) {
          e.preventDefault()
          setQuickIndex((i) => (i + 1) % filtered.length)
          return
        }
        if (e.key === 'ArrowUp' && filtered.length) {
          e.preventDefault()
          setQuickIndex((i) => (i - 1 + filtered.length) % filtered.length)
          return
        }
        if (e.key === 'Enter' && filtered.length) {
          e.preventDefault()
          const pick = filtered[quickIndex] ?? filtered[0]
          if (pick) {
            setSelectedId(pick.id)
            setMobilePane('editor')
            setQuickOpen(false)
          }
          return
        }
      }

      if (searchOpen && query.trim() && document.activeElement === searchRef.current) {
        const list = hitsRef.current
        if (e.key === 'ArrowDown' && list.length) {
          e.preventDefault()
          setSearchIndex((i) => Math.min(i + 1, list.length - 1))
          return
        }
        if (e.key === 'ArrowUp' && list.length) {
          e.preventDefault()
          setSearchIndex((i) => Math.max(i - 1, 0))
          return
        }
        if (e.key === 'Enter' && list.length) {
          e.preventDefault()
          const pick = list[searchIndexRef.current] ?? list[0]
          if (pick) {
            setSelectedId(pick.noteId)
            setMobilePane('editor')
            setQuery('')
            setSearchOpen(false)
          }
          return
        }
        if (e.key === 'Escape') {
          e.preventDefault()
          setSearchOpen(false)
          return
        }
      }

      if (e.key === 'Escape' && !typing) {
        setSearchOpen(false)
        setTemplateMenu(false)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [draft, etag, saveNote, quickOpen, quickQuery, quickIndex, noteCatalog, searchOpen, query])

  async function toggleHistory() {
    if (!selectedId) return
    if (historyOpen) {
      setHistoryOpen(false)
      return
    }
    setOutlineOpen(false)
    setBacklinksOpen(false)
    setFormatLintOpen(false)
    const rows = await fetch(`/api/notes/${selectedId}/history`).then((r) => r.json())
    setHistory(rows)
    setHistoryOpen(true)
  }

  async function toggleFormatLint() {
    if (formatLintOpen) {
      setFormatLintOpen(false)
      return
    }
    setHistoryOpen(false)
    setOutlineOpen(false)
    setBacklinksOpen(false)
    setFormatLintBusy(true)
    try {
      const issues = await lintNoteMarkdown(draft)
      setFormatLintIssues(issues)
      setFormatLintOpen(true)
    } catch {
      setFormatLintIssues([])
      setFormatLintOpen(true)
    } finally {
      setFormatLintBusy(false)
    }
  }

  async function toggleBacklinks() {
    if (!selectedId) return
    if (backlinksOpen) {
      setBacklinksOpen(false)
      return
    }
    setHistoryOpen(false)
    setOutlineOpen(false)
    setFormatLintOpen(false)
    const data = (await fetch(`/api/notes/${selectedId}/backlinks`).then((r) => r.json())) as {
      links: { noteId: string; title: string; relativePath: string; folderPath: string; context?: string }[]
    }
    setBacklinks(data.links ?? [])
    setBacklinksOpen(true)
  }

  function toggleOutline() {
    setHistoryOpen(false)
    setBacklinksOpen(false)
    setFormatLintOpen(false)
    setOutlineOpen((o) => !o)
  }

  function addNetworkSite() {
    const n = nextNetworkSiteNumber(draft)
    const label = window.prompt(`Name for site ${n}`, `Site ${n}`)
    if (label === null) return
    const block = networkSiteMarkdown(n, label.trim() || `Site ${n}`)
    let next = draft
    if (next.includes(NETWORK_SITES_END)) {
      next = next.replace(NETWORK_SITES_END, `${block}\n${NETWORK_SITES_END}`)
    } else {
      next = `${next.trimEnd()}\n\n${block}`
    }
    setDraft(next)
    draftRef.current = next
    setEditorEpoch((e) => e + 1)
    setSaveStatus('editing')
  }

  async function compareSnapshot(snapshotId: string) {
    if (!selectedId || !note) return
    const snap = await fetch(`/api/notes/${selectedId}/history/${snapshotId}`).then((r) => r.json())
    const oldMd = (snap.markdown as string) ?? ''
    const current = joinFrontMatter(frontMatterRef.current, draftRef.current)
    setHistoryDiff({ snapshotId, lines: diffLines(oldMd, current) })
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
      setHttpsSelfSigned(!!n.httpsSelfSigned)
      if (typeof n.httpsPort === 'number') setHttpsPort(n.httpsPort)
      else if (typeof n.port === 'number') setHttpsPort(n.port + 1)
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
      setMirrorDailyMoveKit(!!m.includeDailyMoveKit)
      const st = m.status
      if (st?.lastSucceededUtc) {
        const kit =
          st.lastMoveKitUtc != null
            ? ` · Move kit: ${new Date(st.lastMoveKitUtc).toLocaleString()}`
            : m.includeDailyMoveKit
              ? ' · Daily move kit: waiting'
              : ''
        setMirrorStatus(`Last OK: ${new Date(st.lastSucceededUtc).toLocaleString()}${kit}`)
      } else if (st?.lastError) {
        setMirrorStatus(`Last error: ${st.lastError}`)
      } else {
        setMirrorStatus(st?.hint ?? null)
      }
    } catch {
      /* ignore */
    }
  }

  async function loadOpsNotifications() {
    try {
      const n = await fetch('/api/settings/notifications', { credentials: 'same-origin' }).then((r) => r.json())
      setSmtpEnabled(!!n.smtp?.enabled)
      setSmtpHost(n.smtp?.host ?? '')
      setSmtpPort(typeof n.smtp?.port === 'number' ? n.smtp.port : 587)
      setSmtpSsl(n.smtp?.useSsl !== false)
      setSmtpUser(n.smtp?.username ?? '')
      setSmtpFrom(n.smtp?.fromAddress ?? '')
      setSmtpTo(n.smtp?.toAddress ?? '')
      setTgEnabled(!!n.telegram?.enabled)
      setTgChatId(n.telegram?.chatId ?? '')
      setMirrorStaleAlert(!!n.alerts?.mirrorStaleEnabled)
      setMirrorStaleHours(typeof n.alerts?.mirrorStaleHours === 'number' ? n.alerts.mirrorStaleHours : 24)
      setSmtpPasswordSet(!!n.status?.smtpPasswordSet)
      setTgTokenSet(!!n.status?.telegramTokenSet)
      setSmtpPassword('')
      setTgToken('')
    } catch {
      /* ignore */
    }
  }

  async function createAdminPassword() {
    setSecHint(null)
    if (secPassword !== secConfirm) {
      setSecHint('Passwords do not match.')
      return
    }
    if (secPassword.length < 10) {
      setSecHint('Password must be at least 6 characters.')
      return
    }
    setSecBusy(true)
    try {
      const res = await fetch('/api/auth/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ password: secPassword }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setSecHint(data.error ?? 'Could not create password')
        return
      }
      setSecPassword('')
      setSecConfirm('')
      setSecHint('Password saved. Idle lock is available below if you want it.')
      await loadAuth()
    } catch (e) {
      setSecHint(e instanceof Error ? e.message : 'Could not create password')
    } finally {
      setSecBusy(false)
    }
  }

  async function signInForSecurity() {
    setSecHint(null)
    setSecBusy(true)
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          password: secCurrentPassword,
          totpCode: auth?.totpEnabled || secTotpCode.trim() ? secTotpCode.trim() : undefined,
        }),
      })
      const data = await res.json()
      if (data.requiresTotp) {
        setSecHint('Enter your authenticator code below, then Unlock again.')
        return
      }
      if (!res.ok || !data.success) {
        setSecHint(data.error ?? 'Incorrect password')
        return
      }
      setSecCurrentPassword('')
      setSecTotpCode('')
      setSecHint('Unlocked — you can change or remove the password.')
      await loadAuth()
    } catch (e) {
      setSecHint(e instanceof Error ? e.message : 'Sign-in failed')
    } finally {
      setSecBusy(false)
    }
  }

  async function changeAdminPassword() {
    setSecHint(null)
    if (secNewPassword !== secNewConfirm) {
      setSecHint('New passwords do not match.')
      return
    }
    if (secNewPassword.length < 10) {
      setSecHint('New password must be at least 6 characters.')
      return
    }
    setSecBusy(true)
    try {
      const res = await fetch('/api/auth/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ currentPassword: secCurrentPassword, newPassword: secNewPassword }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setSecHint(data.error ?? (res.status === 401 ? 'Unlock with your password first.' : 'Could not change password'))
        return
      }
      setSecCurrentPassword('')
      setSecNewPassword('')
      setSecNewConfirm('')
      setSecHint('Password updated.')
      await loadAuth()
    } catch (e) {
      setSecHint(e instanceof Error ? e.message : 'Could not change password')
    } finally {
      setSecBusy(false)
    }
  }

  async function removeAdminPassword() {
    setSecHint(null)
    if (!secCurrentPassword) {
      setSecHint('Enter your current password to remove protection.')
      return
    }
    if (!window.confirm('Remove the password? Jotdex will open without asking again until you set a new one.')) {
      return
    }
    setSecBusy(true)
    try {
      const res = await fetch('/api/auth/remove-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ currentPassword: secCurrentPassword }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        setSecHint(data.error ?? 'Could not remove password')
        return
      }
      setSecCurrentPassword('')
      setSecNewPassword('')
      setSecNewConfirm('')
      setIdleLockEnabled(false)
      void saveUiPrefs({ idleLockEnabled: false, idleLockMinutes })
      setSecHint('Password removed. The app opens freely again.')
      await loadAuth()
    } catch (e) {
      setSecHint(e instanceof Error ? e.message : 'Could not remove password')
    } finally {
      setSecBusy(false)
    }
  }

  async function saveNetworkSettings() {
    setNetworkHint(null)
    const body: Record<string, unknown> = {
      bindMode,
      port: listenPort,
      httpsSelfSigned,
      httpsPort: httpsSelfSigned || httpsPfxPath.trim() ? httpsPort : 0,
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
    if (typeof data.httpsPort === 'number') setHttpsPort(data.httpsPort)
    setRestartNeeded(true)
    const httpsHint = data.httpsUrl
      ? ` HTTP ${data.httpUrl || data.listenUrl}; HTTPS ${data.httpsUrl} (self-signed — click past the browser warning).`
      : ` ${data.listenUrl}.`
    let hint = `Saved.${httpsHint} Restart the server to apply.`
    if (data.firewall?.message) {
      hint += data.firewall.success
        ? ` Firewall: ${data.firewall.message}`
        : ` Firewall note: ${data.firewall.message}`
    }
    setNetworkHint(hint)
  }

  async function ensureLanFirewall() {
    setNetworkHint(null)
    setError(null)
    try {
      const res = await fetch('/api/settings/network/firewall', { method: 'POST' })
      const data = await res.json()
      if (data.message) setNetworkHint(data.message)
      else if (!data.success) setNetworkHint('Could not update Windows Firewall rules.')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Firewall request failed')
    }
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
        includeDailyMoveKit: mirrorDailyMoveKit,
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

  async function applyMoveFolder(srcPath: string, destParent: string) {
    const src = srcPath.replace(/\\/g, '/')
    const dest = destParent.replace(/\\/g, '/')
    if (src === dest || dest.startsWith(src + '/')) {
      throw new Error('Cannot move a folder into itself')
    }
    const parentOf = src.includes('/') ? src.slice(0, src.lastIndexOf('/')) : ''
    if (parentOf === dest) return
    const res = await fetch('/api/folders', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: src, newParent: dest }),
    })
    const data = await res.json()
    if (!res.ok) {
      const msg = data.error ?? 'Could not move folder'
      setError(msg)
      throw new Error(msg)
    }
    setFolder(data.path ?? dest)
    setSelectedId(null)
    await loadVault()
    setNetworkHint(`Moved folder to ${dest || 'vault root'}`)
    window.setTimeout(() => setNetworkHint(null), 2500)
  }

  async function moveFolder() {
    if (!folder) {
      setError('Select a folder first')
      return
    }
    setMovePicker({ kind: 'folder', path: folder })
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

  async function createNote(template?: NoteTemplate) {
    const suggested =
      typeof template?.defaultTitle === 'function'
        ? template.defaultTitle(new Date().toISOString().slice(0, 10))
        : (template?.defaultTitle ?? (template?.id === 'daily' ? new Date().toISOString().slice(0, 10) : 'Untitled'))
    const title = window.prompt('New note title', suggested)
    if (!title?.trim()) return
    const markdown = template ? template.body(title.trim()) : undefined
    const res = await fetch('/api/notes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: title.trim(), folder, markdown }),
    })
    const data = await res.json()
    if (!res.ok) {
      setError(data.error ?? 'Could not create note')
      return
    }
    setTemplateMenu(false)
    await loadVault()
    setSelectedId(data.id)
  }

  async function fetchPageInfo(url: string): Promise<{
    title?: string
    description?: string
    textExcerpt?: string
    finalUrl?: string
  } | null> {
    const res = await fetch('/api/fetch-page', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url }),
    })
    const data = await res.json().catch(() => null)
    if (!res.ok || !data?.success) return null
    return data
  }

  function pageInfoToMarkdown(opts: {
    url: string
    title?: string
    description?: string
    textExcerpt?: string
    asNewNoteBody?: boolean
  }): string {
    const linkTitle = opts.title?.trim() || opts.url
    const lines: string[] = []
    if (opts.asNewNoteBody) {
      // Title is already the note H1 from clip/create — body starts with source.
    } else if (opts.title?.trim()) {
      lines.push(`## ${opts.title.trim()}`, '')
    }
    lines.push(`> Source: [${linkTitle}](${opts.url})`, '')
    if (opts.description?.trim()) {
      lines.push(opts.description.trim(), '')
    }
    if (opts.textExcerpt?.trim()) {
      lines.push(opts.textExcerpt.trim(), '')
    }
    return lines.join('\n')
  }

  async function createNoteFromUrl() {
    setTemplateMenu(false)
    const url = window.prompt('Page URL to save as a new note')
    if (!url?.trim()) return
    setNetworkHint('Fetching page…')
    const info = await fetchPageInfo(url.trim())
    setNetworkHint(null)
    const finalUrl = (info?.finalUrl || url).trim()
    let title = info?.title?.trim() || ''
    if (!title) {
      try {
        title = new URL(finalUrl).hostname.replace(/^www\./, '')
      } catch {
        title = 'Web clip'
      }
    }
    if (!info && !window.confirm('Could not fetch page info. Save with the URL only?')) return
    title = window.prompt('Note title', title) || title
    const dest = window.prompt('Folder (blank = Inbox)', folder || clipDefaultFolder)
    if (dest === null) return
    const folderDest = dest.trim() || 'Inbox'
    const body = pageInfoToMarkdown({
      url: finalUrl,
      title: info?.title,
      description: info?.description,
      textExcerpt: info?.textExcerpt,
      asNewNoteBody: true,
    })
    const res = await fetch('/api/clip', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        title: title.trim(),
        sourceUrl: finalUrl,
        folder: folderDest,
        text: body,
      }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      setError(data.error ?? 'Could not create note from URL')
      return
    }
    void saveUiPrefs({ clipDefaultFolder: folderDest })
    await loadVault()
    setSelectedId(data.noteId)
    setMobilePane('editor')
  }

  async function insertUrlIntoNote() {
    if (!note) return
    const url = window.prompt('Page URL to pull into this note')
    if (!url?.trim()) return
    setNetworkHint('Fetching page…')
    const info = await fetchPageInfo(url.trim())
    setNetworkHint(null)
    if (!info && !window.confirm('Could not fetch page info. Insert the URL only?')) return
    const finalUrl = (info?.finalUrl || url).trim()
    const block =
      '\n\n' +
      pageInfoToMarkdown({
        url: finalUrl,
        title: info?.title,
        description: info?.description,
        textExcerpt: info?.textExcerpt,
      })
    setDraft((d) => (d.endsWith('\n') ? d + block.trimStart() : d + block))
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
    setNotes(list.filter((n) => !isStandaloneTodosNote(n.relativePath)))
    bumpTasksRefresh()
  }

  async function renameNote() {
    if (!selectedId || !note) return
    const title = window.prompt('Rename note to', note.title)
    if (title === null) return
    await applyRename(title)
  }

  async function applyRename(rawTitle: string) {
    if (!selectedId || !note) return
    const title = rawTitle.trim()
    if (!title || title === note.title) return
    const res = await fetch(`/api/notes/${selectedId}/move`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, folder: note.folderPath }),
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

  async function applyMoveNote(noteId: string, title: string, destFolder: string, fromFolder?: string) {
    const dest = destFolder.replace(/\\/g, '/')
    const from = (fromFolder ?? '').replace(/\\/g, '/')
    if (dest === from) return
    const res = await fetch(`/api/notes/${noteId}/move`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, folder: dest }),
    })
    const data = await res.json()
    if (!res.ok || !data.success) {
      const msg = data.error ?? 'Move failed'
      setError(msg)
      throw new Error(msg)
    }
    if (data.note && selectedIdRef.current === noteId) {
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
    setNetworkHint(`Moved to ${dest || 'vault root'}`)
    window.setTimeout(() => setNetworkHint(null), 2500)
  }

  async function moveNote() {
    if (!selectedId || !note) return
    setMovePicker({ kind: 'note' })
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

  const openPopout = useCallback((noteId: string) => {
    // Flush any pending debounce before opening another editor on the same note.
    if (saveTimer.current) {
      window.clearTimeout(saveTimer.current)
      saveTimer.current = null
    }
    const pending = joinFrontMatter(frontMatterRef.current, draftRef.current)
    if (selectedIdRef.current === noteId && !sameMarkdown(pending, baselineRef.current)) {
      void saveNote(draftRef.current, etagRef.current)
    }

    const url = new URL(window.location.href)
    url.searchParams.set('popout', noteId)
    url.hash = ''
    // popup=yes works in Chromium; Safari may ignore chrome flags but still opens a window.
    const features = 'popup=yes,width=440,height=620,resizable=yes,scrollbars=yes'
    const win = window.open(url.toString(), `jotdex-note-${noteId}`, features)
    if (!win) {
      setError('Pop-out blocked — allow pop-ups for this site in Chrome/Safari, then try again.')
    } else {
      try {
        win.focus()
      } catch {
        /* ignore */
      }
    }
  }, [saveNote])

  const copyAiPrompt = useCallback(async () => {
    try {
      await copyJotdexAiPrompt()
      setAiPromptHint('AI note prompt copied — paste it into ChatGPT, Claude, etc.')
      window.setTimeout(() => setAiPromptHint(null), 3500)
    } catch {
      setError('Could not copy to clipboard')
    }
  }, [])

  if (auth && auth.authRequired && !auth.authenticated) {
    return (
      <LoginScreen
        onLoggedIn={() => {
          void loadAuth().then(() => loadVault())
        }}
      />
    )
  }

  const pathOnly = typeof window !== 'undefined' ? window.location.pathname.replace(/\/+$/, '') || '/' : '/'
  if (pathOnly === '/capture') {
    // Old capture URL — send people to the main app (bookmarklet now uses /#clip=…).
    if (typeof window !== 'undefined') {
      const hash = window.location.hash || ''
      window.location.replace('/' + (hash.startsWith('#clip=') ? hash : ''))
    }
    return <p className="muted">Opening Jotdex…</p>
  }

  // Password is optional, so setupRequired is rarely true — still use the full wizard when
  // no vault is bound (vault + optional password + network), not only the thin folder picker.
  if ((auth && auth.setupRequired) || (vault && !vault.configured)) {
    return (
      <FirstRunWizard
        onComplete={() => {
          void loadAuth().then(() => loadVault())
        }}
      />
    )
  }

  if (popoutNoteId) {
    return (
      <div className={`app popout-app${popoutChromeAutoHide ? ' chrome-autohide' : ''}`}>
        {error && saveStatus !== 'conflict' && <p className="err">{error}</p>}
        {aiPromptHint && <p className="upload-status">{aiPromptHint}</p>}
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
        {!note && <p className="muted">Loading note…</p>}
        {note && (
          <div className="popout-body">
            <header className="popout-bar">
              <div className="popout-bar-main">
                <p className="brand">Jotdex</p>
                <h1 title={note.relativePath}>{note.title}</h1>
              </div>
              <div className="popout-bar-actions">
                <span className={`save-chip ${saveStatus}`} title={saveChipLabel(saveStatus)}>
                  {saveChipLabel(saveStatus)}
                </span>
                <button
                  type="button"
                  className="ghost"
                  title={showSource ? 'Switch to visual editor' : 'Edit Markdown source'}
                  onClick={() => {
                    setShowSource((s) => {
                      if (s) setSourceForced(null)
                      return !s
                    })
                  }}
                >
                  {showSource ? 'Visual' : 'Source'}
                </button>
                <button
                  type="button"
                  className={`ghost${popoutChromeAutoHide ? ' on' : ''}`}
                  title="Auto-hide formatting tools until hover, or keep them pinned"
                  onClick={() => {
                    setPopoutChromeAutoHide((v) => {
                      const next = !v
                      try {
                        localStorage.setItem('jotdex.popoutChromeAutoHide', next ? '1' : '0')
                      } catch {
                        /* ignore */
                      }
                      return next
                    })
                  }}
                >
                  {popoutChromeAutoHide ? 'Auto' : 'Pin'}
                </button>
              </div>
            </header>
            {sourceForced && showSource && (
              <div className="source-banner">
                <p>{sourceForced}</p>
                <button type="button" className="ghost" onClick={() => { setSourceForced(null); setShowSource(false) }}>
                  Try visual anyway
                </button>
              </div>
            )}
            {showSource ? (
              <textarea className="source-editor" value={draft} onChange={(e) => setDraft(e.target.value)} spellCheck={false} />
            ) : (
              <NoteEditor
                key={note.id}
                noteId={note.id}
                noteStem={note.relativePath.replace(/^.*\//, '').replace(/\.md$/i, '')}
                noteRelativePath={note.relativePath}
                noteCatalog={noteCatalog}
                markdown={draft}
                contentEpoch={editorEpoch}
                jumpHeading={jumpHeading}
                headingFolds={note.headingFolds}
                onOutline={setLiveOutline}
                attachments={note.attachments}
                onChange={(md, rev) => {
                  draftRef.current = md
                  setDraft(md)
                  if (typeof rev === 'number') editorRevisionRef.current = rev
                }}
                onPastePending={(pending) => {
                  pastePendingRef.current = pending
                  if (pending) setSaveStatus('uploading')
                  else if (saveStatusRef.current === 'uploading') setSaveStatus('editing')
                }}
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
          </div>
        )}
      <IdleLockGate
        enabled={idleLockEnabled}
        minutes={idleLockMinutes}
        authAvailable={!!auth?.setupComplete}
        totpEnabled={!!auth?.totpEnabled}
        onUnlocked={() => {
          setError(null)
          setSaveStatus('saved')
        }}
      />
      </div>
    )
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-brand">
          <span
            className="brand brand-home"
            role="button"
            tabIndex={0}
            title="Home"
            onClick={() => {
              setSelectedId(null)
              setMobilePane('editor')
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                setSelectedId(null)
                setMobilePane('editor')
              }
            }}
          >
            Jotdex
          </span>
          {vault?.configured && (
            <span className="vault-pill">{vault.name} · {vault.noteCount}</span>
          )}
        </div>
        <div className="search-wrap" ref={searchWrapRef}>
          <input
            ref={searchRef}
            className="search-input"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onFocus={() => query.trim() && setSearchOpen(true)}
            onBlur={(e) => {
              // Safari often blurs before the result button receives click; don't dismiss
              // if focus is moving into the dropdown (or still inside the wrap).
              const next = e.relatedTarget
              if (next instanceof Node && searchWrapRef.current?.contains(next)) return
              window.setTimeout(() => {
                const wrap = searchWrapRef.current
                if (wrap?.contains(document.activeElement)) return
                setSearchOpen(false)
              }, 180)
            }}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown' && hits.length) {
                e.preventDefault()
                setSearchOpen(true)
                setSearchIndex((i) => Math.min(i + 1, hits.length - 1))
              } else if (e.key === 'ArrowUp' && hits.length) {
                e.preventDefault()
                setSearchIndex((i) => Math.max(i - 1, 0))
              } else if (e.key === 'Enter' && hits.length && searchOpen) {
                e.preventDefault()
                const pick = hits[searchIndex] ?? hits[0]
                if (pick) {
                  setSelectedId(pick.noteId)
                  setMobilePane('editor')
                  setQuery('')
                  setSearchOpen(false)
                }
              } else if (e.key === 'Escape') {
                setSearchOpen(false)
              }
            }}
            placeholder="Search titles and note text…  (Ctrl+K)"
            aria-label="Search notes"
            aria-autocomplete="list"
            aria-controls="search-results"
            aria-activedescendant={hits[searchIndex] ? `search-hit-${hits[searchIndex].noteId}` : undefined}
          />
          {searchOpen && query.trim() && (
            <div className="search-dropdown" id="search-results" role="listbox">
              <div className="search-meta">{searchMeta || 'Searching…'} · ↑↓ Enter</div>
              <ul>
                {hits.map((h, i) => (
                  <li key={h.noteId}>
                    <button
                      type="button"
                      id={`search-hit-${h.noteId}`}
                      role="option"
                      aria-selected={i === searchIndex}
                      className={i === searchIndex ? 'active' : ''}
                      onMouseEnter={() => setSearchIndex(i)}
                      onMouseDown={(e) => {
                        // Prevent input blur from unmounting this button before click (Safari/Mac).
                        e.preventDefault()
                      }}
                      onClick={() => {
                        setSelectedId(h.noteId)
                        setMobilePane('editor')
                        setQuery('')
                        setSearchOpen(false)
                      }}
                    >
                      <span className="note-title">{h.title || 'Untitled'}</span>
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
            title="Copy a prompt that teaches ChatGPT/Claude/etc. how to format Markdown for Jotdex"
            onClick={() => void copyAiPrompt()}
          >
            AI prompt
          </button>
          <button
            type="button"
            className="ghost"
            onClick={() => {
              setSettingsOpen(true)
              setSettingsTab('vault')
              setNetworkHint(null)
              setUpdateInfo(null)
              setNotifyPerm(getNotificationPermission())
              setNotifyHint(null)
              void openBrowse(vaultPathInput || undefined)
              void loadNetworkSettings()
              void loadMirrorSettings()
              void loadOpsNotifications()
            }}
          >
            Settings
          </button>
          {auth && auth.setupComplete && auth.authenticated && (
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
              Lock
            </button>
          )}
        </div>
      </header>

      {aiPromptHint && <p className="ai-prompt-toast">{aiPromptHint}</p>}

      {pendingClip && (
        <ClipSaveModal
          initial={pendingClip}
          onClose={() => setPendingClip(null)}
          onSaved={(id) => {
            setPendingClip(null)
            void loadVault().then(() => {
              setSelectedId(id)
              setMobilePane('editor')
            })
          }}
        />
      )}
      {newNoteModalOpen && (
        <NewNoteModal
          tree={tree}
          defaultFolder={folder}
          onClose={() => setNewNoteModalOpen(false)}
          onCreated={(id, folderPath) => {
            setNewNoteModalOpen(false)
            setFolder(folderPath)
            void loadVault().then(() => {
              setSelectedId(id)
              setShowTrash(false)
              setMobilePane('editor')
            })
          }}
        />
      )}
      {movePicker?.kind === 'note' && note && (
        <FolderPickerModal
          tree={tree}
          title="Move note"
          lede={`Choose a folder for “${note.title}”. Current: ${note.folderPath || 'vault root'}.`}
          confirmLabel="Move note"
          initialPath={note.folderPath}
          onClose={() => setMovePicker(null)}
          onPick={(dest) => applyMoveNote(note.id, note.title, dest, note.folderPath).then(() => setMovePicker(null))}
        />
      )}
      {movePicker?.kind === 'folder' && (
        <FolderPickerModal
          tree={tree}
          title="Move folder"
          lede={`Choose a parent for “${movePicker.path}”. Leave vault root selected to put it at the top level.`}
          confirmLabel="Move folder"
          initialPath={
            movePicker.path.includes('/')
              ? movePicker.path.slice(0, movePicker.path.lastIndexOf('/'))
              : ''
          }
          disablePath={movePicker.path}
          onClose={() => setMovePicker(null)}
          onPick={(dest) => applyMoveFolder(movePicker.path, dest).then(() => setMovePicker(null))}
        />
      )}
      {settingsOpen && (
        <div className="modal-backdrop" onClick={() => setSettingsOpen(false)} role="presentation">
          <div className="modal settings-modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Settings">
            <div className="settings-modal-head">
              <h2>Settings</h2>
              <button type="button" className="ghost" onClick={() => setSettingsOpen(false)}>
                Close
              </button>
            </div>
            <nav className="settings-tabs" aria-label="Settings sections">
              {(
                [
                  ['vault', 'Vault'],
                  ['network', 'Network'],
                  ['security', 'Security'],
                  ['capture', 'Capture'],
                  ['notifications', 'Notifications'],
                  ['backup', 'Backup'],
                  ['updates', 'Updates'],
                  ['advanced', 'Advanced'],
                ] as const
              ).map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  className={settingsTab === id ? 'on' : ''}
                  onClick={() => {
                    setSettingsTab(id)
                    requestAnimationFrame(() => {
                      if (settingsPanelRef.current) settingsPanelRef.current.scrollTop = 0
                    })
                  }}
                >
                  {label}
                </button>
              ))}
            </nav>
            <div className="settings-tab-panel" ref={settingsPanelRef}>
            {settingsTab === 'vault' && (
              <>
            <h2 className="settings-section settings-section-first">Vault location</h2>
            <p className="lede">Pick the folder that contains your .md notes. Use local disk for the live vault.</p>
            <label className="field">
              Path
              <input value={vaultPathInput} onChange={(e) => setVaultPathInput(e.target.value)} placeholder="C:\JotdexVault" />
            </label>
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void applyVaultPath(vaultPathInput)}>
                Use this folder
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
              </>
            )}

            {settingsTab === 'network' && (
              <>
            <h2 className="settings-section settings-section-first">Network</h2>
            <p className="lede">
              Default is this PC only. LAN access is opt-in. Saving LAN prompts Windows UAC to allow HTTP/HTTPS through the firewall; if you decline or lack permission, LAN still saves — open the ports manually if other PCs cannot connect. Enable self-signed HTTPS to use https:// as well as http:// (browser will warn — that is expected). Restart required after changes.
            </p>
            <label className="field">
              Binding
              <select value={bindMode} onChange={(e) => setBindMode(e.target.value as 'loopback' | 'lan')}>
                <option value="loopback">This PC only (127.0.0.1)</option>
                <option value="lan">LAN (all interfaces)</option>
              </select>
            </label>
            <label className="field">
              HTTP port
              <input
                type="number"
                min={1}
                max={65535}
                value={listenPort}
                onChange={(e) => {
                  const p = Number(e.target.value) || 5180
                  setListenPort(p)
                  if (!httpsPfxPath && httpsPort === listenPort + 1) setHttpsPort(p + 1)
                }}
              />
            </label>
            <label className="field checkbox-row">
              <input
                type="checkbox"
                checked={httpsSelfSigned}
                onChange={(e) => setHttpsSelfSigned(e.target.checked)}
              />
              Also listen on HTTPS (self-signed certificate)
            </label>
            {(httpsSelfSigned || !!httpsPfxPath.trim()) && (
              <label className="field">
                HTTPS port
                <input
                  type="number"
                  min={1}
                  max={65535}
                  value={httpsPort}
                  onChange={(e) => setHttpsPort(Number(e.target.value) || listenPort + 1)}
                />
              </label>
            )}
            <p className="muted">
              With HTTPS on, open both e.g. http://127.0.0.1:{listenPort} and https://127.0.0.1:
              {httpsSelfSigned || httpsPfxPath.trim() ? httpsPort : listenPort + 1}. Click through the certificate warning for HTTPS.
            </p>
            <label className="field">
              Custom HTTPS certificate (optional PFX path)
              <input
                value={httpsPfxPath}
                onChange={(e) => setHttpsPfxPath(e.target.value)}
                placeholder="Leave blank to use the built-in self-signed cert"
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
            <p className="muted">
              Leave blank unless you use your own certificate file. Most people only need the self-signed HTTPS checkbox
              above (no PFX password). If you do use a custom PFX, you can type the password here, or set the env var
              JOTDEX_HTTPS_PFX_PASSWORD so it is not stored in config on disk.
            </p>
            {bindMode === 'lan' && !httpsSelfSigned && !httpsPfxPath.trim() && (
              <p className="warn">LAN without HTTPS exposes the app in cleartext on your network. Prefer enabling self-signed HTTPS or a PFX.</p>
            )}
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void saveNetworkSettings()}>
                Save network
              </button>
              {bindMode === 'lan' && (
                <button
                  type="button"
                  className="ghost"
                  onClick={() => void ensureLanFirewall()}
                  title="Prompt UAC again to add Windows Firewall allow rules for the current HTTP/HTTPS ports"
                >
                  Open firewall ports
                </button>
              )}
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
              </>
            )}

            {settingsTab === 'security' && (
              <>
            <h2 className="settings-section settings-section-first">Security</h2>
            <p className="lede">
              Optional password protection — no username. When a password is set, Jotdex asks for it on open. You can
              remove it anytime to open freely again.
            </p>
            {!auth?.setupComplete ? (
              <div className="security-block">
                <h3 className="settings-subhead">Set password</h3>
                <label className="field">
                  Password (at least 6 characters)
                  <input
                    type="password"
                    value={secPassword}
                    onChange={(e) => setSecPassword(e.target.value)}
                    autoComplete="new-password"
                  />
                </label>
                <label className="field">
                  Confirm password
                  <input
                    type="password"
                    value={secConfirm}
                    onChange={(e) => setSecConfirm(e.target.value)}
                    autoComplete="new-password"
                  />
                </label>
                <div className="modal-actions">
                  <button type="button" className="primary" disabled={secBusy} onClick={() => void createAdminPassword()}>
                    {secBusy ? 'Saving…' : 'Save password'}
                  </button>
                </div>
              </div>
            ) : (
              <div className="security-block">
                <p className="muted">Password is on. The app requires it to open.</p>
                {!auth.authenticated && (
                  <>
                    <h3 className="settings-subhead">Unlock to manage</h3>
                    <label className="field">
                      Password
                      <input
                        type="password"
                        value={secCurrentPassword}
                        onChange={(e) => setSecCurrentPassword(e.target.value)}
                        autoComplete="current-password"
                      />
                    </label>
                    {(auth.totpEnabled || secTotpCode) && (
                      <label className="field">
                        Authenticator code
                        <input
                          type="text"
                          inputMode="numeric"
                          autoComplete="one-time-code"
                          value={secTotpCode}
                          onChange={(e) => setSecTotpCode(e.target.value)}
                          placeholder="6-digit or recovery code"
                        />
                      </label>
                    )}
                    <div className="modal-actions">
                      <button type="button" className="primary" disabled={secBusy} onClick={() => void signInForSecurity()}>
                        {secBusy ? 'Unlocking…' : 'Unlock'}
                      </button>
                    </div>
                  </>
                )}
                {auth.authenticated && (
                  <>
                    <h3 className="settings-subhead">Change password</h3>
                    <label className="field">
                      Current password
                      <input
                        type="password"
                        value={secCurrentPassword}
                        onChange={(e) => setSecCurrentPassword(e.target.value)}
                        autoComplete="current-password"
                      />
                    </label>
                    <label className="field">
                      New password
                      <input
                        type="password"
                        value={secNewPassword}
                        onChange={(e) => setSecNewPassword(e.target.value)}
                        autoComplete="new-password"
                      />
                    </label>
                    <label className="field">
                      Confirm new password
                      <input
                        type="password"
                        value={secNewConfirm}
                        onChange={(e) => setSecNewConfirm(e.target.value)}
                        autoComplete="new-password"
                      />
                    </label>
                    <div className="modal-actions">
                      <button type="button" className="primary" disabled={secBusy} onClick={() => void changeAdminPassword()}>
                        {secBusy ? 'Saving…' : 'Update password'}
                      </button>
                      <button type="button" className="ghost" disabled={secBusy} onClick={() => void removeAdminPassword()}>
                        Remove password
                      </button>
                    </div>
                    <p className="muted">Remove password turns protection off so Jotdex opens without asking.</p>
                  </>
                )}
              </div>
            )}
            {secHint && <p className={/saved|updated|removed|Unlocked|Authenticator|enabled|disabled/i.test(secHint) ? 'muted' : 'warn'}>{secHint}</p>}

            {auth?.setupComplete && auth.authenticated && (
              <>
                <h3 className="settings-subhead">Authenticator (TOTP)</h3>
                <p className="muted">
                  Optional second factor after your password (Google Authenticator, Microsoft Authenticator, etc.).
                </p>
                {auth.totpEnabled ? (
                  <>
                    <p className="muted">Authenticator is on.</p>
                    <label className="field">
                      Password (to disable)
                      <input
                        type="password"
                        value={secCurrentPassword}
                        onChange={(e) => setSecCurrentPassword(e.target.value)}
                        autoComplete="current-password"
                      />
                    </label>
                    <label className="field">
                      Current authenticator or recovery code
                      <input
                        type="text"
                        value={secTotpCode}
                        onChange={(e) => setSecTotpCode(e.target.value)}
                        autoComplete="one-time-code"
                      />
                    </label>
                    <div className="modal-actions">
                      <button
                        type="button"
                        className="ghost"
                        disabled={secBusy}
                        onClick={() => {
                          void (async () => {
                            setSecBusy(true)
                            setSecHint(null)
                            try {
                              const res = await fetch('/api/auth/totp/disable', {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                credentials: 'same-origin',
                                body: JSON.stringify({ password: secCurrentPassword, totpCode: secTotpCode }),
                              })
                              const data = await res.json()
                              if (!res.ok || !data.success) {
                                setSecHint(data.error ?? 'Could not disable authenticator')
                                return
                              }
                              setSecCurrentPassword('')
                              setSecTotpCode('')
                              setTotpRecoveryCodes(null)
                              setSecHint('Authenticator disabled.')
                              await loadAuth()
                            } finally {
                              setSecBusy(false)
                            }
                          })()
                        }}
                      >
                        Disable authenticator
                      </button>
                    </div>
                  </>
                ) : (
                  <>
                    <div className="modal-actions">
                      <button
                        type="button"
                        className="primary"
                        disabled={secBusy}
                        onClick={() => {
                          void (async () => {
                            setSecBusy(true)
                            setSecHint(null)
                            setTotpRecoveryCodes(null)
                            try {
                              const res = await fetch('/api/auth/totp/begin', {
                                method: 'POST',
                                credentials: 'same-origin',
                              })
                              const data = await res.json()
                              if (!res.ok || !data.success) {
                                setSecHint(data.error ?? 'Could not start authenticator setup')
                                return
                              }
                              setTotpManualKey(data.manualKey ?? null)
                              setTotpUri(data.otpAuthUri ?? null)
                              setSecHint('Scan the otpauth URI in your app (or enter the manual key), then confirm with a code.')
                            } finally {
                              setSecBusy(false)
                            }
                          })()
                        }}
                      >
                        Set up authenticator
                      </button>
                    </div>
                    {totpManualKey && (
                      <>
                        <p className="muted">
                          Manual key: <code>{totpManualKey}</code>
                        </p>
                        {totpUri && (
                          <p className="muted" style={{ wordBreak: 'break-all' }}>
                            URI: <code>{totpUri}</code>
                          </p>
                        )}
                        <label className="field">
                          Code from authenticator
                          <input
                            type="text"
                            inputMode="numeric"
                            value={totpConfirmCode}
                            onChange={(e) => setTotpConfirmCode(e.target.value)}
                            autoComplete="one-time-code"
                          />
                        </label>
                        <div className="modal-actions">
                          <button
                            type="button"
                            className="primary"
                            disabled={secBusy}
                            onClick={() => {
                              void (async () => {
                                setSecBusy(true)
                                setSecHint(null)
                                try {
                                  const res = await fetch('/api/auth/totp/confirm', {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    credentials: 'same-origin',
                                    body: JSON.stringify({ code: totpConfirmCode }),
                                  })
                                  const data = await res.json()
                                  if (!res.ok || !data.success) {
                                    setSecHint(data.error ?? 'Could not confirm authenticator')
                                    return
                                  }
                                  setTotpManualKey(null)
                                  setTotpUri(null)
                                  setTotpConfirmCode('')
                                  setTotpRecoveryCodes(data.recoveryCodes ?? null)
                                  setSecHint('Authenticator enabled. Save the recovery codes below — shown once.')
                                  await loadAuth()
                                } finally {
                                  setSecBusy(false)
                                }
                              })()
                            }}
                          >
                            Confirm & enable
                          </button>
                        </div>
                      </>
                    )}
                  </>
                )}
                {totpRecoveryCodes && totpRecoveryCodes.length > 0 && (
                  <pre className="maint-report">{totpRecoveryCodes.join('\n')}</pre>
                )}
              </>
            )}

            <h3 className="settings-subhead">Idle lock</h3>
            {!auth?.setupComplete ? (
              <p className="muted">Set a password above first. Until then, idle lock stays off.</p>
            ) : (
              <>
                <p className="muted">
                  Clicks, typing, scrolling, and touch reset the timer. After that many minutes with no interaction —
                  or with this tab hidden — require your password again. Stored on the Jotdex server so every device
                  uses the same setting.
                </p>
                <label className="field checkbox-row">
                  <input
                    type="checkbox"
                    checked={idleLockEnabled}
                    onChange={(e) => {
                      const on = e.target.checked
                      setIdleLockEnabled(on)
                      void saveUiPrefs({ idleLockEnabled: on, idleLockMinutes })
                    }}
                  />
                  Lock after idle
                </label>
                <label className="field">
                  Minutes until lock
                  <input
                    type="number"
                    min={1}
                    max={240}
                    value={idleLockMinutes}
                    disabled={!idleLockEnabled}
                    onChange={(e) => {
                      const n = Number(e.target.value) || 15
                      const clamped = Math.max(1, Math.min(240, Math.floor(n)))
                      setIdleLockMinutes(clamped)
                      void saveUiPrefs({ idleLockEnabled, idleLockMinutes: clamped })
                    }}
                  />
                </label>
              </>
            )}
              </>
            )}

            {settingsTab === 'capture' && (
              <>
                <h2 className="settings-section settings-section-first">Save pages from the web</h2>
                <p className="lede">Two ways to get a link into Jotdex — pick whichever fits the moment.</p>

                <h3 className="settings-subhead">1. Bookmark on your bookmarks bar (one-time setup)</h3>
                <p className="muted">
                  After you install it once, everyday use is: open any website, click that bookmark, and Jotdex opens
                  with the page title, URL, and any selected text. The Copy button is only for installing the bookmark —
                  you do not paste URLs into Settings each time you clip.
                </p>
                <ol className="capture-steps settings-capture-steps">
                  <li>Stay signed in to Jotdex in this browser.</li>
                  <li>
                    Click <strong>Copy bookmarklet</strong>, then Bookmarks → Add bookmark → paste into the URL field
                    and name it “Save to Jotdex”. (Chrome often blocks dragging <code>javascript:</code> links.)
                  </li>
                  <li>
                    Later, on any webpage, click <strong>Save to Jotdex</strong> on the bookmarks bar. Allow the pop-up
                    once if asked.
                  </li>
                  <li>Pick a folder if you want, then Save.</li>
                </ol>
                <label className="field">
                  Default folder for clips
                  <select
                    value={clipDefaultFolder}
                    onChange={(e) => {
                      const v = e.target.value
                      setClipDefaultFolder(v)
                      void saveUiPrefs({ clipDefaultFolder: v || 'Inbox' })
                    }}
                    onFocus={() => {
                      void fetch('/api/tree', { credentials: 'same-origin' })
                        .then((r) => r.json())
                        .then((tree: { relativePath?: string; children?: unknown[] }) => {
                          const acc: { path: string; label: string }[] = []
                          const walk = (n: { relativePath?: string; children?: unknown[] }) => {
                            const p = (n.relativePath || '').replace(/\\/g, '/')
                            if (p) acc.push({ path: p, label: p })
                            for (const c of (n.children as typeof n[]) || []) walk(c)
                          }
                          walk(tree)
                          if (!acc.some((f) => f.path.toLowerCase() === 'inbox')) {
                            acc.unshift({ path: 'Inbox', label: 'Inbox' })
                          }
                          acc.sort((a, b) => a.label.localeCompare(b.label))
                          setClipFolderOptions(acc)
                        })
                        .catch(() => {
                          /* keep Inbox */
                        })
                    }}
                  >
                    <option value="">Vault root</option>
                    {clipFolderOptions.map((f) => (
                      <option key={f.path} value={f.path}>
                        {f.label}
                      </option>
                    ))}
                  </select>
                </label>
                <div className="modal-actions">
                  <button
                    type="button"
                    className="primary"
                    onClick={() => {
                      void navigator.clipboard.writeText(buildClipBookmarklet(window.location.origin)).then(
                        () => {
                          setClipCopied(true)
                          window.setTimeout(() => setClipCopied(false), 2000)
                        },
                        () => setError('Could not copy bookmarklet'),
                      )
                    }}
                  >
                    {clipCopied ? 'Copied — paste into a new bookmark URL' : 'Copy bookmarklet (install once)'}
                  </button>
                </div>
                <label className="field">
                  Bookmarklet URL (install only)
                  <textarea
                    readOnly
                    rows={3}
                    value={buildClipBookmarklet(typeof window !== 'undefined' ? window.location.origin : '')}
                    onFocus={(e) => e.currentTarget.select()}
                  />
                </label>

                <h3 className="settings-subhead">2. From inside Jotdex</h3>
                <p className="muted">
                  <strong>New note ▾ → Clip page…</strong> fetches a page’s title and summary into a new note. On an open
                  note, use <strong>Clip page</strong> to pull the same into that note.
                </p>
                <p className="muted">
                  Tip: if the bookmark does nothing, allow pop-ups for your Jotdex address and confirm you are still
                  signed in. Re-copy the bookmarklet after changing Jotdex’s address or port.
                </p>
              </>
            )}

            {settingsTab === 'notifications' && (
              <>
            <h2 className="settings-section settings-section-first">Todo notifications</h2>
            <p className="lede">
              Reminders use the browser’s notification permission (works best in Chrome / Edge while a Jotdex tab is
              open). Safari support varies. Chrome is also prompted automatically when you add your first to-do. Use this
              button to ask again after switching browsers or if you skipped/blocked earlier — Jotdex cannot override a
              blocked permission; use the browser’s site settings if needed.
            </p>
            <p className="muted">
              Status:{' '}
              {notifyPerm === 'unsupported'
                ? 'Not available in this browser'
                : notifyPerm === 'granted'
                  ? 'Allowed'
                  : notifyPerm === 'denied'
                    ? 'Blocked'
                    : 'Not decided yet'}
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="primary"
                disabled={notifyPerm === 'unsupported' || notifyPerm === 'granted'}
                onClick={() => {
                  void (async () => {
                    setNotifyHint(null)
                    const p = await promptTodoNotifications({ force: true })
                    setNotifyPerm(p)
                    if (p === 'granted') setNotifyHint('Notifications allowed for this site.')
                    else if (p === 'denied')
                      setNotifyHint(
                        'Blocked — in Chrome: padlock / tune icon beside the URL → Site settings → Notifications → Allow.',
                      )
                    else setNotifyHint('No change — try again, or check the browser prompt.')
                  })()
                }}
              >
                {notifyPerm === 'granted' ? 'Notifications on' : 'Allow notifications'}
              </button>
            </div>
            {notifyHint && <p className="muted">{notifyHint}</p>}

            <h2 className="settings-section">Ops alerts (email / Telegram)</h2>
            <p className="lede">
              Optional alerts when the vault mirror goes stale. Passwords and bot tokens are stored with Windows DPAPI on
              this PC (and travel in the move kit as a portable secrets file).
            </p>
            <h3 className="settings-subhead">Email (SMTP)</h3>
            <label className="field checkbox-row">
              <input type="checkbox" checked={smtpEnabled} onChange={(e) => setSmtpEnabled(e.target.checked)} />
              Enable email alerts
            </label>
            <label className="field">
              SMTP host
              <input value={smtpHost} onChange={(e) => setSmtpHost(e.target.value)} placeholder="smtp.example.com" />
            </label>
            <label className="field">
              Port
              <input
                type="number"
                min={1}
                max={65535}
                value={smtpPort}
                onChange={(e) => setSmtpPort(Number(e.target.value) || 587)}
              />
            </label>
            <label className="field checkbox-row">
              <input type="checkbox" checked={smtpSsl} onChange={(e) => setSmtpSsl(e.target.checked)} />
              Use SSL/TLS
            </label>
            <label className="field">
              Username
              <input value={smtpUser} onChange={(e) => setSmtpUser(e.target.value)} autoComplete="off" />
            </label>
            <label className="field">
              Password {smtpPasswordSet ? '(saved — leave blank to keep)' : ''}
              <input
                type="password"
                value={smtpPassword}
                onChange={(e) => setSmtpPassword(e.target.value)}
                autoComplete="new-password"
                placeholder={smtpPasswordSet ? '••••••••' : ''}
              />
            </label>
            <label className="field">
              From address
              <input value={smtpFrom} onChange={(e) => setSmtpFrom(e.target.value)} placeholder="jotdex@example.com" />
            </label>
            <label className="field">
              To address
              <input value={smtpTo} onChange={(e) => setSmtpTo(e.target.value)} placeholder="you@example.com" />
            </label>

            <h3 className="settings-subhead">Telegram</h3>
            <label className="field checkbox-row">
              <input type="checkbox" checked={tgEnabled} onChange={(e) => setTgEnabled(e.target.checked)} />
              Enable Telegram alerts
            </label>
            <label className="field">
              Chat id
              <input value={tgChatId} onChange={(e) => setTgChatId(e.target.value)} placeholder="123456789" />
            </label>
            <label className="field">
              Bot token {tgTokenSet ? '(saved — leave blank to keep)' : ''}
              <input
                type="password"
                value={tgToken}
                onChange={(e) => setTgToken(e.target.value)}
                autoComplete="new-password"
                placeholder={tgTokenSet ? '••••••••' : ''}
              />
            </label>

            <h3 className="settings-subhead">Mirror stale alert</h3>
            <label className="field checkbox-row">
              <input type="checkbox" checked={mirrorStaleAlert} onChange={(e) => setMirrorStaleAlert(e.target.checked)} />
              Alert if mirror has not succeeded
            </label>
            <label className="field">
              Hours without success
              <input
                type="number"
                min={1}
                max={720}
                value={mirrorStaleHours}
                onChange={(e) => setMirrorStaleHours(Number(e.target.value) || 24)}
              />
            </label>
            <div className="modal-actions">
              <button
                type="button"
                className="primary"
                onClick={() => {
                  void (async () => {
                    setOpsNotifyHint(null)
                    try {
                      const res = await fetch('/api/settings/notifications', {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        credentials: 'same-origin',
                        body: JSON.stringify({
                          smtp: {
                            enabled: smtpEnabled,
                            host: smtpHost,
                            port: smtpPort,
                            useSsl: smtpSsl,
                            username: smtpUser,
                            fromAddress: smtpFrom,
                            fromDisplayName: 'Jotdex',
                            toAddress: smtpTo,
                          },
                          telegram: { enabled: tgEnabled, chatId: tgChatId },
                          alerts: { mirrorStaleEnabled: mirrorStaleAlert, mirrorStaleHours },
                          smtpPassword: smtpPassword.trim() || undefined,
                          telegramBotToken: tgToken.trim() || undefined,
                        }),
                      })
                      const data = await res.json()
                      if (!res.ok || !data.success) {
                        setOpsNotifyHint(data.error ?? 'Could not save notification settings')
                        return
                      }
                      setOpsNotifyHint('Ops notification settings saved.')
                      await loadOpsNotifications()
                    } catch (e) {
                      setOpsNotifyHint(e instanceof Error ? e.message : 'Save failed')
                    }
                  })()
                }}
              >
                Save ops alerts
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  void (async () => {
                    setOpsNotifyHint(null)
                    try {
                      const res = await fetch('/api/settings/notifications/test', {
                        method: 'POST',
                        credentials: 'same-origin',
                      })
                      const data = await res.json()
                      if (!res.ok || !data.success) {
                        setOpsNotifyHint(data.error ?? 'Test send failed')
                        return
                      }
                      setOpsNotifyHint(data.warning ? `Test sent (partial): ${data.warning}` : 'Test message sent.')
                    } catch (e) {
                      setOpsNotifyHint(e instanceof Error ? e.message : 'Test send failed')
                    }
                  })()
                }}
              >
                Send test
              </button>
            </div>
            {opsNotifyHint && <p className="muted">{opsNotifyHint}</p>}
              </>
            )}

            {settingsTab === 'backup' && (
              <>
            <h2 className="settings-section settings-section-first">Move to another PC</h2>
            <p className="lede">
              Create a recovery archive with your vault, settings/history, and (when portable) the Jotdex program. If you
              have an unlock password, it is saved as an encrypted <code>.jotdexkit</code> — safer in cloud folders.
            </p>
            <p className="muted">
              Restore stays simple: run <code>Restore-Jotdex.ps1</code> on the kit file; enter your password if asked;
              choose folders. Prefer a local-disk vault on the new PC.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="primary"
                onClick={() => {
                  void (async () => {
                    setNetworkHint('Creating move kit (may take a while for large vaults)…')
                    try {
                      let password: string | undefined
                      if (auth?.setupComplete) {
                        const entered = window.prompt(
                          'Enter your Jotdex unlock password to encrypt the move kit (required the first time encryption is set up; leave blank if already initialized):',
                        )
                        if (entered === null) {
                          setNetworkHint(null)
                          return
                        }
                        password = entered || undefined
                      }
                      const data = await fetch('/api/admin/move-kit', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        credentials: 'same-origin',
                        body: JSON.stringify({ password }),
                      }).then((r) => r.json())
                      if (!data.success) {
                        setError(data.error ?? 'Move kit failed')
                        setNetworkHint(null)
                        return
                      }
                      const mb = (data.bytes / (1024 * 1024)).toFixed(1)
                      const encNote = data.encrypted
                        ? 'Encrypted (.jotdexkit). Run Restore-Jotdex.ps1 — it will ask for your unlock password.'
                        : data.hint ?? 'Plain ZIP — treat as secret. Run Restore-Jotdex.ps1.'
                      setNetworkHint(`Move kit OK (${mb} MB): ${data.bundlePath} — ${encNote}`)
                      window.alert(`Move kit ready (${mb} MB):\n${data.bundlePath}\n\n${encNote}`)
                    } catch (e) {
                      setError(e instanceof Error ? e.message : 'Move kit failed')
                      setNetworkHint(null)
                    }
                  })()
                }}
              >
                Create move kit
              </button>
            </div>

            <h2 className="settings-section">Backup ZIP</h2>
            <p className="lede">
              Archive vault notes plus optional app data (auth, history) into a ZIP — no program files. Good for a
              periodic snapshot on this PC.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="primary"
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
            </div>

            <CloudBackupSettings
              onHint={(m) => setNetworkHint(m)}
              onError={(m) => {
                if (m) setError(m)
              }}
            />

            <div className="settings-backup-mirror">
            <h2 className="settings-section">Vault mirror</h2>
            <p className="lede">
              One-way filesystem copy of the live vault to another path — a second local folder, a USB drive, or a UNC
              share. This is not the API cloud backups above. Keep the live vault on local disk; never open the mirror
              folder as the vault.
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
                placeholder="D:\JotdexMirror or \\server\share\JotdexMirror"
              />
            </label>
            <p className="muted">
              Use a distinct folder name (for example <code>JotdexMirror</code>), not the live vault path. Copy is
              one-way: live vault → destination.
            </p>
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
            <label className="field checkbox-row">
              <input
                type="checkbox"
                checked={mirrorDailyMoveKit}
                onChange={(e) => setMirrorDailyMoveKit(e.target.checked)}
              />
              Also drop a daily recovery move kit into that mirror folder
            </label>
            <p className="muted">
              Extra safety net: still mirrors the full vault, and adds <code>jotdex-move-kits\</code> with one archive
              (encrypted when a password is set) plus <code>Restore-Jotdex.ps1</code>.
            </p>
            <div className="modal-actions">
              <button type="button" className="primary" onClick={() => void saveMirrorSettings()}>
                Save mirror
              </button>
              <button type="button" className="ghost" onClick={() => void runMirrorNow()}>
                Mirror now
              </button>
            </div>
            {mirrorStatus && <p className="muted">{mirrorStatus}</p>}
            </div>
              </>
            )}

            {settingsTab === 'updates' && (
              <>
            <h2 className="settings-section settings-section-first">Updates</h2>
            <p className="lede">
              Jotdex checks GitHub Releases for a newer portable build. Updating replaces the program files only — your
              vault and <code>data\</code> folder stay put. The updater backs up the current program to{' '}
              <code>C:\JotdexBackupHold</code> first and can roll back if something looks wrong.
            </p>
            <p className="muted">
              Current version will appear after you check. Use the portable install folder (not a raw <code>dotnet run</code>{' '}
              debug build) when updating.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="primary"
                disabled={updateBusy}
                onClick={() => {
                  void (async () => {
                    setUpdateBusy(true)
                    setUpdateInfo(null)
                    try {
                      const data = await fetch('/api/updates/check', { credentials: 'same-origin' }).then((r) =>
                        r.json(),
                      )
                      setUpdateInfo(data)
                    } catch (e) {
                      setUpdateInfo({
                        success: false,
                        error: e instanceof Error ? e.message : 'Update check failed',
                      })
                    } finally {
                      setUpdateBusy(false)
                    }
                  })()
                }}
              >
                {updateBusy ? 'Checking…' : 'Check for updates'}
              </button>
              {updateInfo?.htmlUrl && (
                <a className="ghost settings-link-btn" href={updateInfo.htmlUrl} target="_blank" rel="noreferrer">
                  Open Releases
                </a>
              )}
            </div>
            {updateInfo && (
              <div className="update-status">
                {updateInfo.currentVersion && (
                  <p className="muted">Running: {updateInfo.currentVersion}</p>
                )}
                {updateInfo.latestTag && <p className="muted">Latest release: {updateInfo.latestTag}</p>}
                {updateInfo.updateAvailable ? (
                  <p className="warn">An update is available{updateInfo.downloadName ? ` (${updateInfo.downloadName})` : ''}.</p>
                ) : (
                  updateInfo.success && <p className="muted">No newer release found (or no zip asset on the latest release yet).</p>
                )}
                {updateInfo.notes && <p className="muted">{updateInfo.notes}</p>}
                {updateInfo.error && <p className="warn">{updateInfo.error}</p>}
                {updateInfo.installPath && (
                  <p className="muted">
                    Install folder: <code>{updateInfo.installPath}</code>
                  </p>
                )}
              </div>
            )}
            <h3 className="settings-subhead">How to run the updater</h3>
            <ol className="settings-steps">
              <li>
                Open File Explorer to your Jotdex install folder (where <code>Jotdex.Server.exe</code> and{' '}
                <code>Update-Jotdex.ps1</code> live).
              </li>
              <li>
                Right-click <code>Update-Jotdex.ps1</code> → <strong>Run with PowerShell</strong>
                <br />
                <span className="muted">
                  Or: <code>powershell -NoProfile -ExecutionPolicy Bypass -File .\Update-Jotdex.ps1</code>
                </span>
              </li>
              <li>
                Wait while it backs up to <code>C:\JotdexBackupHold</code>, downloads the release, and restarts Jotdex.
              </li>
              <li>
                Spot-check the app in your browser. Answer Yes if it looks good, or No to restore the backup
                automatically.
              </li>
            </ol>
            <p className="muted">
              Tip for publishing updates: run <code>scripts\publish-win-x64.ps1</code>, then upload{' '}
              <code>artifacts\jotdex-win-x64.zip</code> as a GitHub Release asset. See{' '}
              <code>docs\upgrading.md</code>.
            </p>
              </>
            )}

            {settingsTab === 'advanced' && (
              <>
            <h2 className="settings-section settings-section-first">Start with Windows</h2>
            <p className="lede">
              After a reboot, Jotdex should come back by itself. Easiest: enable a Startup shortcut for your Windows user.
              For a always-on PC, prefer the Windows Service (run <code>install-service.ps1</code> as Administrator once).
            </p>
            <div className="modal-actions">
              <button type="button" className="ghost" onClick={() => void loadAutostart()}>
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
              Rescan reloads the note list from disk (use after adding/editing .md files outside Jotdex). Reindex rebuilds
              search only. Also: diagnostics, integrity, trash, and full-vault static HTML export. Backups live under the
              Backup tab. To share one note, open it and use Share HTML.
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="ghost"
                title="Reload folders and notes from the vault folder on disk"
                onClick={() => {
                  void (async () => {
                    setNetworkHint('Rescanning vault…')
                    const res = await fetch('/api/admin/rescan', { method: 'POST' })
                    if (!res.ok) {
                      setError('Rescan failed')
                      setNetworkHint(null)
                      return
                    }
                    await loadVault()
                    setNetworkHint('Vault rescanned — note list refreshed from disk.')
                  })()
                }}
              >
                Rescan vault
              </button>
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
              </>
            )}
            </div>
            {networkHint && <p className="muted settings-footer-hint">{networkHint}</p>}
          </div>
        </div>
      )}

      <div
        className={[
          'body',
          `mobile-${mobilePane}`,
          !narrowLayout && foldersCollapsed ? 'folders-collapsed' : '',
          !narrowLayout && notesCollapsed && !showTrash && !showSnippets ? 'notes-collapsed' : '',
          !narrowLayout && todosCollapsed ? 'todos-collapsed' : '',
        ]
          .filter(Boolean)
          .join(' ')}
      >
        {foldersCollapsed && !narrowLayout ? (
          <aside className="pane left pane-rail-collapsed rail-collapsed-stack">
            <div
              className="rail-collapsed-meta"
              title={folder ? folder.replace(/\\/g, '/') : 'All notes'}
            >
              {folderRailShortLabel(folder)}
            </div>
            <button
              type="button"
              className="pane-collapsed-tab"
              title="Show folders"
              onClick={() => {
                setFoldersCollapsed(false)
                try {
                  localStorage.setItem('jotdex.foldersCollapsed', '0')
                } catch {
                  /* ignore */
                }
              }}
            >
              Folders
            </button>
          </aside>
        ) : (
          <aside
            className="pane left"
            onDragEnd={() => {
              railDragRef.current = null
              setDropTargetPath(null)
              if (folderExpandTimer.current) {
                window.clearTimeout(folderExpandTimer.current)
                folderExpandTimer.current = null
              }
            }}
          >
            <div className="pane-rail-head desktop-only-rail">
              <span className="pane-rail-label">Folders</span>
              <button
                type="button"
                className="ghost pane-collapse-btn"
                title="Collapse folders"
                onClick={() => {
                  setFoldersCollapsed(true)
                  try {
                    localStorage.setItem('jotdex.foldersCollapsed', '1')
                  } catch {
                    /* ignore */
                  }
                }}
              >
                ⟨
              </button>
            </div>
            <div className="mobile-pane-title">Folders</div>
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
                Trash folder
              </button>
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  setShowTrash(true)
                  setShowSnippets(false)
                  setNotesCollapsed(false)
                  setMobilePane('trash')
                }}
              >
                Open trash
              </button>
            </div>
            {tree && (
              <FolderTree
                node={tree}
                depth={0}
                selected={folder}
                collapsed={collapsedFolders}
                onToggle={toggleFolderCollapsed}
                dropTargetPath={dropTargetPath}
                onFolderDragStart={(path, e) => {
                  railDragRef.current = { kind: 'folder', path }
                  e.dataTransfer.setData('text/plain', path)
                  e.dataTransfer.effectAllowed = 'move'
                }}
                onFolderDragOver={(path, e) => {
                  const drag = railDragRef.current
                  if (!drag) return
                  if (drag.kind === 'folder' && (path === drag.path || path.startsWith(drag.path + '/'))) return
                  e.preventDefault()
                  e.dataTransfer.dropEffect = 'move'
                  setDropTargetPath(path)
                  if (folderExpandTimer.current) window.clearTimeout(folderExpandTimer.current)
                  if (path && collapsedFolders.has(path)) {
                    folderExpandTimer.current = window.setTimeout(() => {
                      toggleFolderCollapsed(path)
                    }, 450)
                  }
                }}
                onFolderDrop={(path, e) => {
                  e.preventDefault()
                  e.stopPropagation()
                  setDropTargetPath(null)
                  if (folderExpandTimer.current) {
                    window.clearTimeout(folderExpandTimer.current)
                    folderExpandTimer.current = null
                  }
                  const drag = railDragRef.current
                  railDragRef.current = null
                  if (!drag) return
                  if (drag.kind === 'note') {
                    void applyMoveNote(drag.id, drag.title, path, drag.folderPath).catch(() => {
                      /* error already shown */
                    })
                    return
                  }
                  void applyMoveFolder(drag.path, path).catch(() => {
                    /* error already shown */
                  })
                }}
                onSelect={(p) => {
                  setFolder(p)
                  setSelectedId(null)
                  setShowTrash(false)
                  setShowSnippets(false)
                  setMobilePane('notes')
                }}
              />
            )}
            <div className="folders-rail-snippet">
              <button
                type="button"
                className={`snippets-rail-btn${showSnippets || mobilePane === 'snippets' ? ' active' : ''}`}
                title="View and edit saved code snippets"
                onClick={() => {
                  setShowSnippets(true)
                  setShowTrash(false)
                  setNotesCollapsed(false)
                  try {
                    localStorage.setItem('jotdex.notesCollapsed', '0')
                  } catch {
                    /* ignore */
                  }
                  setMobilePane('snippets')
                }}
              >
                <span className="snippets-rail-icon" aria-hidden>
                  {'</>'}
                </span>
                Snippets
              </button>
            </div>
          </aside>
        )}

        {showTrash || mobilePane === 'trash' ? (
          <TrashPane
            fill={narrowLayout && mobilePane === 'trash'}
            onRestored={() => {
              void loadVault()
              bumpTasksRefresh()
            }}
            onCollapse={() => {
              setShowTrash(false)
              setMobilePane('notes')
            }}
          />
        ) : showSnippets || mobilePane === 'snippets' ? (
          <SnippetsPane
            fill={narrowLayout && mobilePane === 'snippets'}
            activeSnippetId={selectedId}
            onChanged={() => void loadVault()}
            onOpenSnippet={(s) => {
              setSelectedId(s.noteId)
              setMobilePane('editor')
            }}
            onCollapse={() => {
              setShowSnippets(false)
              setMobilePane('notes')
            }}
          />
        ) : notesCollapsed && !narrowLayout ? (
          <section className="pane middle pane-rail-collapsed rail-collapsed-stack">
            <button
              type="button"
              className="rail-collapsed-action"
              title="Add a note (choose folder)"
              onClick={() => setNewNoteModalOpen(true)}
            >
              Add note
            </button>
            <button
              type="button"
              className="pane-collapsed-tab"
              title="Show notes"
              onClick={() => {
                setNotesCollapsed(false)
                try {
                  localStorage.setItem('jotdex.notesCollapsed', '0')
                } catch {
                  /* ignore */
                }
              }}
            >
              Notes
            </button>
          </section>
        ) : (
          <section className="pane middle">
            <div className="pane-head">
              <div className="pane-rail-head pane-rail-head-inline">
                <h2>{folder || 'All notes'}</h2>
                <button
                  type="button"
                  className="ghost pane-collapse-btn desktop-only-rail"
                  title="Collapse notes list"
                  onClick={() => {
                    setNotesCollapsed(true)
                    try {
                      localStorage.setItem('jotdex.notesCollapsed', '1')
                    } catch {
                      /* ignore */
                    }
                  }}
                >
                  ⟨
                </button>
              </div>
              <div className="pane-tools">
                <button
                  type="button"
                  className="ghost"
                  title="Show trash"
                  onClick={() => {
                    setShowTrash(true)
                    setShowSnippets(false)
                    setMobilePane('trash')
                  }}
                >
                  Trash
                </button>
                <div className="template-wrap">
                  <button
                    type="button"
                    className="ghost"
                    title="Create a note (choose folder)"
                    onClick={() => setNewNoteModalOpen(true)}
                  >
                    New note
                  </button>
                  <button
                    ref={templateBtnRef}
                    type="button"
                    className={`ghost${templateMenu ? ' on' : ''}`}
                    title="New note from template"
                    aria-expanded={templateMenu}
                    aria-haspopup="menu"
                    onClick={() => setTemplateMenu((o) => !o)}
                  >
                    ▾
                  </button>
                  {templateMenu &&
                    templateMenuPos &&
                    createPortal(
                      <div
                        className="template-menu"
                        role="menu"
                        style={{ top: templateMenuPos.top, left: templateMenuPos.left }}
                      >
                        {NOTE_TEMPLATES.map((t) => (
                          <button
                            key={t.id}
                            type="button"
                            role="menuitem"
                            onClick={() => void createNote(t)}
                          >
                            <strong>{t.name}</strong>
                            <span>{t.description}</span>
                          </button>
                        ))}
                        <button type="button" role="menuitem" onClick={() => void createNoteFromUrl()}>
                          <strong>Clip page…</strong>
                          <span>Fetch a page’s title and summary as a new note</span>
                        </button>
                      </div>,
                      document.body,
                    )}
                </div>
              </div>
            </div>
            <ul
              className="note-list"
              onDragEnd={() => {
                railDragRef.current = null
                setDropTargetPath(null)
                if (folderExpandTimer.current) {
                  window.clearTimeout(folderExpandTimer.current)
                  folderExpandTimer.current = null
                }
              }}
            >
              {notes.map((n) => (
                <li key={n.id}>
                  <button
                    type="button"
                    className={selectedId === n.id ? 'active' : ''}
                    draggable
                    onDragStart={(e) => {
                      railDragRef.current = {
                        kind: 'note',
                        id: n.id,
                        title: n.title,
                        folderPath: n.folderPath,
                      }
                      e.dataTransfer.setData('text/plain', n.title)
                      e.dataTransfer.effectAllowed = 'move'
                    }}
                    onClick={() => {
                      setSelectedId(n.id)
                      setMobilePane('editor')
                    }}
                  >
                    <span className="note-title">
                      {n.favorite ? <span className="fav-star" aria-hidden>★ </span> : null}
                      {n.title}
                    </span>
                    <span className="note-path">{n.folderPath || '/'}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        )}

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
                <button
                  type="button"
                  className="ghost"
                  onClick={() => {
                    if (!conflictDisk) return
                    const current = joinFrontMatter(frontMatterRef.current, draftRef.current)
                    setHistoryDiff({
                      snapshotId: 'conflict',
                      lines: diffLines(conflictDisk.markdown, current),
                    })
                    setHistoryOpen(true)
                  }}
                >
                  Compare
                </button>
                <button type="button" className="ghost" onClick={() => { setSaveStatus('editing'); setError(null) }}>
                  Keep editing
                </button>
              </div>
            </div>
          )}
          {!note && !selectedId && (
            <HomeLanding
              vaultName={vault?.name}
              noteCount={vault?.noteCount}
              folderCount={vault?.folderCount}
              onOpenNote={(id) => {
                setSelectedId(id)
                setMobilePane('editor')
              }}
              onNewNote={() => setNewNoteModalOpen(true)}
              onFocusSearch={() => searchRef.current?.focus()}
              onOpenTodos={() => {
                if (narrowLayout) {
                  setMobilePane('todos')
                  return
                }
                setTodosCollapsed(false)
                try {
                  localStorage.setItem('jotdex.todosCollapsed', '0')
                } catch {
                  /* ignore */
                }
              }}
              onOpenCloudBackupSettings={() => {
                setSettingsOpen(true)
                setSettingsTab('backup')
              }}
              onRetryCloudBackup={async () => {
                setNetworkHint('Retrying cloud backup…')
                const res = await runCloudBackup()
                if (!res.accepted) {
                  setError(res.error ?? 'Cloud backup retry failed')
                  setNetworkHint(null)
                  return
                }
                setNetworkHint('Cloud backup started.')
              }}
            />
          )}
          {!note && selectedId && <p className="muted">Loading note…</p>}
          {note && (
            <>
              <div className="note-head">
                <div className="note-head-main">
                  <button
                    type="button"
                    className="ghost mobile-only mobile-back"
                    onClick={() => setMobilePane(showSnippets || mobilePane === 'snippets' ? 'snippets' : 'notes')}
                  >
                    {showSnippets || mobilePane === 'snippets' ? '← Snippets' : '← Notes'}
                  </button>
                  {isCodeSnippetNote(frontMatter, note.folderPath) && (
                    <span className="snippet-note-badge" title="Reusable code snippet (stored in Snippets/)">
                      Snippet
                    </span>
                  )}
                  {titleEditing ? (
                    <input
                      className="note-title-edit"
                      value={titleDraft}
                      autoFocus
                      onChange={(e) => setTitleDraft(e.target.value)}
                      onFocus={(e) => e.currentTarget.select()}
                      onBlur={() => {
                        setTitleEditing(false)
                        void applyRename(titleDraft)
                      }}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault()
                          e.currentTarget.blur()
                        } else if (e.key === 'Escape') {
                          e.preventDefault()
                          setTitleDraft(note.title)
                          setTitleEditing(false)
                        }
                      }}
                    />
                  ) : (
                    <h1
                      className="note-title-clickable"
                      title="Click to rename"
                      onClick={() => {
                        setTitleDraft(note.title)
                        setTitleEditing(true)
                      }}
                    >
                      {note.title}
                    </h1>
                  )}
                  <p className="note-path">{note.relativePath}</p>
                </div>
                <div className="actions">
                  <span className={`save-chip ${saveStatus}`} title={saveChipLabel(saveStatus)}>
                    {saveChipLabel(saveStatus)}
                  </span>
                  <button
                    type="button"
                    className="ghost"
                    title={/favorite\s*:\s*(true|yes|1)/i.test(frontMatter) ? 'Remove favorite' : 'Favorite'}
                    onClick={() => void toggleFavorite()}
                  >
                    {/favorite\s*:\s*(true|yes|1)/i.test(frontMatter) ? '★ Favorited' : '☆ Favorite'}
                  </button>
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
                  <button
                    type="button"
                    className="ghost"
                    title="Fetch a page’s title and summary into this note"
                    onClick={() => void insertUrlIntoNote()}
                  >
                    Clip page
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => openPopout(note.id)}
                    title="Open this note in a small floating window for side-by-side editing"
                  >
                    Pop out
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
                    className={`ghost${outlineOpen ? ' on' : ''}`}
                    onClick={() => toggleOutline()}
                    title="Headings in this note"
                  >
                    Outline
                  </button>
                  {isNetworkDoc(draft) && (
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => addNetworkSite()}
                      title="Insert another site section with blank network tables"
                    >
                      Add site
                    </button>
                  )}
                  <button
                    type="button"
                    className={`ghost${backlinksOpen ? ' on' : ''}`}
                    onClick={() => void toggleBacklinks()}
                    title="Notes that link here"
                  >
                    Backlinks
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
                    className={`ghost${formatLintOpen ? ' on' : ''}`}
                    onClick={() => void toggleFormatLint()}
                    title="Check Markdown formatting (report only — does not change your note)"
                  >
                    {formatLintBusy ? 'Checking…' : 'Check formatting'}
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
                  noteRelativePath={note.relativePath}
                  noteCatalog={noteCatalog}
                  markdown={draft}
                  contentEpoch={editorEpoch}
                  jumpHeading={jumpHeading}
                  headingFolds={note.headingFolds}
                  onOutline={setLiveOutline}
                  attachments={note.attachments}
                  onChange={(md, rev) => {
                    draftRef.current = md
                    setDraft(md)
                    if (typeof rev === 'number') editorRevisionRef.current = rev
                  }}
                  onPastePending={(pending) => {
                    pastePendingRef.current = pending
                    if (pending) setSaveStatus('uploading')
                    else if (saveStatusRef.current === 'uploading') setSaveStatus('editing')
                  }}
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
              {outlineOpen && (
                <div className="history-panel">
                  <h3>Outline</h3>
                  {(() => {
                    const items = showSource
                      ? extractOutline(draft).map((item, i) => ({
                          ...item,
                          pos: undefined as number | undefined,
                          slug: item.text,
                          i,
                        }))
                      : liveOutline.map((item, i) => ({ ...item, i }))
                    if (items.length === 0) {
                      return <p className="muted">No headings in this note yet. Use H1–H3 in the toolbar.</p>
                    }
                    return (
                      <ul className="outline-list">
                        {items.map((item) => (
                          <li
                            key={`${item.level}-${item.text}-${item.i}`}
                            style={{ paddingLeft: `${(item.level - 1) * 0.75}rem` }}
                          >
                            <button
                              type="button"
                              className="ghost outline-item"
                              onClick={() => {
                                setJumpHeading({ text: item.text, nonce: Date.now(), pos: item.pos })
                                if ('slug' in item && item.slug) {
                                  try {
                                    window.history.replaceState(null, '', `#${item.slug}`)
                                  } catch {
                                    /* ignore */
                                  }
                                }
                              }}
                            >
                              {item.text}
                            </button>
                            {!showSource && item.pos != null && (
                              <span className="outline-move">
                                <button
                                  type="button"
                                  className="ghost"
                                  title="Move section up"
                                  onClick={() =>
                                    window.dispatchEvent(
                                      new CustomEvent('jotdex-move-section', { detail: { pos: item.pos, dir: -1 } }),
                                    )
                                  }
                                >
                                  ↑
                                </button>
                                <button
                                  type="button"
                                  className="ghost"
                                  title="Move section down"
                                  onClick={() =>
                                    window.dispatchEvent(
                                      new CustomEvent('jotdex-move-section', { detail: { pos: item.pos, dir: 1 } }),
                                    )
                                  }
                                >
                                  ↓
                                </button>
                              </span>
                            )}
                          </li>
                        ))}
                      </ul>
                    )
                  })()}
                </div>
              )}
              {backlinksOpen && (
                <div className="history-panel">
                  <h3>Backlinks</h3>
                  {backlinks.length === 0 ? (
                    <p className="muted">No other notes link here yet. Link with [[Note title]] or a markdown link.</p>
                  ) : (
                    <ul>
                      {backlinks.map((b) => (
                        <li key={b.noteId}>
                          <button
                            type="button"
                            className="ghost backlink-item"
                            onClick={() => setSelectedId(b.noteId)}
                          >
                            <span className="note-title">{b.title}</span>
                            <span className="note-path">{b.folderPath || '/'}</span>
                            {b.context && <span className="history-preview">{b.context}</span>}
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
              {formatLintOpen && (
                <div className="history-panel">
                  <h3>Formatting check</h3>
                  <p className="muted">Report only — your note is not changed automatically.</p>
                  {formatLintIssues.length === 0 ? (
                    <p className="muted">No formatting issues reported for this note body.</p>
                  ) : (
                    <ul className="format-lint-list">
                      {formatLintIssues.map((issue, i) => (
                        <li key={`${issue.rule}-${issue.line}-${i}`}>
                          <span className="diag-sev">{issue.severity === 'error' ? 'Error' : 'Warning'}</span>
                          L{issue.line}:{issue.column} — {issue.message}
                          <span className="muted"> ({issue.rule})</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
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
                          <button type="button" className="ghost" onClick={() => void compareSnapshot(h.snapshotId)}>
                            Compare
                          </button>
                          <button type="button" className="ghost" onClick={() => void restoreSnapshot(h.snapshotId)}>
                            Restore
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                  {historyDiff && (
                    <div className="history-diff">
                      <div className="history-diff-head">
                        <strong>Compare to current</strong>
                        <button type="button" className="ghost" onClick={() => setHistoryDiff(null)}>
                          Close
                        </button>
                      </div>
                      <pre className="history-diff-body">
                        {historyDiff.lines.map((line, i) => (
                          <div key={i} className={`diff-line diff-${line.type}`}>
                            {line.type === 'add' ? '+ ' : line.type === 'del' ? '- ' : '  '}
                            {line.text}
                          </div>
                        ))}
                      </pre>
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </section>

        <TodosRail
          fill={mobilePane === 'todos'}
          collapsed={mobilePane === 'todos' ? false : todosCollapsed}
          refreshKey={tasksRefreshKey}
          onNoteTasksChanged={(noteId) => {
            if (selectedId !== noteId) return
            const pending = joinFrontMatter(frontMatterRef.current, draftRef.current)
            if (!sameMarkdown(pending, baselineRef.current)) return
            void fetch(`/api/notes/${noteId}`, { credentials: 'same-origin' })
              .then(async (r) => {
                if (!r.ok) return
                return r.json() as Promise<NoteDetail>
              })
              .then((n) => {
                if (!n) return
                setNote(n)
                const split = splitFrontMatter(n.markdown)
                setFrontMatter(split.frontMatter)
                setDraft(split.body)
                setEtag(n.etag)
                etagRef.current = n.etag
                draftRef.current = split.body
                frontMatterRef.current = split.frontMatter
                baselineRef.current = joinFrontMatter(split.frontMatter, split.body)
              })
              .catch(() => {
                /* ignore */
              })
          }}
          onOpenNote={(id) => {
            setSelectedId(id)
            setMobilePane('editor')
          }}
          onToggleCollapsed={() => {
            setTodosCollapsed((c) => {
              const next = !c
              try {
                localStorage.setItem('jotdex.todosCollapsed', next ? '1' : '0')
              } catch {
                /* ignore */
              }
              return next
            })
          }}
        />
      </div>

      <nav className="mobile-tabbar" aria-label="Mobile navigation">
        <button
          type="button"
          className={mobilePane === 'folders' ? 'on' : ''}
          onClick={() => setMobilePane('folders')}
        >
          Folders
        </button>
        <button
          type="button"
          className={mobilePane === 'notes' || mobilePane === 'trash' || mobilePane === 'snippets' ? 'on' : ''}
          onClick={() => {
            setShowTrash(false)
            setShowSnippets(false)
            setMobilePane('notes')
          }}
        >
          Notes
        </button>
        <button
          type="button"
          className={mobilePane === 'editor' ? 'on' : ''}
          onClick={() => setMobilePane('editor')}
        >
          {selectedId ? 'Note' : 'Home'}
        </button>
        <button
          type="button"
          className={mobilePane === 'todos' ? 'on' : ''}
          onClick={() => setMobilePane('todos')}
        >
          Todos
        </button>
      </nav>

      {quickOpen && (
        <div
          className="quick-open-backdrop"
          role="presentation"
          onClick={() => setQuickOpen(false)}
        >
          <div
            className="quick-open"
            role="dialog"
            aria-label="Quick open note"
            onClick={(e) => e.stopPropagation()}
          >
            <input
              ref={quickInputRef}
              className="quick-open-input"
              value={quickQuery}
              onChange={(e) => {
                setQuickQuery(e.target.value)
                setQuickIndex(0)
              }}
              placeholder="Jump to note… (Ctrl+O)"
              aria-label="Filter notes by title"
            />
            <ul className="quick-open-list" role="listbox">
              {filterQuick(noteCatalog, quickQuery)
                .slice(0, 40)
                .map((n, i) => (
                  <li key={n.id}>
                    <button
                      type="button"
                      role="option"
                      aria-selected={i === quickIndex}
                      className={i === quickIndex ? 'active' : ''}
                      onMouseEnter={() => setQuickIndex(i)}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => {
                        setSelectedId(n.id)
                        setMobilePane('editor')
                        setQuickOpen(false)
                      }}
                    >
                      <span className="note-title">{n.title}</span>
                      <span className="note-path">{n.folderPath || '/'}</span>
                    </button>
                  </li>
                ))}
              {filterQuick(noteCatalog, quickQuery).length === 0 && (
                <li className="empty">No notes match</li>
              )}
            </ul>
          </div>
        </div>
      )}

      <IdleLockGate
        enabled={idleLockEnabled}
        minutes={idleLockMinutes}
        authAvailable={!!auth?.setupComplete}
        totpEnabled={!!auth?.totpEnabled}
        onUnlocked={returnHomeAfterUnlock}
      />
    </div>
  )
}

function filterQuick(catalog: NoteCatalogItem[], query: string): NoteCatalogItem[] {
  const q = query.trim().toLowerCase()
  if (!q) return catalog
  return catalog.filter(
    (n) => n.title.toLowerCase().includes(q) || n.relativePath.toLowerCase().includes(q),
  )
}

export default App
