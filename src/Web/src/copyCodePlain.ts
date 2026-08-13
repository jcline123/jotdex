import type { EditorView } from '@tiptap/pm/view'

function codeBoxRoot(node: EventTarget | Node | null): Element | null {
  if (!node) return null
  const el = node instanceof Element ? node : (node as Node).parentElement
  return el?.closest('.code-block-box') ?? null
}

function selectionTextInCodeBox(): string | null {
  const sel = window.getSelection()
  if (!sel || sel.isCollapsed || sel.rangeCount === 0) return null
  const box = codeBoxRoot(sel.anchorNode) ?? codeBoxRoot(sel.focusNode)
  if (!box) return null
  if (!box.contains(sel.anchorNode) || !box.contains(sel.focusNode)) return null
  const text = sel.toString().replace(/\r\n/g, '\n')
  return text.length ? text : null
}

function writePlainOnly(event: ClipboardEvent, text: string): void {
  event.preventDefault()
  event.stopImmediatePropagation()
  const data = event.clipboardData
  if (data) {
    try {
      data.clearData()
    } catch {
      /* some browsers throw */
    }
    data.setData('text/plain', text)
    // Characters only — no tags. Chrome still wraps HTML with StartFragment;
    // paste treats fragment-only HTML as plain (see htmlIsPlainClipboardSnippet).
    data.setData('text/html', text)
  }
  if (typeof navigator.clipboard?.writeText === 'function') {
    void navigator.clipboard.writeText(text).catch(() => {
      /* http LAN / permission */
    })
  }
}

function looksLikeEscapedHtmlSnippet(s: string): boolean {
  return /&lt;!--StartFragment--&gt;|&lt;span[\s&]|&lt;\/span&gt;/i.test(s)
}

function decodeBasicEntities(s: string): string {
  return s
    .replace(/&nbsp;/gi, ' ')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&amp;/gi, '&')
    .replace(/&quot;/gi, '"')
}

function stripClipboardHtmlToText(source: string): string {
  const decoded = looksLikeEscapedHtmlSnippet(source) ? decodeBasicEntities(source) : source
  const inner = decoded
    .replace(/<!--StartFragment-->/gi, '')
    .replace(/<!--EndFragment-->/gi, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<\/?(html|body|head|meta|span|font|pre|code)[^>]*>/gi, '')
  return decodeBasicEntities(inner).replace(/\u00a0/g, ' ').trim()
}

/** Word/Chrome HTML wrapper around a text snippet — not a real rich document. */
export function htmlIsPlainClipboardSnippet(html: string | null | undefined): boolean {
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

/** If the "plain" clipboard is actually HTML (Chrome contenteditable copy), peel it to text. */
export function plainTextFromClipboard(plain: string, html: string): string {
  if (htmlIsPlainClipboardSnippet(html)) return stripClipboardHtmlToText(html)
  if (htmlIsPlainClipboardSnippet(plain) || looksLikeEscapedHtmlSnippet(plain)) {
    return stripClipboardHtmlToText(plain)
  }
  return plain
}

export function copyPlainTextFromCodeBox(event: ClipboardEvent): boolean {
  const text = selectionTextInCodeBox()
  if (!text) return false
  writePlainOnly(event, text)
  return true
}

export function deleteDomSelection(view: EditorView): void {
  const sel = window.getSelection()
  if (!sel || sel.isCollapsed || sel.rangeCount === 0) return
  try {
    const range = sel.getRangeAt(0)
    const from = view.posAtDOM(range.startContainer, range.startOffset)
    const to = view.posAtDOM(range.endContainer, range.endOffset)
    if (from === to) return
    view.dispatch(view.state.tr.delete(Math.min(from, to), Math.max(from, to)).scrollIntoView())
  } catch {
    /* node view DOM can throw */
  }
}

/** Capture-phase listeners beat nested contenteditable / ProseMirror copy. */
export function installCodeBoxClipboardGuards(view: EditorView): () => void {
  const onCopy = (event: ClipboardEvent) => {
    copyPlainTextFromCodeBox(event)
  }
  const onCut = (event: ClipboardEvent) => {
    if (!copyPlainTextFromCodeBox(event)) return
    if (view.editable) deleteDomSelection(view)
  }
  document.addEventListener('copy', onCopy, true)
  document.addEventListener('cut', onCut, true)
  return () => {
    document.removeEventListener('copy', onCopy, true)
    document.removeEventListener('cut', onCut, true)
  }
}
