export function planEditorReload(input: {
  epochChanged: boolean
  markdownEqualsLastEmitted: boolean
  attachmentsChanged: boolean
}): { replaceDocument: boolean; updateResolver: boolean } {
  return {
    replaceDocument: input.epochChanged,
    updateResolver: input.attachmentsChanged,
  }
}
