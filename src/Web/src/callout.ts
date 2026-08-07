import { Node, mergeAttributes } from '@tiptap/core'

export type CalloutType = 'note' | 'tip' | 'warning' | 'info' | 'danger'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    callout: {
      setCallout: (type?: CalloutType) => ReturnType
    }
  }
}

/** Obsidian-style callout stored as <blockquote data-callout="…"> for html markdown round-trip. */
export const Callout = Node.create({
  name: 'callout',
  group: 'block',
  content: 'block+',
  defining: true,
  isolating: true,

  addAttributes() {
    return {
      type: {
        default: 'note',
        parseHTML: (element) => element.getAttribute('data-callout') || 'note',
        renderHTML: (attributes) => ({ 'data-callout': attributes.type }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'blockquote[data-callout]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const type = (node.attrs.type as string) || 'note'
    return [
      'blockquote',
      mergeAttributes(HTMLAttributes, {
        'data-callout': type,
        class: `callout callout-${type}`,
      }),
      0,
    ]
  },

  addCommands() {
    return {
      setCallout:
        (type: CalloutType = 'note') =>
        ({ commands }) =>
          commands.insertContent({
            type: this.name,
            attrs: { type },
            content: [
              {
                type: 'paragraph',
                content: [{ type: 'text', text: labelFor(type) }],
              },
              { type: 'paragraph' },
            ],
          }),
    }
  },
})

function labelFor(type: CalloutType): string {
  switch (type) {
    case 'tip':
      return 'Tip'
    case 'warning':
      return 'Warning'
    case 'info':
      return 'Info'
    case 'danger':
      return 'Danger'
    default:
      return 'Note'
  }
}
