import { describe, expect, it, vi } from 'vitest'
import { createTestEditor } from '../testing/createTestEditor'

describe('fragment link navigation', () => {
  it('clicking a #fragment link scrolls to the explicit anchor', () => {
    const src =
      '[Go](#cq-3c-service)\n\n<a id="cq-3c-service"></a>\n\n### 3C Service Call Queue\n\nBody here\n'
    const editor = createTestEditor(src)
    const view = editor.view
    const link = view.dom.querySelector('a[href="#cq-3c-service"]') as HTMLAnchorElement | null
    expect(link).toBeTruthy()

    const target = view.dom.querySelector('#cq-3c-service') as HTMLElement | null
    expect(target).toBeTruthy()

    const scroll = vi.fn()
    if (target) target.scrollIntoView = scroll

    link!.dispatchEvent(
      new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }),
    )

    // ProseMirror may handle via plugin on next tick / sync — run rAF flush
    return new Promise<void>((resolve) => {
      requestAnimationFrame(() => {
        expect(scroll).toHaveBeenCalled()
        editor.destroy()
        resolve()
      })
    })
  })

  it('explicit anchors survive reopen', () => {
    const src = '<a id="aa-3c-main"></a>\n\n### 3C Main AA\n'
    const editor = createTestEditor(src)
    expect(editor.view.dom.querySelector('#aa-3c-main')).toBeTruthy()
    const md = editor.getMarkdown?.() ?? ''
    expect(md).toContain('<a id="aa-3c-main"></a>')
    editor.destroy()
  })
})
