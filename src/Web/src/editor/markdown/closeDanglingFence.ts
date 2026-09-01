/** Named transform: close an odd number of fences so incomplete test/source still parses as a code block. */
export function closeDanglingFence(markdown: string): { markdown: string; changed: boolean } {
  const count = (markdown.match(/^```/gm) ?? []).length
  if (count % 2 === 0) return { markdown, changed: false }
  return { markdown: `${markdown.replace(/\s*$/, '')}\n\`\`\`\n`, changed: true }
}