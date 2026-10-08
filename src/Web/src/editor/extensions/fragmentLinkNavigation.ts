import { Extension } from '@tiptap/core'
import type { Editor } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'
import { extractLiveOutline } from '../outline/liveOutline'
import { unfoldHeadingsContaining } from '../../headingFold'

function fragmentFromHref(href: string | null | undefined): string | null {
  if (!href) return null
  const t = href.trim()
  if (t.startsWith('#')) {
    const id = decodeURIComponent(t.slice(1)).trim()
    return id || null
  }
  try {
    if (typeof window !== 'undefined') {
      const u = new URL(t, window.location.href)
      if (u.hash && u.origin === window.location.origin && !u.search) {
        const id = decodeURIComponent(u.hash.slice(1)).trim()
        return id || null
      }
    }
  } catch {
    /* ignore */
  }
  const hash = t.indexOf('#')
  if (hash >= 0 && !/^[a-z][a-z0-9+.-]*:/i.test(t.slice(0, hash))) {
    const id = decodeURIComponent(t.slice(hash + 1)).trim()
    return id || null
  }
  return null
}

function findTargetInEditorDom(root: HTMLElement, id: string): HTMLElement | null {
  try {
    const byId =
      typeof CSS !== 'undefined' && CSS.escape
        ? root.querySelector(`#${CSS.escape(id)}`)
        : root.querySelector(`[id="${id.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"]`)
    if (byId instanceof HTMLElement) return byId
  } catch {
    /* ignore */
  }
  const global = typeof document !== 'undefined' ? document.getElementById(id) : null
  if (global && root.contains(global)) return global
  return null
}

function scrollToFragment(editor: Editor, view: EditorView, frag: string): boolean {
  const domHit = findTargetInEditorDom(view.dom, frag)
  if (domHit) {
    try {
      const pos = view.posAtDOM(domHit, 0)
      unfoldHeadingsContaining(editor, pos)
    } catch {
      /* ignore */
    }
    requestAnimationFrame(() => {
      domHit.scrollIntoView({ block: 'start', behavior: 'smooth' })
    })
    return true
  }

  const outline = extractLiveOutline(view.state.doc)
  const item = outline.find((o) => o.slug === frag)
  if (item) {
    unfoldHeadingsContaining(editor, item.pos)
    requestAnimationFrame(() => {
      try {
        const dom = view.domAtPos(item.pos + 1)
        const el = (dom.node as HTMLElement).parentElement ?? (dom.node as HTMLElement)
        el?.scrollIntoView?.({ block: 'start', behavior: 'smooth' })
      } catch {
        /* ignore */
      }
    })
    return true
  }
  return false
}

function tryNavigateFragment(editor: Editor, view: EditorView, event: MouseEvent): boolean {
  if (event.button !== 0) return false
  if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return false
  const target = event.target as HTMLElement | null
  if (!target) return false
  const link = target.closest('a')
  if (!link || !view.dom.contains(link)) return false
  if (link.classList.contains('jotdex-anchor')) return false
  const hrefAttr = link.getAttribute('href')
  const frag = fragmentFromHref(hrefAttr) ?? fragmentFromHref(link.href)
  if (!frag) return false

  event.preventDefault()
  event.stopPropagation()
  scrollToFragment(editor, view, frag)
  return true
}

export const FragmentLinkNavigation = Extension.create({
  name: 'fragmentLinkNavigation',

  addProseMirrorPlugins() {
    const editor = this.editor
    return [
      new Plugin({
        key: new PluginKey('fragmentLinkNavigation'),
        props: {
          // DOM click is more reliable than handleClick while contenteditable places the caret.
          handleDOMEvents: {
            click(view, event) {
              return tryNavigateFragment(editor, view, event as MouseEvent)
            },
          },
          handleClick(view, _pos, event) {
            return tryNavigateFragment(editor, view, event as MouseEvent)
          },
        },
      }),
    ]
  },
})
