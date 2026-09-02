import { describe, expect, it } from 'vitest'
import { imageFileFromClipboardParts } from './clipboardImage'

describe('imageFileFromClipboardParts', () => {
  it('prefers clipboard.files when an image is present', () => {
    const files = [new File([new Uint8Array([1])], 'shot.png', { type: 'image/png' })]
    const items = [
      {
        kind: 'file' as const,
        type: 'image/png',
        getAsFile: () => new File([new Uint8Array([2])], 'from-items.png', { type: 'image/png' }),
      },
    ]
    expect(imageFileFromClipboardParts(files, items)?.name).toBe('shot.png')
  })

  it('uses clipboard.items when files is empty (Snipping Tool)', () => {
    const items = [
      { kind: 'string' as const, type: 'text/html', getAsFile: () => null },
      {
        kind: 'file' as const,
        type: 'image/png',
        getAsFile: () => new File([new Uint8Array([1])], 'snip.png', { type: 'image/png' }),
      },
    ]
    expect(imageFileFromClipboardParts([], items)?.name).toBe('snip.png')
  })

  it('returns null when neither files nor items has an image', () => {
    expect(imageFileFromClipboardParts([], [{ kind: 'string', type: 'text/plain', getAsFile: () => null }])).toBeNull()
  })
})
