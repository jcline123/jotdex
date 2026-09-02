import { describe, expect, it } from 'vitest'
import { buildResolverState, displayUrlForSrc } from './AttachmentResolver'

describe('displayUrlForSrc', () => {
  it('maps a vault-relative markdown path to the attachment API URL', () => {
    const state = buildResolverState([
      { id: 'att1', fileName: '2026-09-02_125856_screenshot.png', contentType: 'image/png' },
    ])
    expect(
      displayUrlForSrc(state, 'Workflow%20Rules.assets/2026-09-02_125856_screenshot.png'),
    ).toBe('/api/attachments/att1')
  })

  it('leaves the src unchanged until inventory includes that file', () => {
    const empty = buildResolverState([])
    const src = 'Note.assets/shot.png'
    expect(displayUrlForSrc(empty, src)).toBe(src)
  })
})
