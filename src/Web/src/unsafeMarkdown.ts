/** Heuristic: note body is safer to edit as Markdown source than visually. */
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

  // Dense raw HTML (common in web clips) — TipTap may lose structure
  const htmlTags = markdown.match(/<\/?[a-z][\w:-]*\b[^>]*>/gi) ?? []
  if (htmlTags.length >= 12) {
    return { unsafe: true, reason: 'Dense raw HTML — opened in Source to avoid content loss.' }
  }

  return { unsafe: false }
}
