/** Heuristic: note body is safer to edit as Markdown source than visually. */

/** Tags TipTap / Jotdex already round-trip well (incl. OneNote span noise). */
const SAFE_TAGS = new Set([
  'a',
  'b',
  'blockquote',
  'br',
  'code',
  'del',
  'em',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'hr',
  'i',
  'ins',
  'li',
  'mark',
  'ol',
  'p',
  's',
  'span',
  'strong',
  'sub',
  'sup',
  'table',
  'tbody',
  'td',
  'th',
  'thead',
  'tr',
  'u',
  'ul',
])

export function looksUnsafeForVisual(markdown: string): { unsafe: boolean; reason?: string } {
  if (!markdown) return { unsafe: false }

  if (/<\s*(script|iframe|object|embed|form|link|meta)\b/i.test(markdown)) {
    return { unsafe: true, reason: 'Contains HTML tags that should not be edited visually.' }
  }
  if (/\son[a-z]+\s*=/i.test(markdown)) {
    return { unsafe: true, reason: 'Contains HTML event handlers.' }
  }
  if (/javascript\s*:/i.test(markdown)) {
    return { unsafe: true, reason: 'Contains javascript: URLs.' }
  }

  // Count only tags TipTap is likely to mangle (layout divs, fonts, media, etc.).
  // OneNote exports wrap every line in <span style=…> — those are safe and must not trip this.
  const tags = markdown.match(/<\/?([a-z][\w:-]*)\b[^>]*>/gi) ?? []
  let risky = 0
  for (const tag of tags) {
    const name = /^<\/?([a-z][\w:-]*)/i.exec(tag)?.[1]?.toLowerCase()
    if (!name || SAFE_TAGS.has(name)) continue
    risky++
  }

  if (risky >= 12) {
    return {
      unsafe: true,
      reason: 'Complex raw HTML — opened in Source to avoid content loss.',
    }
  }

  return { unsafe: false }
}
