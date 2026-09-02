const BLOCKED = /^(javascript|data|vbscript|file|blob):/i

export function isSafeHref(href: string): boolean {
  const t = href.trim()
  if (!t) return false
  if (BLOCKED.test(t)) return false
  if (/^https?:\/\//i.test(t)) return true
  if (/^mailto:/i.test(t)) return true
  if (/^#/.test(t)) return true
  if (/^[a-z][a-z0-9+.-]*:/i.test(t)) return false
  return true
}

export function looksLikeBareUrl(text: string): boolean {
  const t = text.trim()
  return /^https?:\/\/\S+$/i.test(t)
}

export function hostnameLabel(href: string): string {
  try {
    return new URL(href).hostname.replace(/^www\./, '')
  } catch {
    return href
  }
}
