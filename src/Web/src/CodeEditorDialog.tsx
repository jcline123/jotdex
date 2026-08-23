import { lazy, Suspense, useCallback, useEffect, useId, useRef, useState } from 'react'
import { EditorView, highlightSpecialChars } from '@codemirror/view'
import { EditorState } from '@codemirror/state'
import { languageLabel, loadCodeMirrorLanguage, normalizeLanguageId } from './codeLanguages'
import { asyncDiagnosticsForLanguage, toCodeMirrorDiagnostics, type CodeDiagnostic } from './codeDiagnostics'
import { buildCodeMirrorExtensions, createCodeMirrorCompartments } from './codeMirrorSetup'
import { fetchSnippets, type SnippetSummary } from './snippetApi'

const SaveAsSnippetModal = lazy(() =>
  import('./SaveAsSnippetModal').then((m) => ({ default: m.SaveAsSnippetModal })),
)
const InsertSnippetModal = lazy(() =>
  import('./InsertSnippetModal').then((m) => ({ default: m.InsertSnippetModal })),
)

export type CodeEditorDialogProps = {
  language: string
  initialText: string
  /** Returns false when the target code block no longer exists. */
  onSync: (text: string) => boolean
  onClose: () => void
}

const SYNC_DEBOUNCE_MS = 150

