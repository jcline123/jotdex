import { Extension } from '@tiptap/core'
import { GapCursor } from '@tiptap/pm/gapcursor'
import { NodeSelection, Plugin, PluginKey, TextSelection, type EditorState, type Transaction } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'
import type { Node as PmNode } from '@tiptap/pm/model'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    blockGapNavigation: {
      arrowToBlockGap: (dir: 1 | -1) => ReturnType
      insertParagraphAtBlockGap: () => ReturnType
    }
  }
}

const GAP_CLICK_KEY = new PluginKey('blockGapClick')

const HARD_BLOCKS = new Set([
  'codeBlock',
  'image',
  'horizontalRule',
  'table',
  'pendingAsset',
  'callout',
  'details',
  'mathBlock',
  'bookmarkCard',
])

const INTERACTIVE = 'button, select, input, textarea, a, option'
const WRAP_SELECTOR = '.code-block-box, .note-image, [data-pending-asset], .tableWrapper'
const EDGE_PX = 8

function isHardBlock(node: PmNode | null | undefined): boolean {
  if (!node) return false
  return HARD_BLOCKS.has(node.type.name) || node.isAtom
}

function atEndOfTextblock($from: { parent: PmNode; parentOffset: number }): boolean {
  return $from.parentOffset === $from.parent.nodeSize - 2
}

function applyGapCursor(tr: Transaction, pos: number): boolean {
  const $pos = tr.doc.resolve(pos)
  const valid = (GapCursor as unknown as { valid: (p: typeof $pos) => boolean }).valid
  if (!valid($pos)) return false
  tr.setSelection(new GapCursor($pos))
  return true
}

function insertParagraphOn(tr: Transaction, pos: number): boolean {
  const paragraph = tr.doc.type.schema.nodes.paragraph
  if (!paragraph) return false
  const node = paragraph.createAndFill()
  if (!node) return false
  tr.insert(pos, node)
  tr.setSelection(TextSelection.near(tr.doc.resolve(pos + 1)))
  tr.scrollIntoView()
  return true
}

function gapPosAfterArrow(state: EditorState, dir: 1 | -1): number | null {
  const { selection } = state
  if (selection instanceof NodeSelection && isHardBlock(selection.node)) {
    return dir > 0 ? selection.to : selection.from
  }
  const { $from, empty } = selection
  if (!empty || $from.parent.type.name !== 'codeBlock') return null
  if (dir > 0) {
    if (!atEndOfTextblock($from)) return null
    return $from.after()
  }
  if ($from.parentOffset !== 0) return null
  return $from.before()
}

function dispatchGap(view: EditorView, pos: number): boolean {
  const tr = view.state.tr
  if (!applyGapCursor(tr, pos)) return false
  view.dispatch(tr)
  return true
}

function placeGapFromClick(view: EditorView, event: MouseEvent): boolean {
  const target = event.target
  if (!(target instanceof HTMLElement)) return false
  if (target.closest(INTERACTIVE)) return false

  const clickPos = view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (!clickPos) return false

  const wrap = target.closest(WRAP_SELECTOR)
  if (wrap instanceof HTMLElement && clickPos.inside >= 0) {
    const node = view.state.doc.nodeAt(clickPos.inside)
    if (node && isHardBlock(node)) {
      const inCode = !!target.closest('.code-block-pre, pre, code')
      if (inCode) return false
      const rect = wrap.getBoundingClientRect()
      const y = event.clientY
      if (y - rect.top <= EDGE_PX) return dispatchGap(view, clickPos.inside)
      const leafChrome =
        wrap.classList.contains('note-image') || wrap.hasAttribute('data-pending-asset')
      if (leafChrome && rect.bottom - y <= EDGE_PX) {
        return dispatchGap(view, clickPos.inside + node.nodeSize)
      }
      return false
    }
  }

  return dispatchGap(view, clickPos.pos)
}

export const BlockGapNavigation = Extension.create({
  name: 'blockGapNavigation',
  priority: 1200,

  extendNodeSchema(extension) {
    if (extension.name === 'codeBlock') return { createGapCursor: true }
    return {}
  },

  addCommands() {
    return {
      arrowToBlockGap:
        (dir: 1 | -1) =>
        ({ state, tr }) => {
          const pos = gapPosAfterArrow(state, dir)
          if (pos == null) return false
          return applyGapCursor(tr, pos)
        },
      insertParagraphAtBlockGap:
        () =>
        ({ state, tr }) => {
          const { selection } = state
          if (selection instanceof GapCursor) return insertParagraphOn(tr, selection.from)
          if (selection instanceof NodeSelection && isHardBlock(selection.node)) {
            return insertParagraphOn(tr, selection.to)
          }
          return false
        },
    }
  },

  addKeyboardShortcuts() {
    return {
      ArrowDown: () => this.editor.commands.arrowToBlockGap(1),
      ArrowUp: () => this.editor.commands.arrowToBlockGap(-1),
      Enter: () => this.editor.commands.insertParagraphAtBlockGap(),
      'Mod-Enter': () =>
        this.editor.commands.command(({ state, tr }) => {
          const { $from } = state.selection
          if ($from.parent.type.name !== 'codeBlock') return false
          const after = $from.after()
          if (!(GapCursor as unknown as { valid: (p: ReturnType<typeof state.doc.resolve>) => boolean }).valid(state.doc.resolve(after))) return false
          return insertParagraphOn(tr, after)
        }),
    }
  },

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: GAP_CLICK_KEY,
        props: {
          handleClick(view, _pos, event) {
            if (!view.editable) return false
            return placeGapFromClick(view, event as MouseEvent)
          },
          handleTextInput(view, from, _to, text) {
            if (!(view.state.selection instanceof GapCursor)) return false
            const paragraph = view.state.schema.nodes.paragraph
            const node = paragraph?.createAndFill()
            if (!node) return false
            const tr = view.state.tr.insert(from, node)
            const insertedAt = from + 1
            tr.insertText(text, insertedAt)
            tr.setSelection(TextSelection.near(tr.doc.resolve(insertedAt + text.length)))
            view.dispatch(tr.scrollIntoView())
            return true
          },
        },
      }),
    ]
  },
})
