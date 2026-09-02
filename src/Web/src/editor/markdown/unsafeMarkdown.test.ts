import { describe, expect, it } from 'vitest'
import { looksUnsafeForVisual } from '../../unsafeMarkdown'

describe('looksUnsafeForVisual', () => {
  it('does not force Source for HTML email dumps inside fenced code', () => {
    const md = `# Notification

![shot](Workflow%20Rules.assets/a.png)

\`\`\`xml
<html>
<head>
	<title></title>
</head>
<body>
<p>Hello [contactfirstname],</p>
<p><img src="https://example.com/logo.png" /></p>
</body>
</html>
\`\`\`
`
    expect(looksUnsafeForVisual(md)).toEqual({ unsafe: false })
  })

  it('does not force Source for many unknown tags in the body', () => {
    const tags = Array.from({ length: 20 }, (_, i) => `<div class="x${i}">block</div>`).join('\n')
    expect(looksUnsafeForVisual(tags)).toEqual({ unsafe: false })
  })

  it('still flags a script tag in the live body', () => {
    const r = looksUnsafeForVisual('Hello\n\n<script>alert(1)</script>\n')
    expect(r.unsafe).toBe(true)
    expect(r.reason).toMatch(/HTML tags/i)
  })

  it('ignores a script tag that is only inside a fence', () => {
    expect(looksUnsafeForVisual('```html\n<script>alert(1)</script>\n```\n')).toEqual({ unsafe: false })
  })

  it('flags javascript: URLs in the live body', () => {
    expect(looksUnsafeForVisual('See [x](javascript:alert(1))').unsafe).toBe(true)
  })
})
