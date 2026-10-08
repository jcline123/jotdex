import { describe, expect, it } from 'vitest'
import { rewriteAnchorsToBraces } from './anchorProtect'

describe('rewriteAnchorsToBraces', () => {
  it('encodes empty id anchors', () => {
    const { markdown, changed } = rewriteAnchorsToBraces('<a id="cq-3c-service"></a>\n\nHi')
    expect(changed).toBe(true)
    expect(markdown).toContain('{jotdex-anchor:cq-3c-service}')
    expect(markdown).not.toContain('<a id=')
  })

  it('skips fenced code', () => {
    const src = '```\n<a id="x"></a>\n```\n'
    const { markdown, changed } = rewriteAnchorsToBraces(src)
    expect(changed).toBe(false)
    expect(markdown).toContain('<a id="x"></a>')
  })
})
