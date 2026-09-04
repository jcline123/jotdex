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
import { Markdown } from '@tiptap/markdown'
import { Extension } from '@tiptap/core'
import type { Editor, Extensions } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { ReactNodeViewRenderer } from '@tiptap/react'
import Typography from '@tiptap/extension-typography'
import { HeadingFold } from '../../headingFold'
import { WikiLinkSuggest, type WikiSuggestState } from '../../wikiLinkSuggest'
import { CODE_BLOCK_ENABLE_TAB_INDENT, CODE_BLOCK_TAB_SIZE } from '../../codeBlockSettings'
import { codeLowlight } from '../../codeHighlight'
import { CodeBlockView } from '../../CodeBlockView'
import { pastePlainIntoCodeBlock, plainTextForCodeBoxPaste } from '../../pasteCodeBlock'
import { JotdexBlockImage, JotdexBlockImageHeadless, JotdexFigureParse } from './JotdexBlockImageMarkdown'
import { JotdexCallout } from './JotdexCalloutMarkdown'
import { JotdexTextStyle, JotdexColor } from './JotdexTextStyleMarkdown'
import { JotdexTaskMetadata } from './JotdexTaskMetadata'
import { HtmlCommentParse } from './HtmlCommentParse'
import { RawHtmlCommentBlock, RawHtmlCommentInline } from './RawHtmlComment'
import { UnresolvedWikiLink } from './UnresolvedWikiLink'
import { PendingAssetPlaceholder } from './PendingAssetPlaceholder'
import { BlockGapNavigation } from './blockGapNavigation'
import { PendingAssetView } from './PendingAssetView'
import { AttachmentResolver, type AttachmentInfo } from '../assets/AttachmentResolver'
import { CANONICAL_LIST_INDENT } from '../markdown/canonical'
import { JotdexHighlight, JotdexUnderline, JotdexSubscript, JotdexSuperscript } from '../formatting/inlineMarks'
import { JotdexAlignMarker, JotdexAlignment, JotdexHeading, JotdexParagraph } from '../formatting/alignment'
import { JotdexMathBlock, JotdexMathInline } from '../math/JotdexMath'
import { JotdexDetails } from '../details/JotdexDetails'
import { JotdexBookmarkCard } from '../links/bookmarkCard'
import { SlashMenuPlugin, type SlashMenuState } from '../slash/slashMenuPlugin'
import { GutterPlusPlugin, type GutterPlusState } from '../gaps/gutterPlusPlugin'
import { DragHandlePlugin } from '../blocks/dragHandlePlugin'
import { isSafeHref } from '../links/linkSchemes'
import { parseSpreadsheet, pasteSpreadsheetIntoTable, stripTableMerges } from '../tables/spreadsheetPaste'

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
            name === 'callout' ||
            name === 'codeBlock' ||
            name === 'details'
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
  slashOnChange?: (s: SlashMenuState) => void
  plusOnChange?: (s: GutterPlusState) => void
  dragOnChange?: (s: { top: number; left: number; pos: number } | null) => void
  attachments?: AttachmentInfo[]
  enableTypography?: boolean
  /** Called when the user toggles a heading fold (not outline jump). */
  persistFolds?: (keys: string[]) => void
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

function tableSpreadsheetPlugin(getEditor: () => Editor | null) {
  return new Plugin({
    key: new PluginKey('tableSpreadsheetPaste'),
    props: {
      transformPastedHTML: (html) => stripTableMerges(html),
      handlePaste: (_view, event) => {
        const ed = getEditor()
        if (!ed?.isActive('table')) return false
        const plain = event.clipboardData?.getData('text/plain') ?? ''
        const grid = parseSpreadsheet(plain)
        if (!grid) return false
        event.preventDefault()
        return pasteSpreadsheetIntoTable(ed, grid)
      },
    },
  })
}

function typographyOn(): boolean {
  if (typeof localStorage === 'undefined') return false
  try {
    return localStorage.getItem('jotdex.typography') === '1'
  } catch {
    return false
  }
}

export function createEditorExtensions(opts: EditorExtensionOptions = {}): Extensions {
  const withViews = opts.withReactNodeViews === true
  const CodeBlockExt = (
    withViews
      ? CodeBlockLowlight.extend({
          draggable: true,
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

  const TableExt = Table.extend({
    addProseMirrorPlugins() {
      return [...(this.parent?.() ?? []), tableSpreadsheetPlugin(() => this.editor)]
    },
  }).configure({ resizable: true })

  const extensions: Extensions = [
    StarterKit.configure({
      codeBlock: false,
      link: false,
      paragraph: false,
      heading: false,
      underline: false,
    }),
    JotdexParagraph,
    JotdexHeading,
    CodeBlockExt,
    Link.configure({
      openOnClick: false,
      autolink: true,
      shouldAutoLink: (url) => isSafeHref(url),
      isAllowedUri: (url, ctx) => {
        if (!isSafeHref(url)) return false
        if (url.startsWith('#') || url.startsWith('/') || url.startsWith('./') || url.startsWith('../')) return true
        if (!/^[a-z][a-z0-9+.-]*:/i.test(url)) return true
        return ctx.defaultValidate(url)
      },
      HTMLAttributes: { rel: 'noreferrer noopener' },
    }),
    withViews ? JotdexBlockImage : JotdexBlockImageHeadless,
    JotdexFigureParse,
    withViews
      ? PendingAssetPlaceholder.extend({
          addNodeView() {
            return ReactNodeViewRenderer(PendingAssetView)
          },
        })
      : PendingAssetPlaceholder,
    AttachmentResolver.configure({ attachments: opts.attachments ?? [] }),
    JotdexCallout,
    JotdexDetails,
    JotdexBookmarkCard,
    JotdexMathInline,
    JotdexMathBlock,
    JotdexHighlight,
    JotdexUnderline,
    JotdexSubscript,
    JotdexSuperscript,
    JotdexAlignMarker,
    JotdexAlignment,
    HeadingFold.configure({ persistFolds: opts.persistFolds }),
    WikiLinkSuggest.configure({ onChange: opts.wikiOnChange }),
    SlashMenuPlugin.configure({ onChange: opts.slashOnChange }),
    GutterPlusPlugin.configure({ onChange: opts.plusOnChange }),
    DragHandlePlugin.configure({ onChange: opts.dragOnChange }),
    Placeholder.configure({ placeholder: 'Start writing… Type / for commands, or [[ to link a note' }),
    TaskList,
    TaskItem.configure({ nested: true }),
    TableExt,
    TableRow,
    TableHeader,
    TableCell,
    JotdexTextStyle,
    JotdexColor,
    JotdexTaskMetadata,
    RawHtmlCommentInline,
    RawHtmlCommentBlock,
    UnresolvedWikiLink,
    HtmlCommentParse,
    BlockGapNavigation,
    ConsistentLineBreaks,
    Markdown.configure({
      indentation: { style: 'space', size: CANONICAL_LIST_INDENT },
      markedOptions: { gfm: true, breaks: false, pedantic: false },
    }),
  ]

  if (opts.enableTypography ?? typographyOn()) {
    extensions.push(Typography)
  }

  return extensions
}

export { ConsistentLineBreaks }
