import type { JSONContent } from '@tiptap/core'
import type { Node as PmNode } from '@tiptap/pm/model'
import { Editor } from '@tiptap/core'
import { createEditorExtensions } from '../extensions/createEditorExtensions'
import { compareSemantic, semanticFingerprint, type SemanticComparison } from './semanticCompare'
import { isSaveSafe, validateMarkdownSafety, type EditorDiagnostic } from './saveSafetyValidator'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'
import { applyOfficialParseFixes } from './parsePostprocess'
import { inspectMarkdown, type MarkdownInspection } from './MarkdownDialectInspector'
import { replaceWithJson, setMarkdownDocument } from '../operations/contentInsertion'

export type ParseResult = {
  ok: boolean
  doc?: JSONContent
  diagnostics: EditorDiagnostic[]
  forcedSourceReason?: string
}

export type SerializeResult = {
  ok: boolean
  markdown?: string
  diagnostics: EditorDiagnostic[]
  semanticFingerprint?: string
}

export interface EditorMarkdownCodec {
  readonly engine: 'official'
  parse(markdownBody: string): ParseResult
  serialize(doc: PmNode): SerializeResult
  compareSemantic(a: PmNode, b: PmNode): SemanticComparison
  inspect(markdownBody: string): MarkdownInspection
  destroy?(): void
}

function hasPending(doc: PmNode): boolean {
  let found = false
  doc.descendants((node) => {
    if (node.type.name === PENDING_ASSET_NODE) found = true
  })
  return found
}

export function createOfficialMarkdownCodec(): EditorMarkdownCodec {
  const editor = new Editor({
    extensions: createEditorExtensions({ withReactNodeViews: false }),
    content: '',
  })

  const applyParsedJson = (json: JSONContent) => {
    const applied = applyOfficialParseFixes(json)
    replaceWithJson(editor, applied.doc, { emitUpdate: false })
    return { diagnostics: applied.diagnostics }
  }

  return {
    engine: 'official',

    parse(markdownBody: string): ParseResult {
      try {
        setMarkdownDocument(editor, markdownBody || '', { emitUpdate: false })
        // setMarkdownDocument already applies official parse fixes
        const applied = applyParsedJson(editor.getJSON())
        const inspection = inspectMarkdown(markdownBody, editor.getJSON())
        if (inspection.sourceOnly) {
          return {
            ok: false,
            doc: editor.getJSON(),
            diagnostics: [...applied.diagnostics, ...inspection.diagnostics],
            forcedSourceReason: inspection.reason,
          }
        }
        return { ok: true, doc: editor.getJSON(), diagnostics: applied.diagnostics }
      } catch (e) {
        return {
          ok: false,
          diagnostics: [
            {
              code: 'parse-failed',
              severity: 'error',
              message: e instanceof Error ? e.message : 'Markdown parse failed',
            },
          ],
          forcedSourceReason: 'The note could not be opened in the visual editor.',
        }
      }
    },

    serialize(doc: PmNode): SerializeResult {
      try {
        if (hasPending(doc)) {
          return {
            ok: false,
            diagnostics: [
              {
                code: 'pending-placeholder',
                severity: 'error',
                message: 'Pending upload placeholder must not be saved',
              },
            ],
          }
        }
        replaceWithJson(editor, doc.toJSON() as JSONContent, { emitUpdate: false })
        const markdown = editor.getMarkdown()
        if (markdown == null) {
          return {
            ok: false,
            diagnostics: [{ code: 'serialize-empty', severity: 'error', message: 'Serializer returned nothing' }],
          }
        }
        const diagnostics = validateMarkdownSafety(markdown, editor.state.doc)
        if (!isSaveSafe(markdown, editor.state.doc)) {
          return { ok: false, markdown, diagnostics }
        }
        return { ok: true, markdown, diagnostics, semanticFingerprint: semanticFingerprint(editor.state.doc) }
      } catch (e) {
        return {
          ok: false,
          diagnostics: [
            {
              code: 'serialize-failed',
              severity: 'error',
              message: e instanceof Error ? e.message : 'Serialize failed',
            },
          ],
        }
      }
    },

    compareSemantic,
    inspect(markdownBody: string) {
      const parsed = this.parse(markdownBody)
      return inspectMarkdown(markdownBody, parsed.doc)
    },
    destroy() {
      editor.destroy()
    },
  }
}
