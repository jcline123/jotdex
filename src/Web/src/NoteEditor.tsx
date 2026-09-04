import { useEffect, useMemo, useRef, useState } from 'react'
import { EditorContent, useEditor } from '@tiptap/react'
import type { Editor } from '@tiptap/react'
import { normalizeBlockSelection } from './selectionUtils'
import { type CalloutType } from './callout'
import { type WikiSuggestState } from './wikiLinkSuggest'
import { relativeMdPath } from './paths'
import { cleanPasteHtml } from './pasteHtml'
import {
  copyPlainTextFromCodeBox,
  deleteDomSelection,
  htmlIsPlainClipboardSnippet,
  installCodeBoxClipboardGuards,
  plainTextFromClipboard,
} from './copyCodePlain'
import { pasteAsCodeBlock, pastePlainIntoCodeBlock, plainTextForCodeBoxPaste } from './pasteCodeBlock'
import { createEditorExtensions } from './editor/extensions/createEditorExtensions'
import { insertHtml, insertLiteralText, insertMarkdown, setMarkdownDocument } from './editor/operations/contentInsertion'
import { dispatchAttachmentInventory } from './editor/assets/AttachmentResolver'
import { imageFileFromClipboard } from './editor/paste/clipboardImage'
import { EditorRevisionCoordinator } from './editor/revisions/EditorRevisionCoordinator'
import { assertSetContentReason } from './editor/operations/operationMeta'
import { logEditorDiag } from './editor/diagnostics'
import { recordSetContent } from './editor/reloadStats'
import { planEditorReload } from './editor/reloadPolicy'
import type { NoteServerEvent } from './editor/events/noteServerEvents'
import {
  insertPendingAssetAtSelection,
  retryPendingUpload,
  runPasteSession,
  rewritePastedImagesToPlaceholders,
  type PasteUploadResult,
} from './editor/paste/PasteSessionManager'
import { createEditorCommandRegistry } from './editor/commands/createEditorCommandRegistry'
import { OverlayCoordinator } from './editor/menus/overlayCoordinator'
import { SlashMenu, slashKeydown } from './editor/slash/SlashMenu'
import { slashMenuKey, type SlashMenuState } from './editor/slash/slashMenuPlugin'
import type { GutterPlusState } from './editor/gaps/gutterPlusPlugin'
import { deleteTransientEmptyParagraph, insertTransientParagraphAt } from './editor/gaps/gutterPlusPlugin'
import { FormattingBubbleMenu } from './editor/menus/FormattingBubbleMenu'
import { LinkPopover } from './editor/links/LinkPopover'
import { isSafeHref, looksLikeBareUrl } from './editor/links/linkSchemes'
import { EmojiPicker } from './editor/emoji/EmojiPicker'
import { TableChrome } from './editor/tables/TableChrome'
import { extractLiveOutline, moveSection, type LiveOutlineItem } from './editor/outline/liveOutline'
import { persistHeadingFolds, restoreHeadingFolds, unfoldHeadingsContaining, foldsForNote, putHeadingFolds, clearLegacyBrowserFoldKeys } from './headingFold'


const TEXT_COLORS = [
  { label: 'Default', value: '' },
  { label: 'Red', value: '#b42318' },
  { label: 'Orange', value: '#b54708' },
  { label: 'Green', value: '#027a48' },
  { label: 'Blue', value: '#175cd3' },
  { label: 'Purple', value: '#6941c6' },
  { label: 'Gray', value: '#667085' },
]

const FONT_SIZES = [
  { label: 'Size', value: '' },
  { label: 'Small', value: '0.85em' },
  { label: 'Normal', value: '1em' },
  { label: 'Large', value: '1.25em' },
  { label: 'Larger', value: '1.5em' },
]

export type PasteMode = 'smart' | 'plain' | 'code' | 'keep' | 'preserve'

type AttachmentInfo = { id: string; fileName: string; contentType: string }

export type NoteCatalogItem = {
  id: string
  title: string
  relativePath: string
  folderPath: string
}

type Props = {
  noteId: string
  noteStem: string
  noteRelativePath: string
  noteCatalog?: NoteCatalogItem[]
  markdown: string
  /** Bumps when parent applies an external markdown reload (open note / conflict / preserve-page). */
  contentEpoch?: number
  jumpHeading?: { text: string; nonce: number; pos?: number } | null
  headingFolds?: string[]
  onChange: (markdown: string, revision?: number) => void
  attachments?: AttachmentInfo[]
  editable?: boolean
  onServerEvent?: (event: NoteServerEvent) => void
  onNoteMeta?: (note: { etag?: string; attachments?: AttachmentInfo[]; markdown?: string; htmlSidecars?: unknown }) => void
  onError?: (message: string) => void
  getEtag?: () => string
  onDirty?: () => void
  onPastePending?: (pending: boolean) => void
  onOutline?: (items: LiveOutlineItem[]) => void
  /** Test/host override for uploads. Production uses the live `/api/notes/...` endpoints. */
  assetTransport?: AssetTransport
}

type UploadResult = PasteUploadResult & { isImage?: boolean }

export type AssetTransport = {
  uploadFile: (noteId: string, file: File) => Promise<UploadResult>
  importRemote: (noteId: string, url: string) => Promise<PasteUploadResult>
}

function findInDoc(editor: Editor, term: string, startPos: number, reverse = false): { from: number; to: number } | null {
  const q = term.trim()
  if (!q) return null
  const lower = q.toLowerCase()
  const hits: { from: number; to: number }[] = []
  editor.state.doc.descendants((node, pos) => {
    if (!node.isText || !node.text) return
    const text = node.text
    const hay = text.toLowerCase()
    let idx = 0
    while (idx < hay.length) {
      const found = hay.indexOf(lower, idx)
      if (found < 0) break
      hits.push({ from: pos + found, to: pos + found + q.length })
      idx = found + Math.max(1, q.length)
    }
  })
  if (!hits.length) return null
  if (!reverse) {
    return hits.find((h) => h.from >= startPos) ?? hits[0]
  }
  const before = hits.filter((h) => h.from < startPos)
  return before.length ? before[before.length - 1] : hits[hits.length - 1]
}

async function uploadFile(noteId: string, file: File): Promise<UploadResult> {
  const form = new FormData()
  form.append('file', file, file.name || 'image.png')
  const res = await fetch(`/api/notes/${noteId}/attachments`, { method: 'POST', body: form })
  return (await res.json()) as UploadResult
}

