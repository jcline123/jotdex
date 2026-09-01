import type { Editor } from '@tiptap/core'
import { setOperationMeta, newOperationId } from '../operations/operationMeta'
import { looksLikeEscapedHtmlSnippet } from '../../copyCodePlain'

function decodeBasicEntities(s: string): string {
  return s
    .replace(/&nbsp;/gi, ' ')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&amp;/gi, '&')
    .replace(/&quot;/gi, '"')
}

function normalizeLf(text: string): string {
  return text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
}

/** Wrapper detection only — must not be used to trim paste payloads. */
export function htmlLooksLikePlainSnippet(html: string | null | undefined): boolean {
  if (!html) return false
  const decoded = looksLikeEscapedHtmlSnippet(html) ? decodeBasicEntities(html) : html
  const inner = decoded
    .replace(/<!--StartFragment-->/gi, '')
    .replace(/<!--EndFragment-->/gi, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<\/?(html|body|head|meta|span|font|pre|code)[^>]*>/gi, '')
    .replace(/&nbsp;/gi, ' ')
    .trim()
  return inner.length > 0 && !/<[a-z][\s\S]*>/i.test(inner)
}

function stripWrappersPreserveWs(source: string): string {
  const decoded = looksLikeEscapedHtmlSnippet(source) ? decodeBasicEntities(source) : source
  return decodeBasicEntities(
    decoded
      .replace(/<!--StartFragment-->/gi, '')
      .replace(/<!--EndFragment-->/gi, '')
      .replace(/<!--[\s\S]*?-->/g, '')
      .replace(/<\/?(html|body|head|meta|span|font|pre|code)[^>]*>/gi, ''),
  ).replace(/\u00a0/g, ' ')
}

/**
 * Clipboard → exact code text. Prefer text/plain (including whitespace-only).
 * Normalize only CRLF/CR → LF. Never trim.
 */
export function clipboardToPlainCode(rawPlain: string, html: string): string {
  if (rawPlain.length > 0) return normalizeLf(rawPlain)
  if (!html) return ''
  if (htmlLooksLikePlainSnippet(html) || looksLikeEscapedHtmlSnippet(html)) {
    return normalizeLf(stripWrappersPreserveWs(html))
  }
  const doc = new DOMParser().parseFromString(html, 'text/html')
  return normalizeLf((doc.body.textContent ?? '').replace(/\u00a0/g, ' '))
}

export function isSelectionInCodeBlock(editor: Editor): boolean {
  return editor.isActive('codeBlock')
}

export function pastePlainIntoCodeBlock(editor: Editor, text: string): boolean {
  if (!isSelectionInCodeBlock(editor)) return false
  const normalized = normalizeLf(text)
  const { from, to } = editor.state.selection
  const tr = editor.state.tr.insertText(normalized, from, to).scrollIntoView()
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'paste-code',
    serializable: true,
    commitBoundary: true,
  })
  editor.view.dispatch(tr)
  return true
}

/** Replace the current selection with exactly one code block. Does not trim. */
export function pasteAsCodeBlock(editor: Editor, text: string, language = 'powershell'): boolean {
  const normalized = normalizeLf(text)
  const { from, to } = editor.state.selection
  const node = editor.schema.nodes.codeBlock?.create(
    { language },
    normalized ? editor.schema.text(normalized) : undefined,
  )
  if (!node) return false
  const tr = editor.state.tr.replaceWith(from, to, node).scrollIntoView()
  setOperationMeta(tr, {
    operationId: newOperationId(),
    kind: 'paste-code',
    serializable: true,
    commitBoundary: true,
  })
  editor.view.dispatch(tr)
  return true
}
