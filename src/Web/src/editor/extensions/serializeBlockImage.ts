type MarkdownState = {
  write: (text: string) => void
  esc: (text: string) => string
  closeBlock: (node: unknown) => void
}

/** Write a Markdown image and close the block so the next heading/list cannot fuse. */
export function serializeBlockImage(state: MarkdownState, node: { attrs: Record<string, unknown> }): void {
  const alt = String(node.attrs.alt ?? '')
  const src = String(node.attrs.src ?? '')
  const title = node.attrs.title != null && String(node.attrs.title).length ? String(node.attrs.title) : ''
  const safeSrc = src.replace(/[()]/g, '\\$&')
  const titlePart = title ? ` "${title.replace(/"/g, '\\"')}"` : ''
  state.write(`![${state.esc(alt)}](${safeSrc}${titlePart})`)
  state.closeBlock(node)
}
