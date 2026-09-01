import type { JSONContent } from '@tiptap/core'
import type { Node as PmNode } from '@tiptap/pm/model'
import { Editor } from '@tiptap/core'
import { createEditorExtensions } from '../extensions/createEditorExtensions'
import { compareSemantic, type SemanticComparison } from './semanticCompare'
import { isSaveSafe, validateMarkdownSafety, type EditorDiagnostic } from './saveSafetyValidator'
import { PENDING_ASSET_NODE } from '../extensions/PendingAssetPlaceholder'

export type { EditorDiagnostic }

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
  parse(markdownBody: string): ParseResult
  serialize(doc: PmNode): SerializeResult
  compareSemantic(a: PmNode, b: PmNode): SemanticComparison
}

function hasPending(doc: PmNode): boolean {
  let found = false
  doc.descendants((node) => {
    if (node.type.name === PENDING_ASSET_NODE) found = true
  })
  return found
}

export function createEditorMarkdownCodec(): EditorMarkdownCodec {
  const editor = new Editor({
    extensions: createEditorExtensions({ withReactNodeViews: false }),
    content: '',
  })

  return {
    parse(markdownBody: string): ParseResult {
      try {
        editor.commands.setContent(markdownBody || '', { emitUpdate: false })
        return { ok: true, doc: editor.getJSON(), diagnostics: [] }
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
        const json = doc.toJSON() as JSONContent
        editor.commands.setContent(json, { emitUpdate: false })
        const markdown = (
          editor.storage as { markdown?: { getMarkdown?: () => string } }
        ).markdown?.getMarkdown?.()
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
        return { ok: true, markdown, diagnostics }
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
  }
}

let shared: EditorMarkdownCodec | null = null

export function getEditorMarkdownCodec(): EditorMarkdownCodec {
  shared ??= createEditorMarkdownCodec()
  return shared
}

export function serializeEditorDoc(doc: PmNode): SerializeResult {
  return getEditorMarkdownCodec().serialize(doc)
}
