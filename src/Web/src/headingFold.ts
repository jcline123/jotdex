import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Node as PmNode } from '@tiptap/pm/model'

const key = new PluginKey('headingFold')

type FoldState = {
  folded: Set<number>
}

function sectionEnd(doc: PmNode, headingPos: number, level: number): number {
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

/** Click a heading’s fold gutter (▸/▾ via CSS ::before) to collapse its section. */
export const HeadingFold = Extension.create({
  name: 'headingFold',

  addProseMirrorPlugins() {
    return [
      new Plugin<FoldState>({
        key,
        state: {
          init: () => ({ folded: new Set<number>() }),
          apply(tr, value) {
            let folded = value.folded
            if (tr.docChanged) folded = remapFolds(folded, tr.mapping, tr.doc)
            const meta = tr.getMeta(key) as { toggle?: number } | undefined
            if (meta?.toggle != null) {
              folded = new Set(folded)
              if (folded.has(meta.toggle)) folded.delete(meta.toggle)
              else folded.add(meta.toggle)
            }
            return { folded }
          },
        },
        props: {
          decorations(state) {
            const pluginState = key.getState(state) as FoldState | undefined
            if (!pluginState) return null
            return buildDecos(state.doc, pluginState.folded)
          },
          handleClick(view, pos, event) {
            const target = event.target as HTMLElement | null
            if (!target?.closest?.('.foldable-heading')) return false
            // Only toggle when clicking near the left gutter (~1.2rem)
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
            view.dispatch(view.state.tr.setMeta(key, { toggle: headingPos }))
            return true
          },
        },
      }),
    ]
  },
})