async function defaultImportRemote(noteId: string, url: string): Promise<PasteUploadResult> {
  const res = await fetch(`/api/notes/${noteId}/import-image`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url }),
  })
  return (await res.json()) as PasteUploadResult
}

function findScrollParent(el: HTMLElement | null): HTMLElement | Window {
  let node = el?.parentElement ?? null
  let fallback: HTMLElement | Window = window
  while (node) {
    const style = getComputedStyle(node)
    const oy = style.overflowY
    if (oy === 'auto' || oy === 'scroll' || oy === 'overlay') {
      // Prefer an overflow container even when content does not currently overflow —
      // short notes can start/stop scrolling as the toolbar collapses.
      fallback = node
      if (node.scrollHeight > node.clientHeight + 1) return node
    }
    node = node.parentElement
  }
  return fallback
}

function loadEditorChromePinned(): boolean {
  try {
    return localStorage.getItem('jotdex.editorChromePinned') === '1'
  } catch {
    return false
  }
}

export function NoteEditor({
  noteId,
  noteStem,
  noteRelativePath,
  noteCatalog = [],
  markdown,
  contentEpoch = 0,
  jumpHeading = null,
  headingFolds = [],
  onChange,
  attachments = [],
  editable = true,
  onServerEvent,
  onNoteMeta,
  onError,
  getEtag,
  onDirty,
  onPastePending,
  assetTransport,
  onOutline,
}: Props) {
  const [uploadStatus, setUploadStatus] = useState<string | null>(null)
  const [validationBanner, setValidationBanner] = useState<string | null>(null)
  const [pasteMode, setPasteMode] = useState<PasteMode>('smart')
  const [findOpen, setFindOpen] = useState(false)
  const [findQuery, setFindQuery] = useState('')
  const [findStatus, setFindStatus] = useState('')
  const [wikiSuggest, setWikiSuggest] = useState<WikiSuggestState>(null)
  const [wikiIndex, setWikiIndex] = useState(0)
  const [chromePinned, setChromePinned] = useState(loadEditorChromePinned)
  const [chromeScrolled, setChromeScrolled] = useState(false)
  const [chromePeek, setChromePeek] = useState(false)
  const [slashState, setSlashState] = useState<SlashMenuState>(null)
  const [slashIndex, setSlashIndex] = useState(0)
  const [plusState, setPlusState] = useState<GutterPlusState>(null)
  const [dragPos, setDragPos] = useState<{ top: number; left: number; pos: number } | null>(null)
  const [linkOpen, setLinkOpen] = useState(false)
  const [linkHref, setLinkHref] = useState('')
  const [emojiOpen, setEmojiOpen] = useState(false)
  const [emojiQuery, setEmojiQuery] = useState('')
  const [moreFormatting, setMoreFormatting] = useState(false)
  const overlaysRef = useRef(new OverlayCoordinator())
  const registry = useMemo(() => createEditorCommandRegistry(), [])
  const chromeRef = useRef<HTMLDivElement>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const findInputRef = useRef<HTMLInputElement>(null)
  const findOpenRef = useRef(false)
  findOpenRef.current = findOpen
  const wikiOnChangeRef = useRef<(s: WikiSuggestState) => void>(() => {})
  wikiOnChangeRef.current = (s) => {
    setWikiSuggest(s)
    setWikiIndex(0)
  }
  const notePathRef = useRef(noteRelativePath)
  const catalogRef = useRef(noteCatalog)
  notePathRef.current = noteRelativePath
  catalogRef.current = noteCatalog
  const editorRef = useRef<Editor | null>(null)
  const readyRef = useRef(false)
  const lastEmittedRef = useRef(markdown)
  const appliedEpochRef = useRef(contentEpoch)
  const lastAttKeyRef = useRef(attachments.map((a) => a.id).join(','))
  const pasteModeRef = useRef<PasteMode>('smart')
  const attachmentsRef = useRef(attachments)
  const noteIdRef = useRef(noteId)
  const headingFoldsRef = useRef(headingFolds)
  const lastWrittenFoldsRef = useRef<string | null>(null)
  const persistFoldsRef = useRef<(keys: string[]) => void>(() => {})
  const noteSessionRef = useRef(crypto.randomUUID())
  attachmentsRef.current = attachments
  noteIdRef.current = noteId
  headingFoldsRef.current = headingFolds
  persistFoldsRef.current = (keys) => {
    const id = noteIdRef.current
    const serialized = JSON.stringify(keys)
    if (lastWrittenFoldsRef.current === serialized) return
    lastWrittenFoldsRef.current = serialized
    void putHeadingFolds(id, keys).then((ok) => {
      if (ok) clearLegacyBrowserFoldKeys(id)
    })
  }
  pasteModeRef.current = pasteMode
  void noteStem

  const transportRef = useRef<AssetTransport>({
    uploadFile,
    importRemote: defaultImportRemote,
  })
  transportRef.current = {
    uploadFile: assetTransport?.uploadFile ?? uploadFile,
    importRemote: assetTransport?.importRemote ?? defaultImportRemote,
  }

  const coordinatorRef = useRef<EditorRevisionCoordinator | null>(null)
  if (!coordinatorRef.current) {
    coordinatorRef.current = new EditorRevisionCoordinator({
      debounceMs: 400,
      onDirty: () => onDirty?.(),
      onValidatedChange: ({ markdown: md, revision }) => {
        lastEmittedRef.current = md
        setValidationBanner(null)
        onChange(md, revision)
      },
      onValidationError: ({ diagnostics }) => {
        const msg = diagnostics.map((d) => d.message).join(' · ')
        setValidationBanner(msg || 'This note cannot be saved until the editor result is valid.')
      },
      onPastePending: (pending) => onPastePending?.(pending),
    })
  }

  const emitEvent = (event: NoteServerEvent) => {
    onServerEvent?.(event)
    if (event.kind === 'attachments-updated') {
      onNoteMeta?.({ attachments: event.attachments })
    } else if (event.kind === 'etag-confirmed') {
      onNoteMeta?.({ etag: event.etag })
    } else if (event.kind === 'replace-document') {
      onNoteMeta?.({ etag: event.etag, markdown: event.markdown })
    }
  }

  const applyUploadedAttachments = (atts: AttachmentInfo[] | undefined) => {
    if (!atts?.length) return
    const live = editorRef.current
    if (live) dispatchAttachmentInventory(live, atts)
    emitEvent({ kind: 'attachments-updated', attachments: atts })
  }

  const emitMarkdown = (ed: Editor) => {
    coordinatorRef.current?.attach(ed)
    coordinatorRef.current?.flush()
  }

  const handleUpload = async (file: File) => {
    const ed = editorRef.current
    const isImage = file.type.startsWith('image/')
    const pasteSessionId = crypto.randomUUID()
    const uploadId = crypto.randomUUID()
    if (ed && isImage) {
      insertPendingAssetAtSelection(ed, {
        uploadId,
        pasteSessionId,
        alt: file.name || 'image',
        status: 'uploading',
      })
      onPastePending?.(true)
    }
    setUploadStatus(`Uploading ${file.name || 'file'}…`)
    try {
      if (isImage && ed) {
        const { failed, lastMeta } = await runPasteSession(
          ed,
          {
            noteId: noteIdRef.current,
            noteSessionId: noteSessionRef.current,
            uploadFile: (id, f) => transportRef.current.uploadFile(id, f),
            importRemote: (id, url) => transportRef.current.importRemote(id, url),
            onAttachments: applyUploadedAttachments,
            onStatus: setUploadStatus,
            onError,
            getNoteSessionId: () => noteSessionRef.current,
          },
          [{ uploadId, kind: 'file', file, alt: file.name }],
        )
        if (lastMeta?.etag) emitEvent({ kind: 'etag-confirmed', etag: lastMeta.etag })
        if (failed) onError?.('Image upload failed — Retry from the placeholder or Remove it.')
        onPastePending?.(false)
        coordinatorRef.current?.flush()
        setUploadStatus(failed ? 'Upload failed' : 'Uploaded')
        window.setTimeout(() => setUploadStatus(null), 1200)
        return
      }

      const result = await transportRef.current.uploadFile(noteIdRef.current, file)
      if (!result.success || !result.markdownPath) {
        onError?.(result.error ?? 'Upload failed')
        setUploadStatus(null)
        return
      }
      if (result.note?.attachments) {
        applyUploadedAttachments(result.note.attachments)
      }
      if (result.note?.etag) {
        emitEvent({ kind: 'etag-confirmed', etag: result.note.etag })
      }
      const live = editorRef.current
      if (!live) return
      const label = result.fileName ?? 'attachment'
      insertMarkdown(live, `[${label}](${result.markdownPath})`)
      emitMarkdown(live)
      setUploadStatus('Uploaded')
      window.setTimeout(() => setUploadStatus(null), 1200)
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Upload failed')
      setUploadStatus(null)
      onPastePending?.(false)
    }
  }

  const uploadRef = useRef(handleUpload)
  uploadRef.current = handleUpload

  const preservePage = async (html: string, sourceUrl?: string) => {
    setUploadStatus('Saving clipped page…')
    try {
      const res = await fetch(`/api/notes/${noteIdRef.current}/preserve-page`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ html, sourceUrl, etag: getEtag?.() }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) {
        onError?.(data.error ?? 'Preserve-page failed')
        setUploadStatus(null)
        return
      }
      if (data.note?.markdown) {
        emitEvent({
          kind: 'replace-document',
          markdown: data.note.markdown,
          etag: data.etag ?? data.note.etag,
          reason: 'preserve-page',
        })
      }
      setUploadStatus('Clipped page saved')
      window.setTimeout(() => setUploadStatus(null), 1500)
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Preserve-page failed')
      setUploadStatus(null)
    }
  }
  const preserveRef = useRef(preservePage)
  preserveRef.current = preservePage

  const pasteRich = async (html: string, mode: 'smart' | 'keep') => {
    const ed = editorRef.current
    if (!ed) return

    setUploadStatus(mode === 'keep' ? 'Pasting HTML…' : 'Pasting…')
    onPastePending?.(true)
    try {
      const cleaned = cleanPasteHtml(html, { keepMore: mode === 'keep' })
      if (!cleaned) {
        setUploadStatus(null)
        onPastePending?.(false)
        return
      }

      const pasteSessionId = crypto.randomUUID()
      const { html: markedHtml, jobs } = rewritePastedImagesToPlaceholders(cleaned, pasteSessionId)
      insertHtml(ed, markedHtml)

      if (jobs.length) {
        const { imported, failed, lastMeta } = await runPasteSession(
          ed,
          {
            noteId: noteIdRef.current,
            noteSessionId: noteSessionRef.current,
            uploadFile: (id, f) => transportRef.current.uploadFile(id, f),
            importRemote: (id, url) => transportRef.current.importRemote(id, url),
            onAttachments: applyUploadedAttachments,
            onStatus: setUploadStatus,
            onError,
            getNoteSessionId: () => noteSessionRef.current,
          },
          jobs,
        )
        if (lastMeta?.etag) emitEvent({ kind: 'etag-confirmed', etag: lastMeta.etag })
        onPastePending?.(false)
        coordinatorRef.current?.flush()
        const parts = []
        if (imported) parts.push(`${imported} image${imported === 1 ? '' : 's'} saved`)
        if (failed) parts.push(`${failed} failed`)
        setUploadStatus(parts.join(' · ') || 'Pasted')
      } else {
        onPastePending?.(false)
        coordinatorRef.current?.flush()
        setUploadStatus('Pasted')
      }
      window.setTimeout(() => setUploadStatus(null), 1800)
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Paste failed')
      setUploadStatus(null)
      onPastePending?.(false)
    }
  }
  const pasteRichRef = useRef(pasteRich)
  pasteRichRef.current = pasteRich

  const runFind = (reverse = false) => {
    const ed = editorRef.current
    if (!ed || !findQuery.trim()) return
    const from = reverse ? ed.state.selection.from : ed.state.selection.to
    const hit = findInDoc(ed, findQuery, from, reverse)
    if (!hit) {
      setFindStatus('No matches')
      return
    }
    ed.chain().focus().setTextSelection(hit).run()
    setFindStatus('')
  }

  const slashStateRef = useRef(slashState)
  slashStateRef.current = slashState
  const slashIndexRef = useRef(slashIndex)
  slashIndexRef.current = slashIndex
  const plusStateRef = useRef(plusState)
  plusStateRef.current = plusState
  const insertPressRef = useRef<{ timer: number; x: number; y: number } | null>(null)

  const wikiSuggestRef = useRef(wikiSuggest)
  const wikiIndexRef = useRef(wikiIndex)
  wikiSuggestRef.current = wikiSuggest
  wikiIndexRef.current = wikiIndex
  const applyWikiLinkRef = useRef<(item: NoteCatalogItem) => void>(() => {})

  const applyWikiLink = (item: NoteCatalogItem) => {
    const ed = editorRef.current
    const suggest = wikiSuggestRef.current
    if (!ed || !suggest) return
    const href = relativeMdPath(notePathRef.current, item.relativePath)
    ed.chain().focus().deleteRange({ from: suggest.from, to: suggest.to }).run()
    insertMarkdown(ed, `[${item.title}](${href})`)
    setWikiSuggest(null)
  }
  applyWikiLinkRef.current = applyWikiLink

  const openAttachPicker = () => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = 'image/*,*/*'
    input.onchange = () => {
      const file = input.files?.[0]
      if (file) void handleUpload(file)
    }
    input.click()
  }

  const commandCtx = () => ({
    editor: editorRef.current!,
    onError,
    onRequestUpload: openAttachPicker,
    onRequestLink: () => {
      const ed = editorRef.current
      const existing = ed?.getAttributes('link').href as string | undefined
      setLinkHref(existing ?? '')
      setLinkOpen(true)
      overlaysRef.current.open('link')
    },
    onRequestEmoji: () => {
      setEmojiQuery('')
      setEmojiOpen(true)
      overlaysRef.current.open('emoji')
    },
  })

  const editor = useEditor({
    extensions: createEditorExtensions({
      withReactNodeViews: true,
      persistFolds: (keys) => persistFoldsRef.current(keys),
      wikiOnChange: (s) => wikiOnChangeRef.current(s),
      slashOnChange: (s) => {
        setSlashState(s)
        setSlashIndex(0)
        if (s?.active) overlaysRef.current.open(s.source === 'plus' ? 'plus' : 'slash')
        else if (overlaysRef.current.primary === 'slash') overlaysRef.current.close('slash')
      },
      plusOnChange: setPlusState,
      dragOnChange: setDragPos,
      attachments,
    }),
    content: '',
    editable,
    editorProps: {
      attributes: {
        class: 'tiptap-editor',
        spellcheck: 'true',
      },
      handlePaste: (_view, event) => {
        const clipboard = event.clipboardData
        if (!clipboard) return false
        const mode = pasteModeRef.current

        const image = imageFileFromClipboard(clipboard)
        if (image && mode !== 'code') {
          event.preventDefault()
          void uploadRef.current(image)
          return true
        }

        const ed = editorRef.current
        if (!ed) return false

        const shiftPlain = (event as ClipboardEvent & { shiftKey?: boolean }).shiftKey
        const html = clipboard.getData('text/html')
        const rawPlain = clipboard.getData('text/plain')
        const plain = plainTextFromClipboard(rawPlain, html)

        if (
          mode !== 'code' &&
          looksLikeBareUrl(plain) &&
          isSafeHref(plain.trim()) &&
          ed.state.selection.empty &&
          ed.state.selection.$from.parent.type.name === 'paragraph' &&
          ed.state.selection.$from.parent.textContent.trim() === ''
        ) {
          event.preventDefault()
          insertMarkdown(ed, `<!-- jotdex-link-card -->\n[${plain.trim()}](${plain.trim()})\n`)
          return true
        }

        if (ed.state.selection.empty === false && looksLikeBareUrl(plain) && isSafeHref(plain.trim()) && (!html || html.length < 40)) {
          event.preventDefault()
          ed.chain().focus().extendMarkRange('link').setLink({ href: plain.trim() }).run()
          return true
        }

        // Rich/HTML paste must never split a code box — keep every line inside the block.
        if (ed.isActive('codeBlock')) {
          event.preventDefault()
          pastePlainIntoCodeBlock(ed, plainTextForCodeBoxPaste(rawPlain, html))
          return true
        }

        if (mode === 'plain' || shiftPlain) {
          event.preventDefault()
          insertLiteralText(ed, plain)
          return true
        }

        if (mode === 'code') {
          event.preventDefault()
          const codeText = plainTextForCodeBoxPaste(rawPlain, html)
          if (ed.isActive('codeBlock')) pastePlainIntoCodeBlock(ed, codeText)
          else pasteAsCodeBlock(ed, codeText, 'powershell')
          return true
        }

        // Chrome/Word wrap a text selection as HTML (StartFragment + spans). Prefer the
        // plain characters so pasting a code-box subset doesn't insert markup.
        if (plain && htmlIsPlainClipboardSnippet(html)) {
          event.preventDefault()
          insertLiteralText(ed, plain)
          return true
        }

        if (mode === 'preserve') {
          if (html && html.length > 20) {
            event.preventDefault()
            void preserveRef.current(html)
            return true
          }
        }

        // smart / keep: rich HTML paste with structure + images
        if (html && html.length > 20 && (mode === 'smart' || mode === 'keep')) {
          event.preventDefault()
          void pasteRichRef.current(html, mode)
          return true
        }

        return false
      },
      handleDOMEvents: {
        copy: (_view, event) => copyPlainTextFromCodeBox(event),
        cut: (view, event) => {
          if (!copyPlainTextFromCodeBox(event)) return false
          if (view.editable) deleteDomSelection(view)
          return true
        },
      },
      handleDrop: (_view, event) => {
        const dt = event.dataTransfer
        if (!dt) return false

        const files: File[] = []
        if (dt.files?.length) {
          files.push(...Array.from(dt.files))
        } else if (dt.items?.length) {
          for (const item of Array.from(dt.items)) {
            if (item.kind === 'file') {
              const f = item.getAsFile()
              if (f) files.push(f)
            }
          }
        }

        if (!files.length) {
          // Block file:// URI drops from becoming broken image links in the editor
          const uri = dt.getData('text/uri-list') || dt.getData('text/plain')
          if (uri && /^(file:|[A-Za-z]:\\|\\\\)/i.test(uri.trim())) {
            event.preventDefault()
            return true
          }
          return false
        }

        event.preventDefault()
        event.stopPropagation()
        void (async () => {
          for (const file of files) {
            await uploadRef.current(file)
          }
        })()
        return true
      },
      handleKeyDown: (_view, event) => {
        if (event.key === 'Escape' && overlaysRef.current.handleEscape()) {
          setLinkOpen(false)
          setEmojiOpen(false)
          setMoreFormatting(false)
          if (slashStateRef.current?.source === 'plus') {
            const ed = editorRef.current
            if (ed) deleteTransientEmptyParagraph(ed)
          }
          setSlashState(null)
          editorRef.current?.view.dispatch(editorRef.current.state.tr.setMeta(slashMenuKey, { clear: true }))
          return true
        }
        if ((event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) && editorRef.current) {
          event.preventDefault()
          registry.execute(event.key === 'ArrowUp' ? 'block.moveUp' : 'block.moveDown', commandCtx())
          return true
        }
        const slash = slashStateRef.current
        if (slash?.active && editorRef.current) {
          const items = (slash.source === 'plus' ? registry.plusItems(slash.query) : registry.slashItems(slash.query)).filter(
            (c) => c.isEnabled(editorRef.current!),
          )
          const handled = slashKeydown(
            event,
            items,
            slashIndexRef.current,
            setSlashIndex,
            (cmd) => {
              if (slash.source === 'slash') {
                editorRef.current?.chain().focus().deleteRange({ from: slash.from, to: slash.to }).run()
              }
              registry.execute(cmd.id, commandCtx())
              setSlashState(null)
              overlaysRef.current.close()
            },
            () => {
              if (slash.source === 'plus' && editorRef.current) deleteTransientEmptyParagraph(editorRef.current)
              setSlashState(null)
              overlaysRef.current.close()
            },
          )
          if (handled) return true
        }
        const suggest = wikiSuggestRef.current
        if (suggest?.active) {
          const filtered = filterCatalog(catalogRef.current, suggest.query).slice(0, 12)
          if (event.key === 'ArrowDown' && filtered.length) {
            event.preventDefault()
            setWikiIndex((i) => (i + 1) % filtered.length)
            return true
          }
          if (event.key === 'ArrowUp' && filtered.length) {
            event.preventDefault()
            setWikiIndex((i) => (i - 1 + filtered.length) % filtered.length)
            return true
          }
          if (event.key === 'Enter' && filtered.length) {
            event.preventDefault()
            const pick = filtered[wikiIndexRef.current] ?? filtered[0]
            if (pick) applyWikiLinkRef.current(pick)
            return true
          }
          if (event.key === 'Escape') {
            event.preventDefault()
            setWikiSuggest(null)
            return true
          }
        }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
          event.preventDefault()
          setFindOpen(true)
          window.setTimeout(() => findInputRef.current?.focus(), 0)
          return true
        }
        if (event.key === 'Escape' && findOpenRef.current) {
          setFindOpen(false)
          return true
        }
        return false
      },
    },
    onCreate: ({ editor: ed }) => {
      setMarkdownDocument(ed, markdown, { emitUpdate: false })
      const resolved = foldsForNote(noteIdRef.current, headingFoldsRef.current)
      restoreHeadingFolds(ed, resolved.keys)
      lastWrittenFoldsRef.current = JSON.stringify(resolved.keys)
      if (resolved.migrateFromBrowser) persistFoldsRef.current(resolved.keys)
      coordinatorRef.current?.attach(ed)
      dispatchAttachmentInventory(ed, attachmentsRef.current)
      ;(ed.storage as { pendingAsset?: { retry?: (id: string) => void }; jotdexReplaceImage?: () => void }).pendingAsset = {
        retry: (uploadId: string) => {
          onPastePending?.(true)
          void retryPendingUpload(ed, uploadId).then(({ failed }) => {
            if (failed) onError?.('Image upload failed — Retry from the placeholder or Remove it.')
            onPastePending?.(false)
            coordinatorRef.current?.flush()
          })
        },
      }
      ;(ed.storage as { jotdexReplaceImage?: () => void }).jotdexReplaceImage = openAttachPicker
      window.setTimeout(() => {
        readyRef.current = true
        onOutline?.(extractLiveOutline(ed.state.doc))
      }, 0)
    },
    onTransaction: ({ editor: ed, transaction }) => {
      if (!readyRef.current) return
      coordinatorRef.current?.attach(ed)
      coordinatorRef.current?.observeTransaction(transaction)
      if (transaction.docChanged) onOutline?.(extractLiveOutline(ed.state.doc))
    },
  })

  useEffect(() => {
    editorRef.current = editor
    if (editor) coordinatorRef.current?.attach(editor)
  }, [editor])

  useEffect(() => {
    const onMove = (ev: Event) => {
      const ed = editorRef.current
      const detail = (ev as CustomEvent<{ pos: number; dir: -1 | 1 }>).detail
      if (!ed || !detail) return
      moveSection(ed, detail.pos, detail.dir)
    }
    window.addEventListener('jotdex-move-section', onMove)
    return () => window.removeEventListener('jotdex-move-section', onMove)
  }, [])

  useEffect(() => {
    const flush = () => {
      coordinatorRef.current?.flush()
    }
    window.addEventListener('jotdex-editor-flush', flush)
    return () => window.removeEventListener('jotdex-editor-flush', flush)
  }, [])

  useEffect(() => {
    if (!editor) return
    return installCodeBoxClipboardGuards(editor.view)
  }, [editor])

  useEffect(() => {
    if (!editor || !jumpHeading?.text) return
    const target = jumpHeading.text.trim().toLowerCase()
    let from = -1
    let to = -1
    if (jumpHeading.pos != null) {
      const node = editor.state.doc.nodeAt(jumpHeading.pos)
      if (node?.type.name === 'heading') {
        from = jumpHeading.pos + 1
        to = jumpHeading.pos + node.nodeSize - 1
      }
    }
    if (from < 0) {
      editor.state.doc.descendants((node, pos) => {
        if (from >= 0 || node.type.name !== 'heading') return
        if (node.textContent.trim().toLowerCase() === target) {
          from = pos + 1
          to = pos + node.nodeSize - 1
        }
      })
    }
    if (from < 0) return
    unfoldHeadingsContaining(editor, from)
    const reveal = () => {
      editor.chain().focus().setTextSelection({ from, to }).run()
      try {
        const dom = editor.view.domAtPos(from)
        const el = (dom.node as HTMLElement).parentElement ?? (dom.node as HTMLElement)
        el?.scrollIntoView?.({ block: 'center', behavior: 'smooth' })
      } catch {
        /* ignore */
      }
    }
    requestAnimationFrame(reveal)
  }, [jumpHeading, editor])

  // Reload only on explicit document replacement (epoch), not attachment inventory.
  useEffect(() => {
    if (!editor) return
    const attKey = attachments.map((a) => a.id).join(',')
    const attachmentsChanged = attKey !== lastAttKeyRef.current
    const epochChanged = appliedEpochRef.current !== contentEpoch
    const plan = planEditorReload({
      epochChanged,
      markdownEqualsLastEmitted: markdown === lastEmittedRef.current,
      attachmentsChanged,
    })
    if (plan.updateResolver) {
      lastAttKeyRef.current = attKey
      dispatchAttachmentInventory(editor, attachments)
    }
    if (plan.replaceDocument) {
      appliedEpochRef.current = contentEpoch
      readyRef.current = false
      assertSetContentReason('external-version')
      recordSetContent('external-version')
      logEditorDiag({ setContentReason: 'external-version', noteId })
      setMarkdownDocument(editor, markdown, { emitUpdate: false })
      dispatchAttachmentInventory(editor, attachments)
      lastEmittedRef.current = markdown
      noteSessionRef.current = crypto.randomUUID()
      coordinatorRef.current?.resetSession()
      coordinatorRef.current?.attach(editor)
      const resolved = foldsForNote(noteId, headingFoldsRef.current)
      restoreHeadingFolds(editor, resolved.keys)
      lastWrittenFoldsRef.current = JSON.stringify(resolved.keys)
      if (resolved.migrateFromBrowser) persistFoldsRef.current(resolved.keys)
      window.setTimeout(() => {
        readyRef.current = true
      }, 0)
    } else if (markdown !== lastEmittedRef.current) {
      lastEmittedRef.current = markdown
    }

    return () => {
      const keys = persistHeadingFolds(editor)
      const serialized = JSON.stringify(keys)
      if (lastWrittenFoldsRef.current === serialized) return
      lastWrittenFoldsRef.current = serialized
      void putHeadingFolds(noteId, keys).then((ok) => {
        if (ok) clearLegacyBrowserFoldKeys(noteId)
      })
    }
  }, [markdown, attachments, contentEpoch, editor, noteId])

  useEffect(() => {
    if (!editor) return
    editor.setEditable(editable)
  }, [editable, editor])

  useEffect(() => {
    const chrome = chromeRef.current
    if (!chrome) return
    // Pop-out has its own Auto/Pin chrome behavior
    if (chrome.closest('.popout-app')) return

    const scroller = findScrollParent(chrome)
    const readTop = () =>
      scroller === window ? window.scrollY : (scroller as HTMLElement).scrollTop

    // Wide hysteresis + short lockout: collapsing the toolbar changes layout height,
    // which can nudge scrollTop back across a tight threshold and flash the bar.
    const COLLAPSE_AT = 110
    const EXPAND_AT = 8
    const COOLDOWN_MS = 450
    let lockedUntil = 0
    let lockedCollapsed: boolean | null = null

    const onScroll = () => {
      const top = readTop()
      const now = performance.now()
      setChromeScrolled((prev) => {
        if (lockedCollapsed !== null && now < lockedUntil) {
          // During cooldown, only allow expand when clearly parked at the top.
          if (lockedCollapsed && top <= EXPAND_AT) {
            lockedCollapsed = false
            lockedUntil = now + COOLDOWN_MS
            return false
          }
          return lockedCollapsed
        }
        if (prev) {
          if (top <= EXPAND_AT) {
            lockedCollapsed = false
            lockedUntil = now + COOLDOWN_MS
            return false
          }
          return true
        }
        if (top > COLLAPSE_AT) {
          lockedCollapsed = true
          lockedUntil = now + COOLDOWN_MS
          return true
        }
        return false
      })
      if (top <= EXPAND_AT) setChromePeek(false)
    }

    onScroll()
    scroller.addEventListener('scroll', onScroll, { passive: true })
    return () => scroller.removeEventListener('scroll', onScroll)
  }, [editor, noteId])

  const runCmd = (id: Parameters<typeof registry.execute>[0]) => {
    if (!editor) return
    registry.execute(id, commandCtx())
  }

  if (!editor) return null

  const openInsertMenu = (fromGap: boolean) => {
    const plus = plusStateRef.current
    if (fromGap && plus?.fromGap) insertTransientParagraphAt(editor.view, plus.pos)
    const next = {
      active: true as const,
      from: editor.state.selection.from,
      to: editor.state.selection.to,
      query: '',
      source: 'plus' as const,
    }
    editor.view.dispatch(editor.state.tr.setMeta(slashMenuKey, next))
    overlaysRef.current.open('plus')
  }

  const chromeAuto = !chromePinned
  const chromeCollapsed = chromeAuto && chromeScrolled && !chromePeek

  return (
    <div
      ref={rootRef}
      className={`rich-editor${chromeAuto ? ' chrome-autohide' : ' chrome-pinned'}${chromeScrolled ? ' is-scrolled' : ''}${chromePeek ? ' chrome-peek' : ''}`}
    >
      <div
        ref={chromeRef}
        className={`editor-chrome${chromeCollapsed ? ' is-collapsed' : ''}`}
        onMouseLeave={() => {
          if (chromeAuto && chromeScrolled) setChromePeek(false)
        }}
      >
        <div className="editor-chrome-peek">
          <button
            type="button"
            className="chrome-peek-toggle"
            title={chromeCollapsed ? 'Show formatting tools' : 'Formatting tools'}
            onClick={() => {
              if (chromeAuto && chromeScrolled) setChromePeek((v) => !v)
            }}
          >
            {chromeCollapsed ? 'Formatting ▾' : 'Formatting'}
          </button>
          <button
            type="button"
            className={`ghost chrome-pin-btn${chromePinned ? ' on' : ''}`}
            title={
              chromePinned
                ? 'Pinned — formatting bar stays open while you scroll'
                : 'Auto — collapses while you scroll; hover or tap to expand. Expands again at the top of the note.'
            }
            onClick={() => {
              setChromePinned((v) => {
                const next = !v
                try {
                  localStorage.setItem('jotdex.editorChromePinned', next ? '1' : '0')
                } catch {
                  /* ignore */
                }
                if (next) setChromePeek(false)
                return next
              })
            }}
          >
            {chromePinned ? 'Pinned' : 'Auto'}
          </button>
        </div>
      <div className="editor-chrome-inner">
      <div
        className="editor-toolbar"
        role="toolbar"
        aria-label="Formatting"
        onMouseDown={(e) => {
          // Keep the editor selection intact while clicking toolbar buttons
          // (selects still need focus for their dropdowns).
          if ((e.target as HTMLElement).closest('button')) e.preventDefault()
        }}
      >
        <button
          type="button"
          className={`ghost chrome-pin-btn toolbar-chrome-pin${chromePinned ? ' on' : ''}`}
          title={
            chromePinned
              ? 'Pinned — formatting bar stays open while you scroll'
              : 'Auto — collapses while you scroll; hover or tap Formatting to expand. Expands again near the top.'
          }
          onClick={() => {
            setChromePinned((v) => {
              const next = !v
              try {
                localStorage.setItem('jotdex.editorChromePinned', next ? '1' : '0')
              } catch {
                /* ignore */
              }
              if (next) setChromePeek(false)
              return next
            })
          }}
        >
          {chromePinned ? 'Pinned' : 'Auto'}
        </button>
        <span className="sep toolbar-chrome-pin-sep" />
        <button
          type="button"
          className={editor.isActive('heading', { level: 1 }) ? 'on' : ''}
          title="Heading 1 — with a selection, only the selected text becomes the heading"
          onClick={() => runCmd('heading.1')}
        >
          H1
        </button>
        <button
          type="button"
          className={editor.isActive('heading', { level: 2 }) ? 'on' : ''}
          title="Heading 2 — with a selection, only the selected text becomes the heading"
          onClick={() => runCmd('heading.2')}
        >
          H2
        </button>
        <button
          type="button"
          className={editor.isActive('heading', { level: 3 }) ? 'on' : ''}
          title="Heading 3 — with a selection, only the selected text becomes the heading"
          onClick={() => runCmd('heading.3')}
        >
          H3
        </button>
        <span className="sep" />
        <button
          type="button"
          className={editor.isActive('bold') ? 'on' : ''}
          onClick={() => runCmd('mark.bold')}
        >
          Bold
        </button>
        <button
          type="button"
          className={editor.isActive('italic') ? 'on' : ''}
          onClick={() => runCmd('mark.italic')}
        >
          Italic
        </button>
        <button
          type="button"
          className={`strike-btn${editor.isActive('strike') ? ' on' : ''}`}
          title="Strikethrough — keep the text, mark it to ignore"
          onClick={() => runCmd('mark.strike')}
        >
          Strike
        </button>
        <button
          type="button"
          className={editor.isActive('code') ? 'on' : ''}
          onClick={() => runCmd('mark.code')}
        >
          Code
        </button>
        <button
          type="button"
          title="Remove formatting from the selection (bold, color, lists, headings, …)"
          onClick={() => runCmd('format.clear')}
        >
          Clear
        </button>
        <label className="toolbar-select" title="Text color for the selection">
          <span className="sr-only">Color</span>
          <select
            aria-label="Text color"
            value={editor.getAttributes('textStyle').color ?? ''}
            onChange={(e) => {
              const v = e.target.value
              normalizeBlockSelection(editor)
              if (!v) editor.chain().focus().unsetColor().run()
              else editor.chain().focus().setColor(v).run()
            }}
          >
            {TEXT_COLORS.map((c) => (
              <option key={c.label} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>
        <label className="toolbar-select" title="Font size for the selection">
          <span className="sr-only">Font size</span>
          <select
            aria-label="Font size"
            value={editor.getAttributes('textStyle').fontSize ?? ''}
            onChange={(e) => {
              const v = e.target.value
              normalizeBlockSelection(editor)
              if (!v || v === '1em') {
                editor.chain().focus().unsetMark('textStyle').run()
              } else {
                editor.chain().focus().setMark('textStyle', { fontSize: v }).run()
              }
            }}
          >
            {FONT_SIZES.map((s) => (
              <option key={s.label} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </label>
        <span className="sep" />
        <button
          type="button"
          className={editor.isActive('bulletList') ? 'on' : ''}
          onClick={() => runCmd('block.bulletList')}
        >
          List
        </button>
        <button
          type="button"
          className={editor.isActive('orderedList') ? 'on' : ''}
          onClick={() => runCmd('block.orderedList')}
        >
          1.
        </button>
        <button
          type="button"
          className={editor.isActive('taskList') ? 'on' : ''}
          onClick={() => runCmd('block.taskList')}
          title="Turn selection into a checklist todo (appears in the Todos rail)"
        >
          Todo
        </button>
        <button
          type="button"
          className={editor.isActive('codeBlock') ? 'on' : ''}
          title="Insert a code box (PowerShell, shell, etc.)"
          onClick={() => runCmd('block.codeBlock')}
        >
          Code box
        </button>
        <button
          type="button"
          title="Paste clipboard as code box"
          onClick={() => {
            void navigator.clipboard.readText().then((text) => {
              pasteAsCodeBlock(editor, text, 'powershell')
            }).catch(() => onError?.('Could not read clipboard'))
          }}
        >
          Paste code
        </button>
        <span className="sep" />
        <button
          type="button"
          onClick={() => runCmd('insert.link')}
        >
          Link
        </button>
        <label className="toolbar-select" title="Insert a callout block">
          <span className="sr-only">Callout</span>
          <select
            aria-label="Insert callout"
            defaultValue=""
            onChange={(e) => {
              const v = e.target.value as CalloutType | ''
              e.target.value = ''
              if (!v) return
              runCmd(`block.callout.${v}` as Parameters<typeof registry.execute>[0])
            }}
          >
            <option value="" disabled>
              Callout
            </option>
            <option value="note">Note</option>
            <option value="tip">Tip</option>
            <option value="info">Info</option>
            <option value="warning">Warning</option>
            <option value="danger">Danger</option>
          </select>
        </label>
        <button
          type="button"
          className="jotdex-toolbar-insert"
          aria-label="Insert block"
          title="Insert a block (type / or use + in a gap)"
          onClick={() => openInsertMenu(false)}
        >
          Insert
        </button>
        <button type="button" onClick={() => runCmd('block.table')}>
          Table
        </button>
        <button type="button" onClick={() => runCmd('block.details')}>
          Details
        </button>
        <button type="button" onClick={() => runCmd('insert.emoji')}>
          Emoji
        </button>
        <button
          type="button"
          onClick={() => runCmd('insert.image')}
        >
          Attach
        </button>
        <button type="button" onClick={() => runCmd('history.undo')}>
          Undo
        </button>
        <button type="button" onClick={() => runCmd('history.redo')}>
          Redo
        </button>
        <button
          type="button"
          className={findOpen ? 'on' : ''}
          onClick={() => {
            setFindOpen((o) => !o)
            window.setTimeout(() => findInputRef.current?.focus(), 0)
          }}
        >
          Find
        </button>
      </div>

      <div className="paste-modes" role="group" aria-label="Paste mode">
        <span className="paste-label">Paste:</span>
        {(
          [
            ['smart', 'Smart'],
            ['keep', 'Keep HTML'],
            ['preserve', 'Preserve page'],
            ['plain', 'Plain'],
            ['code', 'As code'],
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            className={pasteMode === id ? 'on' : ''}
            onClick={() => setPasteMode(id)}
            title={
              id === 'smart'
                ? 'Paste with headings, lists, links, and images (downloads pictures into the note)'
                : id === 'plain'
                  ? 'Strip formatting (also Ctrl+Shift+V)'
                  : id === 'code'
                    ? 'Insert clipboard as a code block'
                    : id === 'preserve'
                      ? 'Save sanitized HTML sidecar and link it in the note'
                      : 'Keep more HTML styles; still downloads images into the note'
            }
          >
            {label}
          </button>
        ))}
      </div>

      {findOpen && (
        <div className="find-bar">
          <input
            ref={findInputRef}
            value={findQuery}
            onChange={(e) => setFindQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                runFind(e.shiftKey)
              }
              if (e.key === 'Escape') setFindOpen(false)
            }}
            placeholder="Find in note…"
            aria-label="Find in note"
          />
          <button type="button" className="ghost" onClick={() => runFind(true)}>
            Prev
          </button>
          <button type="button" className="ghost" onClick={() => runFind(false)}>
            Next
          </button>
          <button type="button" className="ghost" onClick={() => setFindOpen(false)}>
            Close
          </button>
          {findStatus && <span className="find-status">{findStatus}</span>}
        </div>
      )}

      {uploadStatus && <div className="upload-status">{uploadStatus}</div>}
      </div>
      </div>
      <div
        className="editor-stage"
        onPointerDown={(e) => {
          if (e.pointerType !== 'touch') return
          const timer = window.setTimeout(() => {
            openInsertMenu(Boolean(plusStateRef.current?.fromGap))
          }, 500)
          insertPressRef.current = { timer, x: e.clientX, y: e.clientY }
        }}
        onPointerMove={(e) => {
          const press = insertPressRef.current
          if (!press) return
          if (Math.hypot(e.clientX - press.x, e.clientY - press.y) > 12) {
            window.clearTimeout(press.timer)
            insertPressRef.current = null
          }
        }}
        onPointerUp={() => {
          if (insertPressRef.current) window.clearTimeout(insertPressRef.current.timer)
          insertPressRef.current = null
        }}
        onPointerCancel={() => {
          if (insertPressRef.current) window.clearTimeout(insertPressRef.current.timer)
          insertPressRef.current = null
        }}
      >
        {wikiSuggest?.active && (
          <div className="wiki-suggest" role="listbox" aria-label="Link to note">
            {filterCatalog(noteCatalog, wikiSuggest.query)
              .slice(0, 12)
              .map((item, i) => (
                <button
                  key={item.id}
                  type="button"
                  role="option"
                  aria-selected={i === wikiIndex}
                  className={i === wikiIndex ? 'on' : ''}
                  onMouseDown={(e) => {
                    e.preventDefault()
                    applyWikiLink(item)
                  }}
                >
                  <span className="note-title">{item.title}</span>
                  <span className="note-path">{item.folderPath || '/'}</span>
                </button>
              ))}
            {filterCatalog(noteCatalog, wikiSuggest.query).length === 0 && (
              <div className="wiki-suggest-empty">No matching notes — keep typing or finish with ]]</div>
            )}
          </div>
        )}
        {validationBanner && (
          <div className="source-banner" role="alert">
            <p>{validationBanner}</p>
            <button type="button" className="ghost" onClick={() => setValidationBanner(null)}>
              Continue editing
            </button>
          </div>
        )}
        <EditorContent editor={editor} />
        <FormattingBubbleMenu
          editor={editor}
          registry={registry}
          ctx={commandCtx()}
          overlays={overlaysRef.current}
          moreOpen={moreFormatting}
          onMore={setMoreFormatting}
        />
        <TableChrome editor={editor} />
        {slashState?.active && (
          <SlashMenu
            editor={editor}
            state={slashState}
            registry={registry}
            ctx={commandCtx()}
            index={slashIndex}
            onIndex={setSlashIndex}
            onClose={() => {
              editor.view.dispatch(editor.state.tr.setMeta(slashMenuKey, { clear: true }))
              overlaysRef.current.close()
            }}
          />
        )}
        {plusState?.visible && !slashState?.active && (
          <button
            type="button"
            className="jotdex-gutter-plus"
            style={{ top: plusState.top, left: Math.max(8, plusState.left - 28) }}
            aria-label="Insert at gap"
            onMouseDown={(e) => {
              e.preventDefault()
              openInsertMenu(plusState.fromGap)
            }}
          >
            +
          </button>
        )}
        {dragPos && editor.isEditable && (
          <div className="jotdex-drag-handle" style={{ top: dragPos.top, left: Math.max(4, dragPos.left - 22) }}>
            <button
              type="button"
              title="Move block up"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCmd('block.moveUp')}
            >
              ↑
            </button>
            <button
              type="button"
              title="Move block down"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => runCmd('block.moveDown')}
            >
              ↓
            </button>
          </div>
        )}
        {linkOpen && (
          <LinkPopover
            editor={editor}
            href={linkHref}
            onHref={setLinkHref}
            notes={noteCatalog}
            relativeFor={(path) => relativeMdPath(notePathRef.current, path)}
            onClose={() => {
              setLinkOpen(false)
              overlaysRef.current.close('link')
            }}
          />
        )}
        {emojiOpen && (
          <EmojiPicker
            query={emojiQuery}
            onQuery={setEmojiQuery}
            onPick={(ch) => {
              registry.execute('insert.emoji', { ...commandCtx(), extra: ch })
              setEmojiOpen(false)
              overlaysRef.current.close('emoji')
            }}
            onClose={() => {
              setEmojiOpen(false)
              overlaysRef.current.close('emoji')
            }}
          />
        )}
      </div>
    </div>
  )
}

function filterCatalog(catalog: NoteCatalogItem[], query: string): NoteCatalogItem[] {
  const q = query.trim().toLowerCase()
  if (!q) return catalog.slice(0, 40)
  return catalog.filter(
    (n) => n.title.toLowerCase().includes(q) || n.relativePath.toLowerCase().includes(q),
  )
}
