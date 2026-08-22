import { createContext, useContext } from 'react'

export type FolderTreeNode = {
  relativePath?: string
  children?: FolderTreeNode[]
}

export type SnippetEditorContextValue = {
  defaultFolder: string
  tree: FolderTreeNode | null
}

export const SnippetEditorContext = createContext<SnippetEditorContextValue>({
  defaultFolder: '',
  tree: null,
})

export function useSnippetEditorContext() {
  return useContext(SnippetEditorContext)
}
