import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'

export type WikiSuggestItem = {
  id: string
  title: string
  relativePath: string
  folderPath: string
}

export type WikiSuggestState = {
  active: boolean
  from: number
  to: number
  query: string
} | null

const key = new PluginKey<WikiSuggestState>('wikiLinkSuggest')

export function getWikiSuggestState(editor: { state: Parameters<typeof key.getState>[0] }): WikiSuggestState {
  return key.getState(editor.state) ?? null
}

type Options = {
  onChange?: (state: WikiSuggestState) => void
}

/** Detects [[query before the cursor for autocomplete UI. */
export const WikiLinkSuggest = Extension.create<Options>({
  name: 'wikiLinkSuggest',

  addOptions() {
    return { onChange: undefined }
  },

  addProseMirrorPlugins() {
    const onChange = this.options.onChange
    return [
      new Plugin<WikiSuggestState>({
        key,
        state: {
          init: () => null,
          apply(_tr, _value, _old, state) {
            const sel = state.selection
            if (!sel.empty) {
              onChange?.(null)
              return null
            }
            const $from = sel.$from
            const parent = $from.parent
            if (!parent.isTextblock) {
              onChange?.(null)
              return null
            }
            const textBefore = parent.textBetween(0, $from.parentOffset, undefined, '\ufffc')
            const m = /\[\[([^\]\n]*)$/.exec(textBefore)
            if (!m) {
              onChange?.(null)
              return null
            }
            const query = m[1] ?? ''
            const from = $from.pos - m[0].length
            const next = { active: true, from, to: $from.pos, query }
            onChange?.(next)
            return next
          },
        },
      }),
    ]
  },
})

export { key as wikiSuggestKey }
