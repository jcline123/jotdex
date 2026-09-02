import type { Editor } from '@tiptap/core'
import { applyHeadingToSelection } from '../../headingSelection'
import { normalizeBlockSelection } from '../../selectionUtils'
import { moveTopLevelBlock } from '../blocks/moveBlock'
import { insertMarkdown } from '../operations/contentInsertion'
import { setBlockAlignment } from '../formatting/alignment'
import type { CalloutType } from '../../callout'
import type { CommandContext, CommandResult, EditorCommandDescriptor, EditorCommandId } from './types'

function ok(): CommandResult {
  return { ok: true }
}

function fail(reason: string): CommandResult {
  return { ok: false, reason }
}

function markToggle(editor: Editor, run: () => boolean): CommandResult {
  normalizeBlockSelection(editor)
  if (!editor.can().chain().focus().run()) return fail('Editor is not editable')
  run()
  return ok()
}

function heading(level: 1 | 2 | 3, ctx: CommandContext): CommandResult {
  const r = applyHeadingToSelection(ctx.editor, level)
  if (!r.ok && r.reason) ctx.onError?.(r.reason)
  return r.ok ? ok() : fail(r.reason ?? 'Heading failed')
}

function callout(type: CalloutType, editor: Editor): CommandResult {
  if (typeof editor.commands.setCallout !== 'function') return fail('Callouts are unavailable')
  editor.chain().focus().setCallout(type).run()
  return ok()
}

