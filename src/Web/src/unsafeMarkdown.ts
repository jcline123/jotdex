/** Heuristic: note body is safer to edit as Markdown source than visually. */

/** Closed fenced blocks — email HTML dumps live here and must not force Source. */
function markdownOutsideFences(markdown: string): string {
  const normalized = markdown.replace(/\r\n/g, '\n')
  return normalized.replace(/^(```|~~~)[^\n]*\n[\s\S]*?^\1[ \t]*$/gm, '\n')
}

export function looksUnsafeForVisual(markdown: string): { unsafe: boolean; reason?: string } {
  if (!markdown) return { unsafe: false }

  const body = markdownOutsideFences(markdown)

  if (/<\s*(script|iframe|object|embed|form)\b/i.test(body)) {
    return { unsafe: true, reason: 'Contains HTML tags that should not be edited visually.' }
  }
  if (/\son[a-z]+\s*=/i.test(body)) {
    return { unsafe: true, reason: 'Contains HTML event handlers.' }
  }
  if (/javascript\s*:/i.test(body)) {
    return { unsafe: true, reason: 'Contains javascript: URLs.' }
  }

  return { unsafe: false }
}
