import { NodeViewContent, NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { lazy, Suspense, useCallback, useRef, useState } from 'react'
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

  const openEditor = () => {
    if (!editor.isEditable) return
    editor.setEditable(false)
    setEditing(true)
  }

  const closeEditor = useCallback(() => {
    editor.setEditable(true)
    setEditing(false)
  }, [editor])

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

  return (
    <NodeViewWrapper className="code-block-box" data-language={language}>
      <div className="code-block-chrome" contentEditable={false}>
        <label className="code-lang">
          <span className="sr-only">Language</span>
          <select
            value={CODE_LANGUAGES.some((l) => l.id === language) ? language : 'plaintext'}
            disabled={!editor.isEditable || editing}
            onChange={(e) => updateAttributes({ language: e.target.value })}
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
        <div className="code-block-actions">
          {editor.isEditable && (
            <>
              <div className="code-block-snippet-group" role="group" aria-label="Snippets">
                <span className="code-snippet-label">Snippets</span>
                <button
                  type="button"
                  className="code-edit-btn"
                  onClick={openInsert}
                  title="Insert a saved snippet into this code box"
                >
                  Insert
                </button>
                <button
                  type="button"
                  className="code-edit-btn"
                  onClick={() => setSaveOpen(true)}
                  title="Save this code as a reusable snippet note in your vault"
                >
                  <span className="code-btn-label-full">Save as snippet</span>
                  <span className="code-btn-label-short">Save</span>
                </button>
              </div>
              <button type="button" className="code-edit-btn" onClick={openEditor} title="Advanced edit">
                Edit
              </button>
            </>
          )}
          <button type="button" className="code-copy-btn" onClick={() => void copy()} title="Copy code">
            {copied ? 'Copied' : 'Copy'}
          </button>
        </div>
      </div>
      <pre className="code-block-pre" spellCheck={false}>
        <NodeViewContent as={'code' as 'div'} className={`hljs language-${language}`} />
      </pre>

      {editing && (
        <Suspense fallback={<div className="modal-backdrop code-editor-backdrop" role="presentation" />}>
          <CodeEditorDialog
            language={language}
            initialText={node.textContent}
            onSync={handleSync}
            onClose={closeEditor}
          />
        </Suspense>
      )}

      {saveOpen && (
        <Suspense fallback={null}>
          <SaveAsSnippetModal
            language={language}
            code={node.textContent}
            onClose={() => setSaveOpen(false)}
          />
        </Suspense>
      )}

      {insertOpen && (
        <Suspense fallback={null}>
          <InsertSnippetModal language={language} onClose={() => setInsertOpen(false)} onPick={applySnippet} />
        </Suspense>
      )}
    </NodeViewWrapper>
  )
}
