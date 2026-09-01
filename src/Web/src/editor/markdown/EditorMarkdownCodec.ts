import type { Node as PmNode } from '@tiptap/pm/model'
import { createOfficialMarkdownCodec, type EditorMarkdownCodec, type ParseResult, type SerializeResult } from './OfficialMarkdownCodec'

export type { EditorDiagnostic } from './saveSafetyValidator'
export type { EditorMarkdownCodec, ParseResult, SerializeResult }

export function createEditorMarkdownCodec(): EditorMarkdownCodec {
  return createOfficialMarkdownCodec()
}

let sharedOfficial: EditorMarkdownCodec | null = null

export function getEditorMarkdownCodec(): EditorMarkdownCodec {
  sharedOfficial ??= createOfficialMarkdownCodec()
  return sharedOfficial
}

export function serializeEditorDoc(doc: PmNode): SerializeResult {
  return getEditorMarkdownCodec().serialize(doc)
}
