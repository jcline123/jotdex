import { Extension } from '@tiptap/core'
import type { Editor } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Node as PmNode } from '@tiptap/pm/model'

export const headingFoldKey = new PluginKey('headingFold')

const LEGACY_STORAGE_KEY = 'jotdex.headingFolds'

type FoldState = {
  /** Currently hidden in the editor (may be temporarily opened by outline jump). */
  folded: Set<number>
  /** User-chosen folds; outline expand does not remove these. */
  saved: Set<number>
}

type FoldMeta = {
  toggle?: number
  load?: string[]
  unfoldContaining?: number
}

export type HeadingFoldOptions = {
  persistFolds?: (keys: string[]) => void
}

export function headingIdentity(level: number, text: string, occurrence: number): string {
  return `${level}:${occurrence}:${text.trim()}`
}

export function headingIdentities(doc: PmNode): { pos: number; key: string }[] {
  const seen = new Map<string, number>()
  const out: { pos: number; key: string }[] = []
  doc.descendants((node, pos) => {
    if (node.type.name !== 'heading') return
    const level = Number(node.attrs.level ?? 1)
    const text = node.textContent
    const group = `${level}:${text.trim()}`
    const n = (seen.get(group) ?? 0) + 1
    seen.set(group, n)
    out.push({ pos, key: headingIdentity(level, text, n) })
  })
  return out
}

export function keysFromFoldedPositions(doc: PmNode, folded: Set<number>): string[] {
  return headingIdentities(doc)
    .filter((h) => folded.has(h.pos))
    .map((h) => h.key)
}

export function positionsFromFoldKeys(doc: PmNode, keys: Iterable<string>): Set<number> {
  const want = new Set(keys)
  const folded = new Set<number>()
  if (want.size === 0) return folded
  for (const h of headingIdentities(doc)) {
    if (want.has(h.key)) folded.add(h.pos)
  }
  return folded
}

export function peekLegacyBrowserFoldKeys(noteId: string): string[] {
  if (!noteId || typeof localStorage === 'undefined') return []
  try {
    const raw = localStorage.getItem(LEGACY_STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as Record<string, unknown>
    const keys = parsed[noteId]
    return Array.isArray(keys) ? keys.filter((k): k is string => typeof k === 'string') : []
  } catch {
    return []
  }
}

export function clearLegacyBrowserFoldKeys(noteId: string): void {
  if (!noteId || typeof localStorage === 'undefined') return
  try {
    const raw = localStorage.getItem(LEGACY_STORAGE_KEY)
    if (!raw) return
    const parsed = JSON.parse(raw) as Record<string, unknown>
    if (!parsed || typeof parsed !== 'object' || !(noteId in parsed)) return
    delete parsed[noteId]
    if (Object.keys(parsed).length === 0) localStorage.removeItem(LEGACY_STORAGE_KEY)
    else localStorage.setItem(LEGACY_STORAGE_KEY, JSON.stringify(parsed))
  } catch {
    /* quota / private mode */
  }
}

/** Vault helper wins. One-time browser localStorage is only a migration source. */
export function foldsForNote(noteId: string, fromVault: string[] | null | undefined): {
  keys: string[]
  migrateFromBrowser: boolean
} {
  const vault = Array.isArray(fromVault) ? fromVault.filter((k): k is string => typeof k === 'string' && k.length > 0) : []
  if (vault.length > 0) {
    clearLegacyBrowserFoldKeys(noteId)
    return { keys: vault, migrateFromBrowser: false }
  }
  const legacy = peekLegacyBrowserFoldKeys(noteId)
  return { keys: legacy, migrateFromBrowser: legacy.length > 0 }
}

export function persistHeadingFolds(editor: Editor): string[] {
  if (editor.isDestroyed) return []
  const state = headingFoldKey.getState(editor.state) as FoldState | undefined
  return keysFromFoldedPositions(editor.state.doc, state?.saved ?? new Set())
}

export function restoreHeadingFolds(editor: Editor, keys: Iterable<string>): void {
  if (editor.isDestroyed) return
  const list = [...keys]
  editor.view.dispatch(editor.state.tr.setMeta(headingFoldKey, { load: list } satisfies FoldMeta))
}

export async function putHeadingFolds(noteId: string, collapsed: string[]): Promise<boolean> {
  if (!noteId) return false
  try {
    const res = await fetch(`/api/notes/${noteId}/heading-folds`, {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ collapsed }),
    })
    return res.ok
  } catch {
    return false
  }
}

export function unfoldHeadingsContaining(editor: Editor, pos: number): void {
  editor.view.dispatch(editor.state.tr.setMeta(headingFoldKey, { unfoldContaining: pos } satisfies FoldMeta))
}