export function CodeEditorDialog({ language, initialText, onSync, onClose }: CodeEditorDialogProps) {
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const compartmentsRef = useRef(createCodeMirrorCompartments())
  const syncTimer = useRef<number | null>(null)
  const pendingText = useRef(initialText)
  const lintAbort = useRef<AbortController | null>(null)
  const lintGeneration = useRef(0)
  const onSyncRef = useRef(onSync)
  const onCloseRef = useRef(onClose)
  const titleId = useId()

  const [wordWrap, setWordWrap] = useState(false)
  const [showWhitespace, setShowWhitespace] = useState(false)
  const [staleError, setStaleError] = useState<string | null>(null)
  const [panelDiagnostics, setPanelDiagnostics] = useState<CodeDiagnostic[]>([])
  const [lintLabel, setLintLabel] = useState<string | null>(
    normalizeLanguageId(language) === 'powershell' ? 'PowerShell syntax' : null,
  )
  const [saveOpen, setSaveOpen] = useState(false)
  const [insertOpen, setInsertOpen] = useState(false)

  onSyncRef.current = onSync
  onCloseRef.current = onClose

  const currentText = useCallback(() => viewRef.current?.state.doc.toString() ?? pendingText.current, [])

  const flushSync = useCallback((text: string) => {
    pendingText.current = text
    if (!onSyncRef.current(text)) {
      setStaleError('This code block changed elsewhere. Close without saving further edits.')
    }
  }, [])

  const scheduleSync = useCallback(
    (text: string) => {
      if (syncTimer.current) window.clearTimeout(syncTimer.current)
      syncTimer.current = window.setTimeout(() => flushSync(text), SYNC_DEBOUNCE_MS)
    },
    [flushSync],
  )

  const insertSnippetAtCursor = useCallback(
    (snippet: SnippetSummary) => {
      const view = viewRef.current
      if (!view) return
      const pos = view.state.selection.main.head
      view.dispatch({
        changes: { from: pos, to: pos, insert: snippet.code },
        selection: { anchor: pos + snippet.code.length },
      })
      const text = view.state.doc.toString()
      pendingText.current = text
      scheduleSync(text)
      setInsertOpen(false)
    },
    [scheduleSync],
  )

  const handleClose = useCallback(() => {
    if (syncTimer.current) {
      window.clearTimeout(syncTimer.current)
      syncTimer.current = null
    }
    flushSync(pendingText.current)
    lintAbort.current?.abort()
    viewRef.current?.destroy()
    viewRef.current = null
    onCloseRef.current()
  }, [flushSync])

  const handleCloseRef = useRef(handleClose)
  handleCloseRef.current = handleClose

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    let cancelled = false
    const langId = normalizeLanguageId(language)

    void (async () => {
      const langExt = await loadCodeMirrorLanguage(language)
      let snippets: Awaited<ReturnType<typeof fetchSnippets>> = []
      try {
        snippets = await fetchSnippets('', langId === 'plaintext' ? undefined : langId)
      } catch {
        snippets = []
      }
      if (cancelled || !hostRef.current) return

      const state = EditorState.create({
        doc: initialText,
        extensions: buildCodeMirrorExtensions(
          {
            languageExt: langExt,
            snippets,
            wordWrap,
            showWhitespace,
            onDocChange: (text) => {
              pendingText.current = text
              scheduleSync(text)
            },
            onEscape: () => handleCloseRef.current(),
            lintSource: async (doc, signal) => {
              lintAbort.current?.abort()
              const controller = new AbortController()
              lintAbort.current = controller
              signal.addEventListener('abort', () => controller.abort())
              const gen = ++lintGeneration.current
              const { diagnostics: items, hint } = await asyncDiagnosticsForLanguage(langId, doc, controller.signal)
              if (gen !== lintGeneration.current) return []
              setPanelDiagnostics(items)
              if (langId === 'powershell') setLintLabel(hint ?? 'PowerShell syntax (parse-only; does not run code)')
              return toCodeMirrorDiagnostics(doc, items)
            },
          },
          compartmentsRef.current,
        ),
      })

      const view = new EditorView({ state, parent: host })
      viewRef.current = view
      view.focus()
    })()

    return () => {
      cancelled = true
      lintAbort.current?.abort()
      if (syncTimer.current) {
        window.clearTimeout(syncTimer.current)
        syncTimer.current = null
      }
      viewRef.current?.destroy()
      viewRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const view = viewRef.current
    if (!view) return
    view.dispatch({
      effects: compartmentsRef.current.wrap.reconfigure(wordWrap ? EditorView.lineWrapping : []),
    })
  }, [wordWrap])

  useEffect(() => {
    const view = viewRef.current
    if (!view) return
    view.dispatch({
      effects: compartmentsRef.current.whitespace.reconfigure(showWhitespace ? highlightSpecialChars() : []),
    })
  }, [showWhitespace])

  const errors = panelDiagnostics.filter((d) => d.severity === 'error')
  const warnings = panelDiagnostics.filter((d) => d.severity === 'warning' || d.severity === 'style')

  return (
    <div className="modal-backdrop code-editor-backdrop" role="presentation" onClick={handleClose}>
      <div
        className="modal code-editor-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-head">
          <h2 id={titleId}>Edit code — {languageLabel(language)}</h2>
          <div className="code-editor-toolbar">
            <div className="code-block-snippet-group code-editor-snippet-group" role="group" aria-label="Snippets">
              <span className="code-snippet-label">Snippets</span>
              <button type="button" className="code-edit-btn" onClick={() => setInsertOpen(true)} title="Insert snippet at cursor">
                Insert
              </button>
              <button type="button" className="code-edit-btn" onClick={() => setSaveOpen(true)} title="Save this code as a snippet">
                <span className="code-btn-label-full">Save as snippet</span>
                <span className="code-btn-label-short">Save</span>
              </button>
            </div>
            <label className="code-editor-wrap-toggle">
              <input type="checkbox" checked={wordWrap} onChange={(e) => setWordWrap(e.target.checked)} />
              Word wrap
            </label>
            <label className="code-editor-wrap-toggle">
              <input type="checkbox" checked={showWhitespace} onChange={(e) => setShowWhitespace(e.target.checked)} />
              Show whitespace
            </label>
            <button type="button" className="ghost" onClick={handleClose}>
              Done
            </button>
          </div>
        </div>

        {staleError && <p className="banner error">{staleError}</p>}

        <div className="code-editor-host" ref={hostRef} aria-label="Code editor" />

        <div className="code-editor-diagnostics" aria-live="polite">
          <div className="code-editor-diagnostics-head">
            <strong>Diagnostics</strong>
            {lintLabel && <span className="muted">{lintLabel} (parse-only; does not run code)</span>}
          </div>
          {panelDiagnostics.length === 0 ? (
            <p className="muted">No issues reported.</p>
          ) : (
            <ul className="code-editor-diagnostics-list">
              {errors.map((d, i) => (
                <li key={`e-${i}`} className="diag-error">
                  <span className="diag-sev">Error</span>
                  L{d.startLine}:{d.startColumn} — {d.message}
                </li>
              ))}
              {warnings.map((d, i) => (
                <li key={`w-${i}`} className="diag-warn">
                  <span className="diag-sev">{d.severity === 'style' ? 'Style' : 'Warning'}</span>
                  L{d.startLine}:{d.startColumn} — {d.message}
                </li>
              ))}
            </ul>
          )}
        </div>

        <p className="muted code-editor-hint">
          Ctrl+F find · Tab indent · Shift+Tab outdent · Ctrl+Space snippet completions · Insert/Save snippets above ·
          Fold gutter (click ▸) · Escape or Done to close. Edits autosave into the note.
        </p>
      </div>

      {saveOpen && (
        <Suspense fallback={null}>
          <SaveAsSnippetModal
            language={language}
            code={currentText()}
            onClose={() => setSaveOpen(false)}
          />
        </Suspense>
      )}

      {insertOpen && (
        <Suspense fallback={null}>
          <InsertSnippetModal
            language={language}
            onClose={() => setInsertOpen(false)}
            onPick={insertSnippetAtCursor}
          />
        </Suspense>
      )}
    </div>
  )
}