export function createEditorCommandRegistry(): {
  all: EditorCommandDescriptor[]
  get: (id: EditorCommandId) => EditorCommandDescriptor | undefined
  execute: (id: EditorCommandId, ctx: CommandContext) => CommandResult
  slashItems: (query: string) => EditorCommandDescriptor[]
  plusItems: (query: string) => EditorCommandDescriptor[]
} {
  const all: EditorCommandDescriptor[] = [
    {
      id: 'heading.1',
      label: 'Heading 1',
      keywords: ['h1', 'title', 'heading'],
      group: 'basic',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('heading', { level: 1 }),
      execute: (ctx) => heading(1, ctx),
    },
    {
      id: 'heading.2',
      label: 'Heading 2',
      keywords: ['h2', 'heading', 'section'],
      group: 'basic',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('heading', { level: 2 }),
      execute: (ctx) => heading(2, ctx),
    },
    {
      id: 'heading.3',
      label: 'Heading 3',
      keywords: ['h3', 'heading', 'subsection'],
      group: 'basic',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('heading', { level: 3 }),
      execute: (ctx) => heading(3, ctx),
    },
    {
      id: 'mark.bold',
      label: 'Bold',
      keywords: ['bold', 'strong'],
      group: 'format',
      shortcut: 'Mod-b',
      isEnabled: (e) => e.can().chain().focus().toggleBold().run(),
      isActive: (e) => e.isActive('bold'),
      execute: (ctx) => markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleBold().run()),
    },
    {
      id: 'mark.italic',
      label: 'Italic',
      keywords: ['italic', 'emphasis'],
      group: 'format',
      shortcut: 'Mod-i',
      isEnabled: (e) => e.can().chain().focus().toggleItalic().run(),
      isActive: (e) => e.isActive('italic'),
      execute: (ctx) => markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleItalic().run()),
    },
    {
      id: 'mark.strike',
      label: 'Strikethrough',
      keywords: ['strike', 'strikethrough', 'del'],
      group: 'format',
      isEnabled: (e) => e.can().chain().focus().toggleStrike().run(),
      isActive: (e) => e.isActive('strike'),
      execute: (ctx) => markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleStrike().run()),
    },
    {
      id: 'mark.code',
      label: 'Inline code',
      keywords: ['code', 'inline'],
      group: 'format',
      isEnabled: (e) => e.can().chain().focus().toggleCode().run(),
      isActive: (e) => e.isActive('code'),
      execute: (ctx) => markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleCode().run()),
    },
    {
      id: 'mark.highlight',
      label: 'Highlight',
      keywords: ['highlight', 'mark', 'yellow'],
      group: 'format',
      isEnabled: (e) => typeof e.commands.toggleHighlight === 'function',
      isActive: (e) => e.isActive('highlight'),
      execute: (ctx) => {
        if (typeof ctx.editor.commands.toggleHighlight !== 'function') return fail('Highlight is unavailable')
        return markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleHighlight().run())
      },
    },
    {
      id: 'mark.underline',
      label: 'Underline',
      keywords: ['underline', 'u'],
      group: 'format',
      isEnabled: (e) => typeof e.commands.toggleUnderline === 'function',
      isActive: (e) => e.isActive('underline'),
      execute: (ctx) => {
        if (typeof ctx.editor.commands.toggleUnderline !== 'function') return fail('Underline is unavailable')
        return markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleUnderline().run())
      },
    },
    {
      id: 'mark.subscript',
      label: 'Subscript',
      keywords: ['sub', 'subscript'],
      group: 'format',
      isEnabled: (e) => typeof e.commands.toggleSubscript === 'function',
      isActive: (e) => e.isActive('subscript'),
      execute: (ctx) => {
        if (typeof ctx.editor.commands.toggleSubscript !== 'function') return fail('Subscript is unavailable')
        return markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleSubscript().run())
      },
    },
    {
      id: 'mark.superscript',
      label: 'Superscript',
      keywords: ['sup', 'superscript'],
      group: 'format',
      isEnabled: (e) => typeof e.commands.toggleSuperscript === 'function',
      isActive: (e) => e.isActive('superscript'),
      execute: (ctx) => {
        if (typeof ctx.editor.commands.toggleSuperscript !== 'function') return fail('Superscript is unavailable')
        return markToggle(ctx.editor, () => ctx.editor.chain().focus().toggleSuperscript().run())
      },
    },
    {
      id: 'format.clear',
      label: 'Clear formatting',
      keywords: ['clear', 'remove', 'unformat'],
      group: 'format',
      isEnabled: () => true,
      execute: (ctx) => {
        normalizeBlockSelection(ctx.editor)
        ctx.editor.chain().focus().unsetAllMarks().clearNodes().run()
        return ok()
      },
    },
    {
      id: 'block.bulletList',
      label: 'Bullet list',
      keywords: ['list', 'bullet', 'ul'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('bulletList'),
      execute: (ctx) => {
        normalizeBlockSelection(ctx.editor)
        ctx.editor.chain().focus().toggleBulletList().run()
        return ok()
      },
    },
    {
      id: 'block.orderedList',
      label: 'Numbered list',
      keywords: ['list', 'numbered', 'ol'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('orderedList'),
      execute: (ctx) => {
        normalizeBlockSelection(ctx.editor)
        ctx.editor.chain().focus().toggleOrderedList().run()
        return ok()
      },
    },
    {
      id: 'block.taskList',
      label: 'Todo list',
      keywords: ['todo', 'task', 'checkbox'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('taskList'),
      execute: (ctx) => {
        normalizeBlockSelection(ctx.editor)
        ctx.editor.chain().focus().toggleTaskList().run()
        return ok()
      },
    },
    {
      id: 'block.codeBlock',
      label: 'Code box',
      keywords: ['code', 'fence', 'powershell'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('codeBlock'),
      execute: (ctx) => {
        normalizeBlockSelection(ctx.editor)
        ctx.editor.chain().focus().toggleCodeBlock({ language: 'powershell' }).run()
        return ok()
      },
    },
    {
      id: 'block.blockquote',
      label: 'Quote',
      keywords: ['quote', 'blockquote'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('blockquote'),
      execute: (ctx) => {
        ctx.editor.chain().focus().toggleBlockquote().run()
        return ok()
      },
    },
    {
      id: 'block.callout.note',
      label: 'Callout: note',
      keywords: ['callout', 'note'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => typeof e.commands.setCallout === 'function',
      execute: (ctx) => callout('note', ctx.editor),
    },
    {
      id: 'block.callout.tip',
      label: 'Callout: tip',
      keywords: ['callout', 'tip'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => typeof e.commands.setCallout === 'function',
      execute: (ctx) => callout('tip', ctx.editor),
    },
    {
      id: 'block.callout.info',
      label: 'Callout: info',
      keywords: ['callout', 'info'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => typeof e.commands.setCallout === 'function',
      execute: (ctx) => callout('info', ctx.editor),
    },
    {
      id: 'block.callout.warning',
      label: 'Callout: warning',
      keywords: ['callout', 'warning'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => typeof e.commands.setCallout === 'function',
      execute: (ctx) => callout('warning', ctx.editor),
    },
    {
      id: 'block.callout.danger',
      label: 'Callout: danger',
      keywords: ['callout', 'danger'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => typeof e.commands.setCallout === 'function',
      execute: (ctx) => callout('danger', ctx.editor),
    },
    {
      id: 'block.table',
      label: 'Table',
      keywords: ['table', 'grid'],
      group: 'insert',
      slash: true,
      plus: true,
      isEnabled: () => true,
      execute: (ctx) => {
        ctx.editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()
        return ok()
      },
    },
    {
      id: 'block.details',
      label: 'Details / fold',
      keywords: ['details', 'fold', 'disclosure', 'summary'],
      group: 'blocks',
      slash: true,
      plus: true,
      isEnabled: (e) => Boolean(e.schema.nodes.details),
      execute: (ctx) => {
        if (!ctx.editor.schema.nodes.details) return fail('Details are unavailable')
        if (typeof ctx.editor.commands.setDetails === 'function') {
          ctx.editor.chain().focus().setDetails().run()
          return ok()
        }
        insertMarkdown(ctx.editor, '<!-- jotdex-details -->\nSummary\n\nHidden details\n<!-- /jotdex-details -->\n')
        return ok()
      },
    },
    {
      id: 'block.horizontalRule',
      label: 'Divider',
      keywords: ['hr', 'rule', 'divider'],
      group: 'insert',
      slash: true,
      plus: true,
      isEnabled: () => true,
      execute: (ctx) => {
        ctx.editor.chain().focus().setHorizontalRule().run()
        return ok()
      },
    },
    {
      id: 'insert.link',
      label: 'Link',
      keywords: ['link', 'url', 'href'],
      group: 'insert',
      slash: true,
      isEnabled: () => true,
      isActive: (e) => e.isActive('link'),
      execute: (ctx) => {
        ctx.onRequestLink?.()
        return ok()
      },
    },
    {
      id: 'insert.bookmark',
      label: 'Link card',
      keywords: ['bookmark', 'card', 'embed'],
      group: 'insert',
      slash: true,
      plus: true,
      isEnabled: (e) => Boolean(e.schema.nodes.bookmarkCard),
      execute: (ctx) => {
        ctx.onRequestLink?.()
        return ok()
      },
    },
    {
      id: 'insert.mathInline',
      label: 'Inline math',
      keywords: ['math', 'latex', 'katex', 'equation'],
      group: 'insert',
      slash: true,
      isEnabled: (e) => Boolean(e.schema.nodes.mathInline),
      execute: (ctx) => {
        const src = typeof ctx.extra === 'string' ? ctx.extra : 'x'
        insertMarkdown(ctx.editor, `\\(${src}\\)`)
        return ok()
      },
    },
    {
      id: 'insert.mathBlock',
      label: 'Block math',
      keywords: ['math', 'latex', 'display'],
      group: 'insert',
      slash: true,
      plus: true,
      isEnabled: (e) => Boolean(e.schema.nodes.mathBlock),
      execute: (ctx) => {
        const src = typeof ctx.extra === 'string' ? ctx.extra : 'x = 1'
        insertMarkdown(ctx.editor, `\\[${src}\\]`)
        return ok()
      },
    },
    {
      id: 'insert.emoji',
      label: 'Emoji',
      keywords: ['emoji', 'unicode'],
      group: 'insert',
      slash: true,
      isEnabled: () => true,
      execute: (ctx) => {
        if (typeof ctx.extra === 'string' && ctx.extra) {
          ctx.editor.chain().focus().insertContent(ctx.extra).run()
          return ok()
        }
        ctx.onRequestEmoji?.()
        return ok()
      },
    },
    {
      id: 'insert.image',
      label: 'Image / attach',
      keywords: ['image', 'attach', 'upload', 'picture'],
      group: 'insert',
      slash: true,
      plus: true,
      isEnabled: () => true,
      execute: (ctx) => {
        ctx.onRequestUpload?.()
        return ok()
      },
    },
    {
      id: 'align.left',
      label: 'Align left',
      keywords: ['align', 'left'],
      group: 'format',
      isEnabled: () => true,
      execute: (ctx) => {
        setBlockAlignment(ctx.editor, null)
        return ok()
      },
    },
    {
      id: 'align.center',
      label: 'Align center',
      keywords: ['align', 'center'],
      group: 'format',
      isEnabled: () => true,
      execute: (ctx) => {
        setBlockAlignment(ctx.editor, 'center')
        return ok()
      },
    },
    {
      id: 'align.right',
      label: 'Align right',
      keywords: ['align', 'right'],
      group: 'format',
      isEnabled: () => true,
      execute: (ctx) => {
        setBlockAlignment(ctx.editor, 'right')
        return ok()
      },
    },
    {
      id: 'align.justify',
      label: 'Justify',
      keywords: ['align', 'justify'],
      group: 'format',
      isEnabled: () => true,
      execute: (ctx) => {
        setBlockAlignment(ctx.editor, 'justify')
        return ok()
      },
    },
    {
      id: 'history.undo',
      label: 'Undo',
      keywords: ['undo'],
      group: 'history',
      shortcut: 'Mod-z',
      isEnabled: () => true,
      execute: (ctx) => {
        ctx.editor.chain().focus().undo().run()
        return ok()
      },
    },
    {
      id: 'history.redo',
      label: 'Redo',
      keywords: ['redo'],
      group: 'history',
      shortcut: 'Mod-y',
      isEnabled: () => true,
      execute: (ctx) => {
        ctx.editor.chain().focus().redo().run()
        return ok()
      },
    },
    {
      id: 'block.moveUp',
      label: 'Move block up',
      keywords: ['move', 'up'],
      group: 'blocks',
      shortcut: 'Alt-ArrowUp',
      isEnabled: () => true,
      execute: (ctx) => (moveTopLevelBlock(ctx.editor, -1) ? ok() : fail('Already at the top')),
    },
    {
      id: 'block.moveDown',
      label: 'Move block down',
      keywords: ['move', 'down'],
      group: 'blocks',
      shortcut: 'Alt-ArrowDown',
      isEnabled: () => true,
      execute: (ctx) => (moveTopLevelBlock(ctx.editor, 1) ? ok() : fail('Already at the bottom')),
    },
  ]

  const byId = new Map(all.map((c) => [c.id, c]))

  const matchQuery = (query: string, list: EditorCommandDescriptor[]) => {
    const q = query.trim().toLowerCase()
    const pool = list.filter((c) => c.isEnabled)
    if (!q) return pool
    return pool.filter(
      (c) =>
        c.label.toLowerCase().includes(q) ||
        c.id.includes(q) ||
        c.keywords.some((k) => k.includes(q)),
    )
  }

  return {
    all,
    get: (id) => byId.get(id),
    execute: (id, ctx) => {
      const cmd = byId.get(id)
      if (!cmd) return fail(`Unknown command ${id}`)
      if (!cmd.isEnabled(ctx.editor)) {
        const reason = cmd.disabledReason?.(ctx.editor) ?? 'This command is not available here'
        ctx.onError?.(reason)
        return fail(reason)
      }
      return cmd.execute(ctx)
    },
    slashItems: (query) => matchQuery(query, all.filter((c) => c.slash)),
    plusItems: (query) => matchQuery(query, all.filter((c) => c.plus)),
  }
}

export type EditorCommandRegistry = ReturnType<typeof createEditorCommandRegistry>
