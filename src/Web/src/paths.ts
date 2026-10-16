/** Build a relative markdown path from one note file to another. */
export function relativeMdPath(fromNoteRelativePath: string, toNoteRelativePath: string): string {
  const fromParts = fromNoteRelativePath.replace(/\\/g, '/').split('/')
  fromParts.pop()
  const toParts = toNoteRelativePath.replace(/\\/g, '/').split('/')

  let i = 0
  while (i < fromParts.length && i < toParts.length - 1 && fromParts[i] === toParts[i]) i++

  const ups = fromParts.length - i
  const down = toParts.slice(i)
  const segs = [...Array(ups).fill('..'), ...down]
  const rel = segs.join('/') || toParts[toParts.length - 1]
  return encodeMdPath(rel.startsWith('.') ? rel : `./${rel}`)
}

export type NoteLinkTarget = {
  id: string
  title: string
  relativePath: string
  folderPath?: string
}

/** Resolve a markdown link href to a note in the catalog. External and in-page hashes return null. */
export function resolveNoteLink(
  fromNoteRelativePath: string,
  href: string,
  catalog: NoteLinkTarget[],
): NoteLinkTarget | null {
  const raw = href.trim()
  if (!raw || raw.startsWith('#')) return null
  if (/^[a-z][a-z0-9+.-]*:/i.test(raw)) return null

  let path = raw
  const hash = path.indexOf('#')
  if (hash >= 0) path = path.slice(0, hash)
  const query = path.indexOf('?')
  if (query >= 0) path = path.slice(0, query)
  try {
    path = decodeURIComponent(path)
  } catch {
    /* keep the raw path */
  }
  path = path.replace(/\\/g, '/')
  if (!path || path.includes('.assets/')) return null

  const stack = fromNoteRelativePath.replace(/\\/g, '/').split('/').slice(0, -1).filter(Boolean)
  for (const seg of path.split('/')) {
    if (!seg || seg === '.') continue
    if (seg === '..') stack.pop()
    else stack.push(seg)
  }
  let rel = stack.join('/')
  if (!rel.toLowerCase().endsWith('.md')) rel += '.md'
  const hit = catalog.find((n) => n.relativePath.replace(/\\/g, '/').toLowerCase() === rel.toLowerCase())
  return hit ?? null
}

/** Resolve [[Title]] / [[Title|label]] / [[Title#heading]] to one note. */
export function resolveWikiTarget(
  fromNoteRelativePath: string,
  target: string,
  catalog: NoteLinkTarget[],
): NoteLinkTarget | null {
  let title = target.split('|')[0]?.trim() ?? ''
  const hash = title.indexOf('#')
  if (hash >= 0) title = title.slice(0, hash).trim()
  if (!title) return null

  const byPath = resolveNoteLink(fromNoteRelativePath, title.endsWith('.md') ? title : `${title}.md`, catalog)
  if (byPath) return byPath

  const wanted = title.toLowerCase()
  const byTitle = catalog.filter((n) => n.title.trim().toLowerCase() === wanted)
  const byFile = catalog.filter((n) => {
    const file = n.relativePath.replace(/\\/g, '/').split('/').pop() ?? ''
    return file.replace(/\.md$/i, '').toLowerCase() === wanted
  })
  const pool = byTitle.length > 0 ? byTitle : byFile
  if (pool.length === 0) return null
  if (pool.length === 1) return pool[0]

  const fromFolder = fromNoteRelativePath.replace(/\\/g, '/').split('/').slice(0, -1).join('/').toLowerCase()
  const sameFolder = pool.filter(
    (n) => (n.folderPath ?? '').replace(/\\/g, '/').toLowerCase() === fromFolder,
  )
  if (sameFolder.length === 1) return sameFolder[0]
  return null
}

export function encodeMdPath(path: string): string {
  return path
    .split('/')
    .map((seg) => (seg === '.' || seg === '..' ? seg : encodeURIComponent(seg).replace(/%20/g, '%20')))
    .join('/')
}
