import type { JSONContent } from '@tiptap/core'
import { looksUnsafeForVisual } from '../../unsafeMarkdown'
import { hasMixedInlineImage } from './liftBlockImages'
import { documentHasMultiBlockTableCell, tableHasControlCharacters } from '../tables/tableCompatibility'
import type { EditorDiagnostic } from './saveSafetyValidator'

export type MarkdownInspection = {
  sourceOnly: boolean
  reason?: string
  features: string[]
  diagnostics: EditorDiagnostic[]
}

export function inspectMarkdown(markdownBody: string, doc: JSONContent | undefined): MarkdownInspection {
  const features: string[] = []
  const diagnostics: EditorDiagnostic[] = []
  if (/```/.test(markdownBody)) features.push('code')
  if (/!\[/.test(markdownBody)) features.push('image')
  if (/\[!/.test(markdownBody) || /data-callout=/.test(markdownBody)) features.push('callout')
  if (/jotdex-task|jotdex-todo/.test(markdownBody)) features.push('task-meta')
  if (/\[\[/.test(markdownBody)) features.push('wikilink')
  if (/<span\s+style=/.test(markdownBody)) features.push('style-span')

  const unsafe = looksUnsafeForVisual(markdownBody)
  if (unsafe.unsafe) {
    return {
      sourceOnly: true,
      reason: unsafe.reason,
      features,
      diagnostics: [{ code: 'source-only-html', severity: 'error', message: unsafe.reason ?? 'Unsafe HTML' }],
    }
  }

  if (doc && hasMixedInlineImage(doc)) {
    return {
      sourceOnly: true,
      reason: 'Inline image mixed with prose cannot be represented as a Jotdex block image.',
      features,
      diagnostics: [{ code: 'mixed-inline-image', severity: 'error', message: 'Mixed image and prose' }],
    }
  }

  if (doc && documentHasMultiBlockTableCell(doc)) {
    return {
      sourceOnly: true,
      reason: 'A table cell contains multiple blocks that Markdown cannot store.',
      features,
      diagnostics: [{ code: 'multi-block-table-cell', severity: 'error', message: 'Multi-block table cell' }],
    }
  }

  if (tableHasControlCharacters(markdownBody)) {
    diagnostics.push({
      code: 'table-control-character',
      severity: 'error',
      message: 'Control characters in table Markdown',
    })
  }

  return { sourceOnly: diagnostics.some((d) => d.severity === 'error'), features, diagnostics, reason: diagnostics[0]?.message }
}
