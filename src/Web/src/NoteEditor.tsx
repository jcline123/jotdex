import { useEffect, useRef, useState } from 'react'
import { EditorContent, ReactNodeViewRenderer, useEditor } from '@tiptap/react'
import type { Editor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import Placeholder from '@tiptap/extension-placeholder'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { Table } from '@tiptap/extension-table'
import { TableRow } from '@tiptap/extension-table-row'
import { TableCell } from '@tiptap/extension-table-cell'
import { TableHeader } from '@tiptap/extension-table-header'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import { common, createLowlight } from 'lowlight'
import powershell from 'highlight.js/lib/languages/powershell'
import dos from 'highlight.js/lib/languages/dos'
import { Markdown } from 'tiptap-markdown'
import { Extension } from '@tiptap/core'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import { CodeBlockView } from './CodeBlockView'
import { ImageView } from './ImageView'
import { applyHeadingToSelection } from './headingSelection'
import { normalizeBlockSelection } from './selectionUtils'
import { Callout, type CalloutType } from './callout'
import { HeadingFold } from './headingFold'
import { WikiLinkSuggest, type WikiSuggestState } from './wikiLinkSuggest'
import { relativeMdPath } from './paths'
import {
  cleanPasteHtml,
  dataUrlToFile,
  extractHttpImageUrls,
  rewriteDataImages,
} from './pasteHtml'

const lowlight = createLowlight(common)
lowlight.register('powershell', powershell)
lowlight.register('ps1', powershell)
lowlight.register('pwsh', powershell)
lowlight.register('cmd', dos)
lowlight.register('bat', dos)

const FontSizeTextStyle = TextStyle.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      fontSize: {
        default: null,
        parseHTML: (element) => (element as HTMLElement).style.fontSize?.replace(/['"]+/g, '') || null,
        renderHTML: (attributes) => {
          if (!attributes.fontSize) return {}
          return { style: `font-size: ${attributes.fontSize}` }
        },
      },
    }
  },
})

/**
 * Accidental Shift+Enter in a plain paragraph/heading inserts an invisible hard
 * break, which spaces differently from an Enter paragraph and confuses spacing
 * (trailing `\` in the vault Markdown). Treat Shift+Enter as Enter there; keep
 * the real line break inside lists, tasks, tables, and blockquotes where a
 * break is the only way to get a new line without changing structure.
 */
const ConsistentLineBreaks = Extension.create({
  name: 'consistentLineBreaks',
  priority: 1000,
  addKeyboardShortcuts() {
    return {
      'Shift-Enter': () => {
        const { $from } = this.editor.state.selection
        for (let depth = $from.depth; depth > 0; depth--) {
          const name = $from.node(depth).type.name
          if (
            name === 'listItem' ||
            name === 'taskItem' ||
            name === 'tableCell' ||
            name === 'tableHeader' ||
            name === 'blockquote' ||
            name === 'codeBlock'
          ) {
            return false
          }
        }
        return this.editor.commands.first(({ commands }) => [
          () => commands.newlineInCode(),
          () => commands.createParagraphNear(),
          () => commands.liftEmptyBlock(),
          () => commands.splitBlock(),
        ])
      },
    }
  },
})

const CodeBlockBox = CodeBlockLowlight.extend({
  addNodeView() {
    return ReactNodeViewRenderer(CodeBlockView)
  },
}).configure({ lowlight, defaultLanguage: 'plaintext' })

const ImageBox = Image.extend({
  addNodeView() {
    return ReactNodeViewRenderer(ImageView)
  },
}).configure({
  allowBase64: true,
  inline: false,
  HTMLAttributes: { class: 'note-image-img' },
})
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
  jumpHeading?: { text: string; nonce: number } | null
  onChange: (markdown: string) => void
  attachments?: AttachmentInfo[]
  editable?: boolean
  onNoteMeta?: (note: { etag?: string; attachments?: AttachmentInfo[]; markdown?: string; htmlSidecars?: unknown }) => void
  onError?: (message: string) => void
  getEtag?: () => string
}

type UploadResult = {
  success: boolean
  error?: string
  markdownPath?: string
  attachmentId?: string
  isImage?: boolean
  fileName?: string
  note?: { etag?: string; attachments?: AttachmentInfo[]; markdown?: string }
}

