import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection, highlightSpecialChars } from '@codemirror/view'
import { EditorState, Compartment, type Extension } from '@codemirror/state'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import {
  bracketMatching,
  indentOnInput,
  syntaxHighlighting,
  defaultHighlightStyle,
  foldGutter,
  foldKeymap,
} from '@codemirror/language'
import { searchKeymap, highlightSelectionMatches } from '@codemirror/search'
import { linter, lintGutter, type Diagnostic } from '@codemirror/lint'
import { autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap } from '@codemirror/autocomplete'
import { CODE_BLOCK_TAB_SIZE } from './codeBlockSettings'
import { jotdexCodeTheme } from './codeMirrorTheme'
import type { SnippetSummary } from './snippetApi'

export type CodeMirrorSetupOptions = {
  languageExt: Extension
  lintSource?: (text: string, signal: AbortSignal) => Promise<Diagnostic[]>
  snippets?: SnippetSummary[]
  wordWrap?: boolean
  showWhitespace?: boolean
  compact?: boolean
  onDocChange?: (text: string) => void
  onEscape?: () => void
}

export function createCodeMirrorCompartments() {
  return {
    wrap: new Compartment(),
    whitespace: new Compartment(),
  }
}

export function buildCodeMirrorExtensions(
  opts: CodeMirrorSetupOptions,
  compartments: ReturnType<typeof createCodeMirrorCompartments>,
): Extension[] {
  const snippets = opts.snippets ?? []
  const exts: Extension[] = [
    lineNumbers(),
    highlightActiveLine(),
    highlightActiveLineGutter(),
    drawSelection(),
    highlightSelectionMatches(),
    history(),
    foldGutter({ openText: '▾', closedText: '▸' }),
    indentOnInput(),
    bracketMatching(),
    closeBrackets(),
    syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
    jotdexCodeTheme,
    EditorState.tabSize.of(CODE_BLOCK_TAB_SIZE),
    compartments.wrap.of(opts.wordWrap ? EditorView.lineWrapping : []),
    compartments.whitespace.of(opts.showWhitespace ? highlightSpecialChars() : []),
    opts.languageExt,
    keymap.of([
      ...closeBracketsKeymap,
      ...defaultKeymap,
      ...historyKeymap,
      ...searchKeymap,
      ...foldKeymap,
      ...completionKeymap,
      indentWithTab,
    ]),
    autocompletion({
      activateOnTyping: false,
      override: [
        (context) => {
          const word = context.matchBefore(/[\w-]*/)
          if (!word && !context.explicit) return null
          const q = (word?.text ?? '').toLowerCase()
          const options = snippets
            .filter(
              (s) =>
                !q ||
                s.trigger.toLowerCase().includes(q) ||
                s.title.toLowerCase().includes(q) ||
                s.tags.some((t) => t.toLowerCase().includes(q)),
            )
            .slice(0, 15)
            .map((s) => ({
              label: s.trigger,
              detail: `${s.title} (${s.language})`,
              type: 'keyword' as const,
              apply: s.code,
            }))
          if (options.length === 0) return null
          return { from: word?.from ?? context.pos, options }
        },
      ],
    }),
  ]

  if (opts.lintSource) {
    const source = opts.lintSource
    exts.push(
      lintGutter(),
      linter(
        async (view) => {
          const controller = new AbortController()
          try {
            return await source(view.state.doc.toString(), controller.signal)
          } catch (err) {
            if ((err as Error).name === 'AbortError') return []
            throw err
          }
        },
        { delay: opts.compact ? 600 : 450 },
      ),
    )
  }

  if (opts.onDocChange || opts.onEscape) {
    exts.push(
      EditorView.updateListener.of((update) => {
        if (update.docChanged && opts.onDocChange) {
          opts.onDocChange(update.state.doc.toString())
        }
      }),
      EditorView.domEventHandlers({
        keydown(event) {
          if (opts.onEscape && event.key === 'Escape' && !event.shiftKey && !event.ctrlKey && !event.metaKey) {
            event.preventDefault()
            opts.onEscape?.()
            return true
          }
          return false
        },
      }),
    )
  }

  return exts
}
