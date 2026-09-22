import { NodeViewContent, NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { Component, lazy, Suspense, useCallback, useEffect, useRef, useState, type ErrorInfo, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { CodeBlockMoreMenu } from './CodeBlockMoreMenu'
import { codeBlockInsertOffset, insertCodeBlockText, syncCodeBlockText } from './syncCodeBlock'
import type { SnippetSummary } from './snippetApi'

const CodeEditorDialog = lazy(() =>
  import('./CodeEditorDialog').then((m) => ({ default: m.CodeEditorDialog })),
)
const SaveAsSnippetModal = lazy(() =>
  import('./SaveAsSnippetModal').then((m) => ({ default: m.SaveAsSnippetModal })),
)
const InsertSnippetModal = lazy(() =>
  import('./InsertSnippetModal').then((m) => ({ default: m.InsertSnippetModal })),
)

export const CODE_LANGUAGES = [
  { id: 'plaintext', label: 'Plain text' },
  { id: 'powershell', label: 'PowerShell' },
  { id: 'bash', label: 'Bash / shell' },
  { id: 'cmd', label: 'CMD' },
  { id: 'csharp', label: 'C#' },
  { id: 'javascript', label: 'JavaScript' },
  { id: 'typescript', label: 'TypeScript' },
  { id: 'json', label: 'JSON' },
  { id: 'sql', label: 'SQL' },
  { id: 'python', label: 'Python' },
  { id: 'yaml', label: 'YAML' },
  { id: 'xml', label: 'XML / HTML' },
] as const

/** Catches lazy-load / render failures so Edit never blanks the whole app. */
class CodeEditorErrorBoundary extends Component<
  { onClose: () => void; children: ReactNode },
  { error: string | null }
> {
  state: { error: string | null } = { error: null }

  static getDerivedStateFromError(err: Error) {
    return { error: err.message || 'Code editor failed to open' }
  }

  componentDidCatch(err: Error, info: ErrorInfo) {
    console.error('Code editor failed', err, info.componentStack)
  }

  render() {
    if (this.state.error) {
      return createPortal(
        <div className="modal-backdrop code-editor-backdrop" role="presentation" onClick={this.props.onClose}>
          <div
            className="modal code-editor-modal"
            role="dialog"
            aria-modal="true"
            aria-label="Code editor error"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-head">
              <h2>Could not open code editor</h2>
              <button type="button" className="ghost" onClick={this.props.onClose}>
                Close
              </button>
            </div>
            <p className="banner error">{this.state.error}</p>
            <p className="muted">Try refreshing the page. If it keeps happening, hard-reload to pick up the latest assets.</p>
          </div>
        </div>,
        document.body,
      )
    }
    return this.props.children
  }
}

function CodeEditorLoadingFallback({ onCancel }: { onCancel: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  return (
    <div className="modal-backdrop code-editor-backdrop" role="presentation" onClick={onCancel}>
      <div
        className="modal code-editor-modal code-editor-loading"
        role="dialog"
        aria-modal="true"
        aria-busy="true"
        aria-label="Loading code editor"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2>Loading code editor…</h2>
          <button type="button" className="ghost" onClick={onCancel}>
            Cancel
          </button>
        </div>
        <p className="muted">CodeMirror is loading. Escape or Cancel to go back.</p>
      </div>
    </div>
  )
}

export function CodeBlockView({ node, updateAttributes, editor, getPos }: NodeViewProps) {
  const [copied, setCopied] = useState(false)
  const [editing, setEditing] = useState(false)
  const [saveOpen, setSaveOpen] = useState(false)
  const [insertOpen, setInsertOpen] = useState(false)
  const insertOffsetRef = useRef(0)
  const language = (node.attrs.language as string) || 'plaintext'

  const copy = async () => {
    const text = node.textContent
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1400)
    } catch {
      const ta = document.createElement('textarea')
      ta.value = text
      ta.style.position = 'fixed'
      ta.style.left = '-9999px'
      document.body.appendChild(ta)
      ta.select()
      document.execCommand('copy')
      document.body.removeChild(ta)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1400)
    }
  }

  // Backdrop blocks the note; avoid setEditable(false) so TipTap does not remount this node view
  // (which would wipe editing state and leave a stuck dark overlay).
  const openEditor = () => {
    if (!editor.isEditable) return
    setEditing(true)
  }

  const closeEditor = useCallback(() => {
    setEditing(false)
  }, [])

  const handleSync = useCallback(
    (text: string) => {
      const pos = getPos()
      if (typeof pos !== 'number') return false
      return syncCodeBlockText(editor, pos, text)
    },
    [editor, getPos],
  )

  const openInsert = useCallback(() => {
    const pos = getPos()
    if (typeof pos === 'number') {
      insertOffsetRef.current = codeBlockInsertOffset(editor, pos, node.textContent.length)
    } else {
      insertOffsetRef.current = node.textContent.length
    }
    setInsertOpen(true)
  }, [editor, getPos, node.textContent.length])

  const applySnippet = useCallback(
    (snippet: SnippetSummary) => {
      const pos = getPos()
      if (typeof pos !== 'number') return
      insertCodeBlockText(editor, pos, insertOffsetRef.current, snippet.code)
      if (snippet.language) updateAttributes({ language: snippet.language })
      setInsertOpen(false)
    },
    [editor, getPos, updateAttributes],
  )

  const editorPortal =
    editing &&
    createPortal(
      <CodeEditorErrorBoundary onClose={closeEditor}>
        <Suspense fallback={<CodeEditorLoadingFallback onCancel={closeEditor} />}>
          <CodeEditorDialog
            language={language}
            initialText={node.textContent}
            onSync={handleSync}
            onClose={closeEditor}
          />
        </Suspense>
      </CodeEditorErrorBoundary>,
      document.body,
    )

  return (
    <NodeViewWrapper className="code-block-box" data-language={language}>
      <div className="code-block-chrome" contentEditable={false}>
        <div className="code-block-chrome-start">
          {editor.isEditable && (
            <span
              className="code-block-drag"
              data-drag-handle=""
              title="Drag to move this code box"
              aria-label="Drag to move this code box"
            >
              ⋮⋮
            </span>
          )}
          <label className="code-lang">
            <span className="sr-only">Language</span>
            <select
              value={CODE_LANGUAGES.some((l) => l.id === language) ? language : 'plaintext'}
              disabled={!editor.isEditable || editing}
              onChange={(e) => updateAttributes({ language: e.target.value })}
              onPointerDown={(e) => e.stopPropagation()}
              aria-label="Code language"
            >
              {CODE_LANGUAGES.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.label}
                </option>
              ))}
              {!CODE_LANGUAGES.some((l) => l.id === language) && language !== 'plaintext' && (
                <option value={language}>{language}</option>
              )}
            </select>
          </label>
        </div>
        <div className="code-block-actions" onPointerDown={(e) => e.stopPropagation()}>
          {editor.isEditable && (
            <button type="button" className="code-chrome-btn" onClick={openEditor} title="Advanced edit">
              Edit
            </button>
          )}
          <button type="button" className="code-chrome-btn code-copy-btn" onClick={() => void copy()} title="Copy code">
            {copied ? 'Copied' : 'Copy'}
          </button>
          {editor.isEditable && (
            <CodeBlockMoreMenu onInsertSnippet={openInsert} onSaveSnippet={() => setSaveOpen(true)} />
          )}
        </div>
      </div>
      <pre className="code-block-pre" spellCheck={false}>
        <NodeViewContent as={'code' as 'div'} className={`hljs language-${language}`} />
      </pre>

      {editorPortal}

      {saveOpen &&
        createPortal(
          <Suspense fallback={null}>
            <SaveAsSnippetModal
              language={language}
              code={node.textContent}
              onClose={() => setSaveOpen(false)}
            />
          </Suspense>,
          document.body,
        )}

      {insertOpen &&
        createPortal(
          <Suspense fallback={null}>
            <InsertSnippetModal language={language} onClose={() => setInsertOpen(false)} onPick={applySnippet} />
          </Suspense>,
          document.body,
        )}
    </NodeViewWrapper>
  )
}
