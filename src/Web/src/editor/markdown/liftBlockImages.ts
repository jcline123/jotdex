import type { JSONContent } from '@tiptap/core'

function meaningfulChildren(node: JSONContent): JSONContent[] {
  return (node.content ?? []).filter((c) => {
    if (c.type === 'text' && !String(c.text ?? '').trim()) return false
    return true
  })
}

function isStandaloneImageParagraph(node: JSONContent): boolean {
  if (node.type !== 'paragraph') return false
  const kids = meaningfulChildren(node)
  return kids.length === 1 && kids[0]?.type === 'image'
}

export function paragraphHasMixedImageAndProse(node: JSONContent): boolean {
  if (node.type !== 'paragraph') return false
  const kids = meaningfulChildren(node)
  const hasImage = kids.some((k) => k.type === 'image')
  const hasProse = kids.some((k) => k.type !== 'image')
  return hasImage && hasProse
}

/** Lift a paragraph whose only content is a block image. */
export function liftStandaloneImages(doc: JSONContent): { doc: JSONContent; changed: boolean } {
  const content = doc.content ?? []
  let changed = false
  const next = content.map((node) => {
    if (!isStandaloneImageParagraph(node)) return node
    changed = true
    return meaningfulChildren(node)[0]!
  })
  return { doc: { ...doc, content: next }, changed }
}

export function hasMixedInlineImage(doc: JSONContent): boolean {
  return (doc.content ?? []).some(paragraphHasMixedImageAndProse)
}

/** Drop a single trailing empty paragraph that official parse often appends after headings/images. */
export function trimTrailingEmptyParagraph(doc: JSONContent): { doc: JSONContent; changed: boolean } {
  let content = [...(doc.content ?? [])]
  let changed = false
  while (content.length > 1) {
    const last = content[content.length - 1]
    const empty =
      last?.type === 'paragraph' &&
      (!(last.content ?? []).length ||
        (last.content ?? []).every(
          (c) => c.type === 'text' && !String(c.text ?? '').replace(/\u00a0/g, '').trim(),
        ))
    if (!empty) break
    content = content.slice(0, -1)
    changed = true
  }
  return { doc: { ...doc, content }, changed }
}
