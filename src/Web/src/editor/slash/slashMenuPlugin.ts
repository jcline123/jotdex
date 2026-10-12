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

/** Slash trigger: `/` after start-of-block or whitespace; query is a single token (no spaces). */
export function matchSlashTrigger(textBefore: string): { query: string; triggerOffset: number } | null {
  const m = /(^|[\s])\/([^\n/\s]*)$/.exec(textBefore)
  if (!m) return null
  const query = m[2] ?? ''
  // triggerOffset = index of `/` within textBefore
  const triggerOffset = textBefore.length - query.length - 1
  return { query, triggerOffset }
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

            // Programmatic document loads must never open the insert menu.
            if (tr.getMeta('jotdexReplaceDocument')) {
              onChange?.(null)
              return null
            }

            const sel = state.selection
            if (!sel.empty) {
              if (value?.source === 'plus') return value
              if (value) onChange?.(null)
              return null
            }
            const $from = sel.$from
            const parent = $from.parent
            if (!parent.isTextblock || parent.type.name === 'codeBlock') {
              if (value?.source === 'plus') return value
              if (value) onChange?.(null)
              return null
            }
            const textBefore = parent.textBetween(0, $from.parentOffset, undefined, '\ufffc')
            const matched = matchSlashTrigger(textBefore)
            if (!matched) {
              if (value?.source === 'plus') return value
              if (value) onChange?.(null)
              return null
            }

            // Do not open merely because the caret landed after an existing "/foo"
            // (note open, click, arrow keys). Only open/update when the user is typing,
            // or the slash menu is already active from a prior keystroke.
            if (!value?.active && !tr.docChanged) {
              return null
            }
            // Full-document replace still has docChanged — blocked above via meta.
            // Selection-only after open: ignore.

            const slashAt = $from.pos - matched.query.length - 1
            const next: SlashMenuState = {
              active: true,
              from: slashAt,
              to: $from.pos,
              query: matched.query,
              source: 'slash',
            }
            onChange?.(next)
            return next
          },
        },
      }),
    ]
  },
})

export { key as slashMenuKey }
