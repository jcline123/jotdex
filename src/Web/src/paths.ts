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

export function encodeMdPath(path: string): string {
  return path
    .split('/')
    .map((seg) => (seg === '.' || seg === '..' ? seg : encodeURIComponent(seg).replace(/%20/g, '%20')))
    .join('/')
}
