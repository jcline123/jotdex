import type { Editor } from '@tiptap/core'

export type EditorCommandGroup = 'basic' | 'format' | 'insert' | 'blocks' | 'history'

export type EditorCommandId =
  | 'heading.1'
  | 'heading.2'
  | 'heading.3'
  | 'mark.bold'
  | 'mark.italic'
  | 'mark.strike'
  | 'mark.code'
  | 'mark.highlight'
  | 'mark.underline'
  | 'mark.subscript'
  | 'mark.superscript'
  | 'format.clear'
  | 'block.bulletList'
  | 'block.orderedList'
  | 'block.taskList'
  | 'block.codeBlock'
  | 'block.blockquote'
  | 'block.callout.note'
  | 'block.callout.tip'
  | 'block.callout.info'
  | 'block.callout.warning'
  | 'block.callout.danger'
  | 'block.table'
  | 'block.details'
  | 'block.horizontalRule'
  | 'insert.link'
  | 'insert.bookmark'
  | 'insert.mathInline'
  | 'insert.mathBlock'
  | 'insert.emoji'
  | 'insert.image'
  | 'align.left'
  | 'align.center'
  | 'align.right'
  | 'align.justify'
  | 'history.undo'
  | 'history.redo'
  | 'block.moveUp'
  | 'block.moveDown'

export type CommandContext = {
  editor: Editor
  onError?: (message: string) => void
  onRequestUpload?: () => void
  onRequestLink?: () => void
  onRequestEmoji?: () => void
  extra?: unknown
}

export type CommandResult = { ok: boolean; reason?: string }

export type EditorCommandDescriptor = {
  id: EditorCommandId
  label: string
  keywords: string[]
  group: EditorCommandGroup
  shortcut?: string
  slash?: boolean
  plus?: boolean
  isEnabled: (editor: Editor) => boolean
  disabledReason?: (editor: Editor) => string | undefined
  isActive?: (editor: Editor) => boolean
  execute: (ctx: CommandContext) => CommandResult
}
