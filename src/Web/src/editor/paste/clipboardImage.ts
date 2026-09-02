/** Snipping Tool / some browsers put the PNG on `items`, not `files`. */

export type ClipboardFileItem = {
  kind: string
  type: string
  getAsFile: () => File | null
}

export function imageFileFromClipboardParts(
  files: ArrayLike<File> | undefined,
  items: ArrayLike<ClipboardFileItem> | undefined,
): File | null {
  const fromFiles = Array.from(files ?? []).find((f) => f.type.startsWith('image/'))
  if (fromFiles) return fromFiles
  for (const item of Array.from(items ?? [])) {
    if (item.kind === 'file' && item.type.startsWith('image/')) {
      const f = item.getAsFile()
      if (f) return f
    }
  }
  return null
}

export function imageFileFromClipboard(clipboard: DataTransfer | null | undefined): File | null {
  if (!clipboard) return null
  return imageFileFromClipboardParts(clipboard.files, clipboard.items)
}
