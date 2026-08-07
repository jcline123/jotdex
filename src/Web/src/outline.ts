export type OutlineItem = { level: number; text: string }

/** ATX headings from markdown body (ignores front matter if still present). */
export function extractOutline(markdown: string): OutlineItem[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  const items: OutlineItem[] = []
  for (const line of lines) {
    const m = /^(#{1,6})\s+(.+?)\s*$/.exec(line)
    if (!m) continue
    items.push({ level: m[1].length, text: m[2].replace(/#+\s*$/, '').trim() })
  }
  return items
}
