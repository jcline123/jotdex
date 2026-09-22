import { describe, expect, it } from 'vitest'
import { rewriteHtmlMarksToBraces } from './htmlMarksProtect'

describe('rewriteHtmlMarksToBraces', () => {
  it('brace-protects color spans so # and > do not split the lexer', () => {
    const src =
      'Hello <span style="color: #b54708">map</span> or <span style="color: #027a48">ok</span>'
    const { markdown, changed } = rewriteHtmlMarksToBraces(src)
    expect(changed).toBe(true)
    expect(markdown).not.toContain('<span')
    expect(markdown).toContain('{jotdex-style:')
    expect(markdown).toContain('map')
    expect(markdown).toContain('ok')
  })

  it('leaves unrecognized span styles alone', () => {
    const src = '<span style="color: tomato">nope</span>'
    const { markdown, changed } = rewriteHtmlMarksToBraces(src)
    expect(changed).toBe(false)
    expect(markdown).toBe(src)
  })
})
