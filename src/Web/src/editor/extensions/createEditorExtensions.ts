import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Placeholder from '@tiptap/extension-placeholder'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { Table } from '@tiptap/extension-table'
import { TableRow } from '@tiptap/extension-table-row'
import { TableCell } from '@tiptap/extension-table-cell'
import { TableHeader } from '@tiptap/extension-table-header'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import { Markdown } from 'tiptap-markdown'
import { Extension } from '@tiptap/core'
import type { Editor, Extensions } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { TextStyle } from '@tiptap/extension-text-style'
import { Color } from '@tiptap/extension-color'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { Callout } from '../../callout'
import { HeadingFold } from '../../headingFold'
import { WikiLinkSuggest, type WikiSuggestState } from '../../wikiLinkSuggest'
import { CODE_BLOCK_ENABLE_TAB_INDENT, CODE_BLOCK_TAB_SIZE } from '../../codeBlockSettings'
import { codeLowlight } from '../../codeHighlight'
import { CodeBlockView } from '../../CodeBlockView'
import { pastePlainIntoCodeBlock, plainTextForCodeBoxPaste } from '../../pasteCodeBlock'
import { BlockImage, BlockImageHeadless } from './BlockImageMarkdown'
import { PendingAssetPlaceholder } from './PendingAssetPlaceholder'
import { PendingAssetView } from './PendingAssetView'
import { AttachmentResolver, type AttachmentInfo } from '../assets/AttachmentResolver'

const FontSizeTextStyle = TextStyle.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      fontSize: {
        default: null,
        parseHTML: (element) => (element as HTMLElement).style.fontSize?.replace(/['"]+/g, '') || null,
        renderHTML: (attributes) => {
          if (!attributes.fontSize) return {}
          return { style: `font-size: ${attributes.fontSize}` }
        },
      },
    }
  },
})

const ConsistentLineBreaks = Extension.create({
  name: 'consistentLineBreaks',
  priority: 1000,
  addKeyboardShortcuts() {
    return {
      'Shift-Enter': () => {
        const { $from } = this.editor.state.selection
        for (let depth = $from.depth; depth > 0; depth--) {
          const name = $from.node(depth).type.name
          if (
            name === 'listItem' ||
            name === 'taskItem' ||
            name === 'tableCell' ||
            name === 'tableHeader' ||
            name === 'blockquote' ||
            name === 'codeBlock'
          ) {
            return false
          }
        }
        return this.editor.commands.first(({ commands }) => [
          () => commands.newlineInCode(),
          () => commands.createParagraphNear(),
          () => commands.liftEmptyBlock(),
          () => commands.splitBlock(),
        ])
      },
    }
  },
})

export type EditorExtensionOptions = {
  withReactNodeViews?: boolean
  wikiOnChange?: (s: WikiSuggestState) => void
  attachments?: AttachmentInfo[]
}

function codeBlockPastePlugin(getEditor: () => Editor | null) {
  return new Plugin({
    key: new PluginKey('codeBlockPlainPaste'),
    props: {
      handlePaste: (_view, event) => {
        const ed = getEditor()
        if (!ed?.isActive('codeBlock')) return false
        const clipboard = event.clipboardData
        if (!clipboard) return false
        event.preventDefault()
        const html = clipboard.getData('text/html')
        const rawPlain = clipboard.getData('text/plain')
        pastePlainIntoCodeBlock(ed, plainTextForCodeBoxPaste(rawPlain, html))
        return true
      },
    },
  })
}

export function createEditorExtensions(opts: EditorExtensionOptions = {}): Extensions {
  const withViews = opts.withReactNodeViews === true
  const CodeBlockExt = (
    withViews
      ? CodeBlockLowlight.extend({
          addNodeView() {
            return ReactNodeViewRenderer(CodeBlockView)
          },
          addProseMirrorPlugins() {
            return [...(this.parent?.() ?? []), codeBlockPastePlugin(() => this.editor)]
          },
        })
      : CodeBlockLowlight.extend({
          addProseMirrorPlugins() {
            return [...(this.parent?.() ?? []), codeBlockPastePlugin(() => this.editor)]
          },
        })
  ).configure({
    lowlight: codeLowlight,
    defaultLanguage: 'plaintext',
    enableTabIndentation: CODE_BLOCK_ENABLE_TAB_INDENT,
    tabSize: CODE_BLOCK_TAB_SIZE,
  })

  return [
    StarterKit.configure({ codeBlock: false, link: false }),
    CodeBlockExt,
    Link.configure({ openOnClick: false, autolink: true }),
    withViews ? BlockImage : BlockImageHeadless,
    withViews
      ? PendingAssetPlaceholder.extend({
          addNodeView() {
            return ReactNodeViewRenderer(PendingAssetView)
          },
        })
      : PendingAssetPlaceholder,
    AttachmentResolver.configure({ attachments: opts.attachments ?? [] }),
    Callout,
    HeadingFold,
    WikiLinkSuggest.configure({ onChange: opts.wikiOnChange }),
    Placeholder.configure({ placeholder: 'Start writing… Type [[ to link a note' }),
    TaskList,
    TaskItem.configure({ nested: true }),
    Table.configure({ resizable: true }),
    TableRow,
    TableHeader,
    TableCell,
    FontSizeTextStyle,
    Color,
    ConsistentLineBreaks,
    Markdown.configure({
      html: true,
      transformPastedText: false,
      transformCopiedText: false,
    }),
  ]
}

export { FontSizeTextStyle, ConsistentLineBreaks }