export function sectionEnd(doc: PmNode, headingPos: number, level: number): number {
  const heading = doc.nodeAt(headingPos)
  if (!heading) return headingPos
  let pos = headingPos + heading.nodeSize
  while (pos < doc.content.size) {
    const node = doc.nodeAt(pos)
    if (!node) break
    if (node.type.name === 'heading' && (node.attrs.level as number) <= level) break
    pos += node.nodeSize
  }
  return pos
}

function buildDecos(doc: PmNode, folded: Set<number>): DecorationSet {
  const decos: ReturnType<typeof Decoration.node>[] = []
  doc.descendants((node, pos) => {
    if (node.type.name !== 'heading') return
    const isFolded = folded.has(pos)
    decos.push(
      Decoration.node(pos, pos + node.nodeSize, {
        class: `foldable-heading${isFolded ? ' is-folded' : ''}`,
      }),
    )
    if (!isFolded) return
    const end = sectionEnd(doc, pos, node.attrs.level as number)
    let p = pos + node.nodeSize
    while (p < end) {
      const child = doc.nodeAt(p)
      if (!child) break
      decos.push(Decoration.node(p, p + child.nodeSize, { class: 'heading-fold-hidden' }))
      p += child.nodeSize
    }
  })
  return DecorationSet.create(doc, decos)
}

function remapFolds(folded: Set<number>, mapping: { map: (pos: number) => number }, doc: PmNode): Set<number> {
  const next = new Set<number>()
  for (const pos of folded) {
    const mapped = mapping.map(pos)
    if (mapped >= 0 && mapped < doc.content.size) {
      const node = doc.nodeAt(mapped)
      if (node?.type.name === 'heading') next.add(mapped)
    }
  }
  return next
}

function unfoldContaining(doc: PmNode, folded: Set<number>, targetPos: number): Set<number> {
  const next = new Set(folded)
  for (const pos of folded) {
    const node = doc.nodeAt(pos)
    if (node?.type.name !== 'heading') {
      next.delete(pos)
      continue
    }
    const end = sectionEnd(doc, pos, node.attrs.level as number)
    if (targetPos > pos && targetPos < end) next.delete(pos)
  }
  return next
}

/** Click a heading’s fold gutter (▸/▾ via CSS ::before) to collapse its section. */
export const HeadingFold = Extension.create<HeadingFoldOptions>({
  name: 'headingFold',

  addOptions() {
    return { persistFolds: undefined }
  },

  addProseMirrorPlugins() {
    const persistFolds = this.options.persistFolds
    return [
      new Plugin<FoldState>({
        key: headingFoldKey,
        state: {
          init: () => ({ folded: new Set<number>(), saved: new Set<number>() }),
          apply(tr, value) {
            let folded = value.folded
            let saved = value.saved
            if (tr.docChanged) {
              folded = remapFolds(folded, tr.mapping, tr.doc)
              saved = remapFolds(saved, tr.mapping, tr.doc)
            }
            const meta = tr.getMeta(headingFoldKey) as FoldMeta | undefined
            if (meta && Array.isArray(meta.load)) {
              folded = positionsFromFoldKeys(tr.doc, meta.load)
              saved = new Set(folded)
            }
            if (meta?.toggle != null) {
              const willFold = !folded.has(meta.toggle)
              folded = new Set(folded)
              saved = new Set(saved)
              if (willFold) {
                folded.add(meta.toggle)
                saved.add(meta.toggle)
              } else {
                folded.delete(meta.toggle)
                saved.delete(meta.toggle)
              }
            }
            if (meta?.unfoldContaining != null) {
              folded = unfoldContaining(tr.doc, folded, meta.unfoldContaining)
            }
            return { folded, saved }
          },
        },
        props: {
          decorations(state) {
            const pluginState = headingFoldKey.getState(state) as FoldState | undefined
            if (!pluginState) return null
            return buildDecos(state.doc, pluginState.folded)
          },
          handleClick(view, pos, event) {
            const target = event.target as HTMLElement | null
            if (!target?.closest?.('.foldable-heading')) return false
            const headingEl = target.closest('.foldable-heading') as HTMLElement
            const rect = headingEl.getBoundingClientRect()
            if (event.clientX > rect.left + 22) return false

            const $pos = view.state.doc.resolve(pos)
            let headingPos: number | null = null
            for (let d = $pos.depth; d > 0; d--) {
              const node = $pos.node(d)
              if (node.type.name === 'heading') {
                headingPos = $pos.before(d)
                break
              }
            }
            if (headingPos == null) {
              const node = view.state.doc.nodeAt(pos)
              if (node?.type.name === 'heading') headingPos = pos
            }
            if (headingPos == null) return false
            const tr = view.state.tr.setMeta(headingFoldKey, { toggle: headingPos } satisfies FoldMeta)
            view.dispatch(tr)
            const next = headingFoldKey.getState(view.state) as FoldState | undefined
            persistFolds?.(keysFromFoldedPositions(view.state.doc, next?.saved ?? new Set()))
            return true
          },
        },
      }),
    ]
  },
})
