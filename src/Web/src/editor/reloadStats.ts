export const editorReloadStats = {
  setContentCount: 0,
  lastReason: '' as string,
}

export function recordSetContent(reason: string): void {
  editorReloadStats.setContentCount += 1
  editorReloadStats.lastReason = reason
}

export function resetEditorReloadStats(): void {
  editorReloadStats.setContentCount = 0
  editorReloadStats.lastReason = ''
}
