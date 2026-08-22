import { EditorView } from '@codemirror/view'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { tags as t } from '@lezer/highlight'

/** Dark theme aligned with Jotdex inline code blocks. */
export const jotdexCodeTheme = EditorView.theme(
  {
    '&': {
      color: '#e6edf5',
      backgroundColor: '#12161c',
    },
    '.cm-content': {
      fontFamily: "ui-monospace, 'Cascadia Code', 'Consolas', monospace",
      fontSize: '0.86rem',
      lineHeight: 1.45,
      caretColor: '#e6edf5',
    },
    '.cm-gutters': {
      backgroundColor: '#0f1318',
      color: '#6b7a8c',
      borderRight: '1px solid rgba(255,255,255,0.08)',
    },
    '.cm-activeLine': { backgroundColor: 'rgba(255,255,255,0.04)' },
    '.cm-activeLineGutter': { backgroundColor: 'rgba(255,255,255,0.06)' },
    '.cm-selectionBackground, &.cm-focused .cm-selectionBackground': {
      backgroundColor: 'rgba(88, 166, 255, 0.28) !important',
    },
    '.cm-cursor': { borderLeftColor: '#e6edf5' },
    '.cm-lintRange-error': { backgroundImage: 'none', borderBottom: '2px wavy #f85149' },
    '.cm-lintRange-warning': { backgroundImage: 'none', borderBottom: '2px wavy #d29922' },
    '.cm-lintRange-info': { backgroundImage: 'none', borderBottom: '2px wavy #8b949e' },
    '.cm-lint-marker-error': { content: '"●"' },
  },
  { dark: true },
)

export const jotdexHighlightStyle = HighlightStyle.define([
  { tag: t.keyword, color: '#ff7b72' },
  { tag: [t.string, t.special(t.string)], color: '#a5d6ff' },
  { tag: t.comment, color: '#8b949e', fontStyle: 'italic' },
  { tag: [t.variableName, t.propertyName], color: '#e6edf5' },
  { tag: [t.function(t.variableName), t.labelName], color: '#d2a8ff' },
  { tag: t.number, color: '#e0b35a' },
])

export const jotdexSyntaxHighlight = syntaxHighlighting(jotdexHighlightStyle)
