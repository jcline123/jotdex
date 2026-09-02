import type { Editor } from '@tiptap/core'

type Props = {
  editor: Editor
}

export function TableChrome({ editor }: Props) {
  if (!editor.isActive('table')) return null
  const align = (which: 'left' | 'center' | 'right') => {
    editor.chain().focus().setCellAttribute('style', `text-align: ${which}`).run()
  }
  return (
    <div className="jotdex-table-chrome" role="toolbar" aria-label="Table">
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().addRowBefore().run()}>
        Row +
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().addRowAfter().run()}>
        Row below
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().deleteRow().run()}>
        Del row
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().addColumnBefore().run()}>
        Col +
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().addColumnAfter().run()}>
        Col after
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().deleteColumn().run()}>
        Del col
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().toggleHeaderRow().run()}>
        Header
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => align('left')}>
        Left
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => align('center')}>
        Center
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => align('right')}>
        Right
      </button>
      <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => editor.chain().focus().deleteTable().run()}>
        Delete table
      </button>
    </div>
  )
}