function getMarkdown(editor: Editor): string {
  const md = (editor.storage as { markdown?: { getMarkdown?: () => string } }).markdown
  return md?.getMarkdown?.() ?? ''
}

function encodeSpaces(value: string) {
  return Array.from(value)
    .map((ch) => {
      const code = ch.codePointAt(0)!
      if (ch === ' ' || code === 0x00a0 || code === 0x202f || code === 0x2007) return '%20'
      return ch
    })
    .join('')
}

/** Encode markdown link/image targets so ? # & and odd whitespace don't break the URL. */
function encodeMdPath(value: string) {
  return Array.from(value)
    .map((ch) => {
      const code = ch.codePointAt(0)!
      if (ch === ' ') return '%20'
      if (code === 0x00a0 || code === 0x202f || code === 0x2007) return '%20'
      if (/[%?#&[\]()'"\\]/.test(ch)) return `%${code.toString(16).toUpperCase().padStart(2, '0')}`
      if (code < 32 || code === 127) return ''
      return ch
    })
    .join('')
}

function mdPathFor(stem: string, fileName: string) {
  return `${encodeMdPath(stem)}.assets/${encodeMdPath(fileName)}`
}

function markdownForEditor(markdown: string, attachments: AttachmentInfo[]): string {
  if (!attachments.length) return markdown
  let result = markdown
  for (const att of attachments) {
    const api = `/api/attachments/${att.id}`
    // Match any markdown image/link target that ends with this attachment file name
    // (plain, space-encoded, or fully path-encoded), including odd stems like '#tags'.
    const nameVariants = Array.from(
      new Set([
        att.fileName,
        encodeSpaces(att.fileName),
        encodeMdPath(att.fileName),
        encodeURIComponent(att.fileName),
      ]),
    )
    for (const name of nameVariants) {
      const re = new RegExp(`\\(([^)\\s]*${escapeRegExp('.assets/' + name)})\\)`, 'gi')
      result = result.replace(re, `(${api})`)
    }
  }
  return result
}

function escapeRegExp(s: string) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function toVaultMarkdown(editor: Editor, attachments: AttachmentInfo[], apiToMd: Map<string, string>, stem: string) {
  let md = getMarkdown(editor)
  for (const [api, rel] of apiToMd) {
    md = md.split(api).join(rel)
  }
  for (const att of attachments) {
    const api = `/api/attachments/${att.id}`
    if (md.includes(api)) {
      md = md.split(api).join(mdPathFor(stem, att.fileName))
    }
  }
  return md
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

function replaceImageSrc(editor: Editor, fromSrc: string, toSrc: string, alt?: string) {
  const { state } = editor
  let tr = state.tr
  let changed = false
  state.doc.descendants((node, pos) => {
    if (node.type.name !== 'image') return
    if (node.attrs.src !== fromSrc) return
    tr = tr.setNodeMarkup(pos, undefined, {
      ...node.attrs,
      src: toSrc,
      alt: alt || node.attrs.alt || 'image',
    })
    changed = true
  })
  if (changed) editor.view.dispatch(tr)
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
  onChange,
  attachments = [],
  editable = true,
  onNoteMeta,
  onError,
  getEtag,
}: Props) {
  const [uploadStatus, setUploadStatus] = useState<string | null>(null)
  const [pasteMode, setPasteMode] = useState<PasteMode>('smart')
  const [findOpen, setFindOpen] = useState(false)
  const [findQuery, setFindQuery] = useState('')
  const [findStatus, setFindStatus] = useState('')
  const [wikiSuggest, setWikiSuggest] = useState<WikiSuggestState>(null)
  const [wikiIndex, setWikiIndex] = useState(0)
  const [chromePinned, setChromePinned] = useState(loadEditorChromePinned)
  const [chromeScrolled, setChromeScrolled] = useState(false)
  const [chromePeek, setChromePeek] = useState(false)
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
  const apiToMd = useRef(new Map<string, string>())
  const editorRef = useRef<Editor | null>(null)
  const readyRef = useRef(false)
  const lastEmittedRef = useRef(markdown)
  const appliedEpochRef = useRef(contentEpoch)
  const lastAttKeyRef = useRef(attachments.map((a) => a.id).join(','))
  const pasteModeRef = useRef<PasteMode>('smart')
  const attachmentsRef = useRef(attachments)
  const stemRef = useRef(noteStem)
  const noteIdRef = useRef(noteId)
  attachmentsRef.current = attachments
  stemRef.current = noteStem
  noteIdRef.current = noteId
  pasteModeRef.current = pasteMode

  const emitMarkdown = (ed: Editor) => {
    const md = toVaultMarkdown(ed, attachmentsRef.current, apiToMd.current, stemRef.current)
    lastEmittedRef.current = md
    onChange(md)
  }

  const handleUpload = async (file: File) => {
    setUploadStatus(`Uploading ${file.name || 'file'}…`)
    try {
      const result = await uploadFile(noteIdRef.current, file)
      if (!result.success || !result.markdownPath) {
        onError?.(result.error ?? 'Upload failed')
        setUploadStatus(null)
        return
      }
      if (result.attachmentId) {
        apiToMd.current.set(`/api/attachments/${result.attachmentId}`, result.markdownPath)
      }
      // Attachments only — note.markdown on the upload response is pre-edit disk state.
      if (result.note) {
        onNoteMeta?.({
          etag: result.note.etag,
          attachments: result.note.attachments,
        })
      }

      const ed = editorRef.current
      if (!ed) return
      if (result.isImage && result.attachmentId) {
        ed.chain()
          .focus()
          .setImage({ src: `/api/attachments/${result.attachmentId}`, alt: result.fileName ?? 'image' })
          .run()
      } else {
        const label = result.fileName ?? 'attachment'
        ed.chain().focus().insertContent(`[${label}](${result.markdownPath})`).run()
      }
      emitMarkdown(ed)
      setUploadStatus('Uploaded')
      window.setTimeout(() => setUploadStatus(null), 1200)
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Upload failed')
      setUploadStatus(null)
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
      if (data.note) {
        onNoteMeta?.({
          etag: data.etag ?? data.note.etag,
          attachments: data.note.attachments,
          markdown: data.note.markdown,
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
    try {
      const cleaned = cleanPasteHtml(html, { keepMore: mode === 'keep' })
      if (!cleaned) {
        setUploadStatus(null)
        return
      }

      const { html: markedHtml, items } = rewriteDataImages(cleaned)
      const remoteUrls = extractHttpImageUrls(markedHtml).filter((u) => !u.includes('paste.invalid'))

      ed.chain().focus().insertContent(markedHtml).run()

      let lastMeta: UploadResult['note'] | undefined
      let imported = 0
      let failed = 0

      for (const item of items) {
        try {
          const file = dataUrlToFile(item.mime, item.bytesBase64, item.fileName)
          const result = await uploadFile(noteIdRef.current, file)
          if (!result.success || !result.attachmentId) {
            failed++
            continue
          }
          if (result.markdownPath) {
            apiToMd.current.set(`/api/attachments/${result.attachmentId}`, result.markdownPath)
          }
          replaceImageSrc(ed, item.marker, `/api/attachments/${result.attachmentId}`, result.fileName)
          lastMeta = result.note ?? lastMeta
          imported++
        } catch {
          failed++
        }
      }

      for (const url of remoteUrls.slice(0, 20)) {
        try {
          const res = await fetch(`/api/notes/${noteIdRef.current}/import-image`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ url }),
          })
          const data = (await res.json()) as {
            success?: boolean
            attachmentId?: string
            markdownPath?: string
            fileName?: string
            note?: UploadResult['note']
            error?: string
          }
          if (!res.ok || !data.success || !data.attachmentId) {
            failed++
            continue
          }
          if (data.markdownPath) {
            apiToMd.current.set(`/api/attachments/${data.attachmentId}`, data.markdownPath)
          }
          replaceImageSrc(ed, url, `/api/attachments/${data.attachmentId}`, data.fileName)
          lastMeta = data.note ?? lastMeta
          imported++
        } catch {
          failed++
        }
      }

      emitMarkdown(ed)
      // Attachment APIs return disk markdown *before* this paste is saved — never push
      // that body into the parent or the editor will reload and the paste vanishes.
      if (lastMeta) {
        onNoteMeta?.({
          etag: lastMeta.etag,
          attachments: lastMeta.attachments,
        })
      }

      if (imported > 0 || failed > 0) {
        const parts = []
        if (imported) parts.push(`${imported} image${imported === 1 ? '' : 's'} saved`)
        if (failed) parts.push(`${failed} failed`)
        setUploadStatus(parts.join(' · ') || 'Pasted')
      } else {
        setUploadStatus('Pasted')
      }
      window.setTimeout(() => setUploadStatus(null), 1800)
    } catch (e) {
      onError?.(e instanceof Error ? e.message : 'Paste failed')
      setUploadStatus(null)
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
    ed.chain()
      .focus()
      .deleteRange({ from: suggest.from, to: suggest.to })
      .insertContent(`[${item.title}](${href})`)
      .run()
    setWikiSuggest(null)
  }
  applyWikiLinkRef.current = applyWikiLink

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ codeBlock: false }),
      CodeBlockBox,
      Link.configure({ openOnClick: false, autolink: true }),
      ImageBox,
      Callout,
      HeadingFold,
      WikiLinkSuggest.configure({
        onChange: (s) => wikiOnChangeRef.current(s),
      }),
      Placeholder.configure({ placeholder: 'Start writing… Type [[ to link a note' }),
      TaskList,
      TaskItem.configure({ nested: true }),
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      FontSizeTextStyle,
      Color,
      ConsistentLineBreaks,
      Markdown.configure({
        // Allow <span style="color/font-size"> so color & size round-trip in the vault.
        html: true,
        // We handle rich paste ourselves so formatting/images aren't stripped by MD transform.
        transformPastedText: false,
        transformCopiedText: true,
      }),
    ],
    content: markdownForEditor(markdown, attachments),
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

        const files = Array.from(clipboard.files)
        const image = files.find((f) => f.type.startsWith('image/'))
        if (image && mode !== 'code') {
          event.preventDefault()
          void uploadRef.current(image)
          return true
        }

        const ed = editorRef.current
        if (!ed) return false

        const shiftPlain = (event as ClipboardEvent & { shiftKey?: boolean }).shiftKey
        if (mode === 'plain' || shiftPlain) {
          event.preventDefault()
          const text = clipboard.getData('text/plain')
          ed.chain().focus().insertContent(text).run()
          return true
        }

        if (mode === 'code') {
          event.preventDefault()
          const text = clipboard.getData('text/plain')
          ed.chain()
            .focus()
            .insertContent({
              type: 'codeBlock',
              attrs: { language: 'powershell' },
              content: text ? [{ type: 'text', text }] : undefined,
            })
            .run()
          return true
        }

        if (mode === 'preserve') {
          const html = clipboard.getData('text/html')
          if (html && html.length > 20) {
            event.preventDefault()
            void preserveRef.current(html)
            return true
          }
        }

        // smart / keep: rich HTML paste with structure + images
        const html = clipboard.getData('text/html')
        if (html && html.length > 20 && (mode === 'smart' || mode === 'keep')) {
          event.preventDefault()
          void pasteRichRef.current(html, mode)
          return true
        }

        return false
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
    onCreate: () => {
      window.setTimeout(() => {
        readyRef.current = true
      }, 0)
    },
    onUpdate: ({ editor: ed }) => {
      if (!readyRef.current) return
      emitMarkdown(ed)
    },
  })

  useEffect(() => {
    editorRef.current = editor
  }, [editor])

  useEffect(() => {
    if (!editor || !jumpHeading?.text) return
    const target = jumpHeading.text.trim().toLowerCase()
    let from = -1
    let to = -1
    editor.state.doc.descendants((node, pos) => {
      if (from >= 0 || node.type.name !== 'heading') return
      if (node.textContent.trim().toLowerCase() === target) {
        from = pos + 1
        to = pos + node.nodeSize - 1
      }
    })
    if (from < 0) return
    editor.chain().focus().setTextSelection({ from, to }).run()
    try {
      const dom = editor.view.domAtPos(from)
      const el = (dom.node as HTMLElement).parentElement ?? (dom.node as HTMLElement)
      el?.scrollIntoView?.({ block: 'center', behavior: 'smooth' })
    } catch {
      /* ignore */
    }
  }, [jumpHeading, editor])

  // Only reload editor document on external content changes (note open / conflict / preserve-page),
  // or when attachments arrive so local image paths can be rewritten to /api/attachments/{id}.
  useEffect(() => {
    if (!editor) return
    const attKey = attachments.map((a) => a.id).join(',')
    const epochChanged = appliedEpochRef.current !== contentEpoch
    const markdownChanged = markdown !== lastEmittedRef.current
    const attachmentsChanged = attKey !== lastAttKeyRef.current
    if (!epochChanged && !markdownChanged && !attachmentsChanged) return

    appliedEpochRef.current = contentEpoch
    lastAttKeyRef.current = attKey
    readyRef.current = false
    const display = markdownForEditor(markdown, attachments)
    editor.commands.setContent(display, { emitUpdate: false })
    lastEmittedRef.current = markdown
    window.setTimeout(() => {
      readyRef.current = true
    }, 0)
  }, [markdown, attachments, contentEpoch, editor])

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

    // Hysteresis: collapsing the toolbar shortens the page. On short notes that makes
    // scrollTop snap back under the collapse threshold and the bar thrash. Stay collapsed
    // until near the top; only collapse after a clearer scroll.
    const COLLAPSE_AT = 72
    const EXPAND_AT = 16

    const onScroll = () => {
      const top = readTop()
      setChromeScrolled((prev) => {
        if (prev) return top > EXPAND_AT
        return top > COLLAPSE_AT
      })
      if (top <= EXPAND_AT) setChromePeek(false)
    }

    onScroll()
    scroller.addEventListener('scroll', onScroll, { passive: true })
    return () => scroller.removeEventListener('scroll', onScroll)
  }, [editor, noteId])

  if (!editor) return null

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
          onClick={() => applyHeadingToSelection(editor, 1)}
        >
          H1
        </button>
        <button
          type="button"
          className={editor.isActive('heading', { level: 2 }) ? 'on' : ''}
          title="Heading 2 — with a selection, only the selected text becomes the heading"
          onClick={() => applyHeadingToSelection(editor, 2)}
        >
          H2
        </button>
        <button
          type="button"
          className={editor.isActive('heading', { level: 3 }) ? 'on' : ''}
          title="Heading 3 — with a selection, only the selected text becomes the heading"
          onClick={() => applyHeadingToSelection(editor, 3)}
        >
          H3
        </button>
        <span className="sep" />
        <button
          type="button"
          className={editor.isActive('bold') ? 'on' : ''}
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleBold().run()
          }}
        >
          Bold
        </button>
        <button
          type="button"
          className={editor.isActive('italic') ? 'on' : ''}
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleItalic().run()
          }}
        >
          Italic
        </button>
        <button
          type="button"
          className={editor.isActive('code') ? 'on' : ''}
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleCode().run()
          }}
        >
          Code
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
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleBulletList().run()
          }}
        >
          List
        </button>
        <button
          type="button"
          className={editor.isActive('orderedList') ? 'on' : ''}
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleOrderedList().run()
          }}
        >
          1.
        </button>
        <button
          type="button"
          className={editor.isActive('taskList') ? 'on' : ''}
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleTaskList().run()
          }}
          title="Turn selection into a checklist todo (appears under Todos → From notes)"
        >
          Todo
        </button>
        <button
          type="button"
          className={editor.isActive('codeBlock') ? 'on' : ''}
          title="Insert a code box (PowerShell, shell, etc.)"
          onClick={() => {
            normalizeBlockSelection(editor)
            editor.chain().focus().toggleCodeBlock({ language: 'powershell' }).run()
          }}
        >
          Code box
        </button>
        <button
          type="button"
          title="Paste clipboard as code box"
          onClick={() => {
            void navigator.clipboard.readText().then((text) => {
              editor
                .chain()
                .focus()
                .insertContent({
                  type: 'codeBlock',
                  attrs: { language: 'powershell' },
                  content: text ? [{ type: 'text', text }] : undefined,
                })
                .run()
            }).catch(() => onError?.('Could not read clipboard'))
          }}
        >
          Paste code
        </button>
        <span className="sep" />
        <button
          type="button"
          onClick={() => {
            const url = window.prompt('Link URL')
            if (!url) return
            editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
          }}
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
              editor.chain().focus().setCallout(v).run()
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
        <button type="button" onClick={() => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()}>
          Table
        </button>
        <button
          type="button"
          onClick={() => {
            const input = document.createElement('input')
            input.type = 'file'
            input.accept = 'image/*,*/*'
            input.onchange = () => {
              const file = input.files?.[0]
              if (file) void handleUpload(file)
            }
            input.click()
          }}
        >
          Attach
        </button>
        <button type="button" onClick={() => editor.chain().focus().undo().run()}>
          Undo
        </button>
        <button type="button" onClick={() => editor.chain().focus().redo().run()}>
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
      <div className="editor-stage">
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
        <EditorContent editor={editor} />
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
