import { NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'

/** Pending upload chrome: Retry/Remove on failure. Canonical Markdown is never written for this node. */
export function PendingAssetView({ node, editor, deleteNode }: NodeViewProps) {
  const status = String(node.attrs.status ?? 'uploading')
  const uploadId = String(node.attrs.uploadId ?? '')
  const error = String(node.attrs.error ?? 'Upload failed')
  const failed = status === 'failed'
  const retry = (editor.storage as { pendingAsset?: { retry?: (id: string) => void } }).pendingAsset?.retry

  return (
    <NodeViewWrapper
      className={`pending-asset${failed ? ' is-failed' : ''}`}
      data-pending-asset="1"
      data-status={status}
      contentEditable={false}
    >
      <span>{failed ? error : 'Uploading image…'}</span>
      {failed && (
        <div className="pending-asset-actions">
          <button
            type="button"
            className="ghost"
            onClick={(e) => {
              e.preventDefault()
              e.stopPropagation()
              retry?.(uploadId)
            }}
          >
            Retry
          </button>
          <button
            type="button"
            className="ghost"
            onClick={(e) => {
              e.preventDefault()
              e.stopPropagation()
              deleteNode()
            }}
          >
            Remove
          </button>
        </div>
      )}
    </NodeViewWrapper>
  )
}
