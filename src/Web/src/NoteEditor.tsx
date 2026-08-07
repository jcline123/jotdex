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
import { CodeBlockView } from './CodeBlockView'

const lowlight = createLowlight(common)
lowlight.register('powershell', powershell)
lowlight.register('ps1', powershell)
lowlight.register('pwsh', powershell)
lowlight.register('cmd', dos)
lowlight.register('bat', dos)

const CodeBlockBox = CodeBlockLowlight.extend({
  addNodeView() {
    return ReactNodeViewRenderer(CodeBlockView)
  },
}).configure({ lowlight, defaultLanguage: 'plaintext' })

export type PasteMode = 'smart' | 'plain' | 'code' | 'keep' | 'preserve'

type AttachmentInfo = { id: string; fileName: string; contentType: string }

type Props = {
  noteId: string
  noteStem: string
  markdown: string
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
  return value.replace(/ /g, '%20')
}

function mdPathFor(stem: string, fileName: string) {
  return `${encodeSpaces(stem)}.assets/${encodeSpaces(fileName)}`
}

function markdownForEditor(markdown: string, attachments: AttachmentInfo[]): string {
  if (!attachments.length) return markdown
  let result = markdown
  for (const att of attachments) {
    const api = `/api/attachments/${att.id}`
    const suffixes = [
      `.assets/${att.fileName}`,
      `.assets/${encodeSpaces(att.fileName)}`,
      `.assets/${encodeURIComponent(att.fileName)}`,
    ]
    for (const suffix of suffixes) {
      const re = new RegExp(`\\(([^)\\s]*${escapeRegExp(suffix)})\\)`, 'gi')
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
  const pasteModeRef = useRef<PasteMode>('smart')
  const attachmentsRef = useRef(attachments)
  const stemRef = useRef(noteStem)
  const noteIdRef = useRef(noteId)
  attachmentsRef.current = attachments
  stemRef.current = noteStem
  noteIdRef.current = noteId
  pasteModeRef.current = pasteMode

  const emitMarkdown = (ed: Editor) => {
    onChange(toVaultMarkdown(ed, attachmentsRef.current, apiToMd.current, stemRef.current))
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
      Markdown.configure({
        html: false,
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
        const files = event.dataTransfer?.files
        if (!files?.length) return false
        event.preventDefault()
        void (async () => {
          for (const file of Array.from(files)) {
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

  useEffect(() => {
    if (!editor) return
    readyRef.current = false
    const display = markdownForEditor(markdown, attachments)
    const currentVault = toVaultMarkdown(editor, attachments, apiToMd.current, noteStem)
    if (currentVault.trim() !== markdown.trim()) {
      editor.commands.setContent(display, { emitUpdate: false })
    }
    window.setTimeout(() => {
      readyRef.current = true
    }, 0)
  }, [markdown, attachments, noteStem, editor])

  useEffect(() => {
    if (!editor) return
    editor.setEditable(editable)
  }, [editable, editor])

  if (!editor) return null

  return (
    <div className="rich-editor">
      <div className="editor-toolbar" role="toolbar" aria-label="Formatting">
        <button type="button" className={editor.isActive('heading', { level: 1 }) ? 'on' : ''} onClick={() => editor.chain().focus().toggleHeading({ level: 1 }).run()}>
          H1
        </button>
        <button type="button" className={editor.isActive('heading', { level: 2 }) ? 'on' : ''} onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}>
          H2
        </button>
        <button type="button" className={editor.isActive('heading', { level: 3 }) ? 'on' : ''} onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}>
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
