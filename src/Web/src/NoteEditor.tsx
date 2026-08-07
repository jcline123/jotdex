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
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import { CodeBlockView } from './CodeBlockView'
import { applyHeadingToSelection } from './headingSelection'

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

const CodeBlockBox = CodeBlockLowlight.extend({
  addNodeView() {
    return ReactNodeViewRenderer(CodeBlockView)
  },
}).configure({ lowlight, defaultLanguage: 'plaintext' })

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

type Props = {
  noteId: string
  noteStem: string
  markdown: string
  /** Bumps when parent applies an external markdown reload (open note / conflict / preserve-page). */
  contentEpoch?: number
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

export function NoteEditor({
  noteId,
  noteStem,
  markdown,
  contentEpoch = 0,
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
  const findInputRef = useRef<HTMLInputElement>(null)
  const findOpenRef = useRef(false)
  findOpenRef.current = findOpen
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
      if (result.note) onNoteMeta?.(result.note)

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

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ codeBlock: false }),
      CodeBlockBox,
      Link.configure({ openOnClick: false, autolink: true }),
      Image.configure({ allowBase64: false }),
      Placeholder.configure({ placeholder: 'Start writing…' }),
      TaskList,
      TaskItem.configure({ nested: true }),
      Table.configure({ resizable: true }),
      TableRow,
      TableHeader,
      TableCell,
      FontSizeTextStyle,
      Color,
      Markdown.configure({
        // Allow <span style="color/font-size"> so color & size round-trip in the vault.
        html: true,
        transformPastedText: true,
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

        // smart / keep: let TipTap handle HTML→doc; images already handled
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

  if (!editor) return null

  return (
    <div className="rich-editor">
      <div className="editor-toolbar" role="toolbar" aria-label="Formatting">
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
        <button type="button" className={editor.isActive('bold') ? 'on' : ''} onClick={() => editor.chain().focus().toggleBold().run()}>
          Bold
        </button>
        <button type="button" className={editor.isActive('italic') ? 'on' : ''} onClick={() => editor.chain().focus().toggleItalic().run()}>
          Italic
        </button>
        <button type="button" className={editor.isActive('code') ? 'on' : ''} onClick={() => editor.chain().focus().toggleCode().run()}>
          Code
        </button>
        <label className="toolbar-select" title="Text color for the selection">
          <span className="sr-only">Color</span>
          <select
            aria-label="Text color"
            value={editor.getAttributes('textStyle').color ?? ''}
            onChange={(e) => {
              const v = e.target.value
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
        <button type="button" className={editor.isActive('bulletList') ? 'on' : ''} onClick={() => editor.chain().focus().toggleBulletList().run()}>
          List
        </button>
        <button type="button" className={editor.isActive('orderedList') ? 'on' : ''} onClick={() => editor.chain().focus().toggleOrderedList().run()}>
          1.
        </button>
        <button type="button" className={editor.isActive('taskList') ? 'on' : ''} onClick={() => editor.chain().focus().toggleTaskList().run()}>
          Tasks
        </button>
        <button
          type="button"
          className={editor.isActive('codeBlock') ? 'on' : ''}
          title="Insert a code box (PowerShell, shell, etc.)"
          onClick={() => editor.chain().focus().toggleCodeBlock({ language: 'powershell' }).run()}
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
                ? 'Ctrl+V smart paste (default)'
                : id === 'plain'
                  ? 'Strip formatting (also Ctrl+Shift+V)'
                  : id === 'code'
                    ? 'Insert clipboard as a code block'
                    : id === 'preserve'
                      ? 'Save sanitized HTML sidecar and link it in the note'
                      : 'Prefer keeping HTML structure when possible'
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
      <EditorContent editor={editor} />
    </div>
  )
}
