import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import { extractLiveOutline } from '../outline/liveOutline'

/** Puts outline slugs on heading DOM nodes so `#fragment` links can scroll to them. */
export const HeadingIds = Extension.create({
  name: 'headingIds',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: new PluginKey('headingIds'),
        props: {
          decorations(state) {
            const outline = extractLiveOutline(state.doc)
            const decos = outline.map((item) => {
              const node = state.doc.nodeAt(item.pos)
              if (!node) return null
              return Decoration.node(item.pos, item.pos + node.nodeSize, { id: item.slug })
            }).filter((d): d is Decoration => d != null)
            return DecorationSet.create(state.doc, decos)
          },
        },
      }),
    ]
  },
})
