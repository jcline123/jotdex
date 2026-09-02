import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'

export type SlashMenuState = {
  active: boolean
  from: number
  to: number
  query: string
  source: 'slash' | 'plus'
} | null

const key = new PluginKey<SlashMenuState>('jotdexSlashMenu')

export function getSlashMenuState(editor: { state: Parameters<typeof key.getState>[0] }): SlashMenuState {
  return key.getState(editor.state) ?? null
}

type Options = {
  onChange?: (state: SlashMenuState) => void
}

export const SlashMenuPlugin = Extension.create<Options>({
  name: 'jotdexSlashMenu',

  addOptions() {
    return { onChange: undefined }
  },

  addProseMirrorPlugins() {
    const onChange = this.options.onChange
    return [
      new Plugin<SlashMenuState>({
        key,
        state: {
          init: () => null,
          apply(tr, value, _old, state) {
            const meta = tr.getMeta(key) as SlashMenuState | { clear?: boolean } | undefined
            if (meta && 'clear' in meta && meta.clear) {
              onChange?.(null)
              return null
            }
            if (meta && 'active' in meta) {
              onChange?.(meta)
              return meta
            }
            const sel = state.selection
            if (!sel.empty) {
              if (value?.source === 'plus') return value
              onChange?.(null)
              return null
            }
            const $from = sel.$from
            const parent = $from.parent
            if (!parent.isTextblock || parent.type.name === 'codeBlock') {
              onChange?.(null)
              return null
            }
            const textBefore = parent.textBetween(0, $from.parentOffset, undefined, '\ufffc')
            const m = /(^|[\s])\/([^\n/]*)$/.exec(textBefore)
            if (!m) {
              if (value?.source === 'plus') return value
              onChange?.(null)
              return null
            }
            const query = m[2] ?? ''
            const slashAt = $from.pos - query.length - 1
            const next: SlashMenuState = { active: true, from: slashAt, to: $from.pos, query, source: 'slash' }
            onChange?.(next)
            return next
          },
        },
      }),
    ]
  },
})

export { key as slashMenuKey }
