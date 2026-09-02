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
      title: {
        default: '',
        parseHTML: (element) => element.getAttribute('data-callout-title') || '',
        renderHTML: (attributes) =>
          attributes.title ? { 'data-callout-title': attributes.title } : {},
      },
      collapse: {
        default: null as null | 'collapsed' | 'expanded',
        parseHTML: (element) => {
          const v = element.getAttribute('data-callout-collapse')
          if (v === 'collapsed' || v === 'expanded') return v
          return null
        },
        renderHTML: (attributes) =>
          attributes.collapse ? { 'data-callout-collapse': attributes.collapse } : {},
      },
    }
  },

  parseHTML() {
    return [{ tag: 'blockquote[data-callout]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    const type = (node.attrs.type as string) || 'note'
    const title = String(node.attrs.title ?? '')
    const collapse = node.attrs.collapse as string | null
    if (collapse === 'collapsed' || collapse === 'expanded') {
      const open = collapse === 'expanded' ? { open: '' } : {}
      return [
        'details',
        mergeAttributes(HTMLAttributes, open, {
          'data-callout': type,
          class: `callout callout-${type} callout-collapsible`,
        }),
        ['summary', { class: 'callout-summary' }, title || labelFor(type as CalloutType)],
        ['div', { class: 'callout-body' }, 0],
      ]
    }
    return [
      'blockquote',
      mergeAttributes(HTMLAttributes, {
        'data-callout': type,
        class: `callout callout-${type}`,
        ...(title ? { 'data-callout-title': title } : {}),
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
