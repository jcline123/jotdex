import { remark } from 'remark'
import remarkParse from 'remark-parse'
import remarkLint from 'remark-lint'
import remarkLintFinalNewline from 'remark-lint-final-newline'
import remarkLintNoConsecutiveBlankLines from 'remark-lint-no-consecutive-blank-lines'
import remarkLintHeadingIncrement from 'remark-lint-heading-increment'
import remarkLintFencedCodeFlag from 'remark-lint-fenced-code-flag'

export type MarkdownLintIssue = {
  line: number
  column: number
  endLine?: number
  endColumn?: number
  message: string
  rule: string
  severity: 'warning' | 'error'
}

const processor = remark()
  .use(remarkParse)
  .use(remarkLint)
  .use(remarkLintFinalNewline)
  .use(remarkLintNoConsecutiveBlankLines, { maximum: 2 })
  .use(remarkLintHeadingIncrement, { increment: 1 })
  .use(remarkLintFencedCodeFlag)

/** Report-only Markdown consistency check on note body (no auto-fix). */
export async function lintNoteMarkdown(body: string): Promise<MarkdownLintIssue[]> {
  const file = await processor.process(body)
  const messages = file.messages ?? []
  return messages.map((m) => ({
    line: m.line ?? 1,
    column: m.column ?? 1,
    message: m.reason,
    rule: m.source ?? m.ruleId ?? 'remark-lint',
    severity: m.fatal ? 'error' as const : 'warning' as const,
  }))
}
