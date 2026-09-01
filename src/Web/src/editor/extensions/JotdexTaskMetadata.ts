import { Node, mergeAttributes } from '@tiptap/core'
import type { JSONContent, MarkdownToken } from '@tiptap/core'

export const JOTDEX_TASK_META = 'jotdexTaskMetadata'

function parseAttrs(raw: string): Record<string, string> {
  const out: Record<string, string> = {}
  const re = /(\w+)="([^"]*)"/g
  let m: RegExpExecArray | null
  while ((m = re.exec(raw))) out[m[1]!] = m[2]!
  return out
}

export const JotdexTaskMetadata = Node.create({
  name: JOTDEX_TASK_META,
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,

  addAttributes() {
    return {
      kind: { default: 'task' },
      raw: { default: '' },
      id: { default: '' },
      priority: { default: '' },
      due: { default: '' },
      remind: { default: '' },
    }
  },

  parseHTML() {
    return [{ tag: 'span[data-jotdex-task-meta]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-jotdex-task-meta': HTMLAttributes.kind,
        contenteditable: 'false',
        class: 'jotdex-task-meta',
      }),
    ]
  },

  parseMarkdown: (token: MarkdownToken) => {
    const t = token as MarkdownToken & { kind?: string; attrRaw?: string; raw?: string }
    const raw = String(t.raw ?? '')
    const m = /<!--\s*(jotdex-task|jotdex-todo)\s+([^>]*)-->/.exec(raw)
    const kind = t.kind === 'todo' || m?.[1] === 'jotdex-todo' ? 'todo' : 'task'
    const attrs = parseAttrs(String(t.attrRaw ?? m?.[2] ?? ''))
    return {
      type: JOTDEX_TASK_META,
      attrs: {
        kind,
        raw: raw || `<!-- jotdex-${kind} -->`,
        id: attrs.id ?? '',
        priority: attrs.priority ?? '',
        due: attrs.due ?? '',
        remind: attrs.remind ?? '',
      },
    }
  },

  markdownTokenizer: {
    name: JOTDEX_TASK_META,
    level: 'inline',
    start: (src: string) => {
      const hits = ['<!--', '{jotdex-task', '{jotdex-todo'].map((s) => src.indexOf(s)).filter((i) => i >= 0)
      return hits.length ? Math.min(...hits) : -1
    },
    tokenize(src: string) {
      const m =
        /^<!--\s*(jotdex-task|jotdex-todo)\s+([^>]*)-->/.exec(src) ??
        /^\{(jotdex-task|jotdex-todo)\s+([^}]*)\}/.exec(src)
      if (!m) return
      const kind = m[1] === 'jotdex-todo' ? 'todo' : 'task'
      return {
        type: JOTDEX_TASK_META,
        raw: `<!-- ${m[1]} ${m[2] ?? ''} -->`,
        kind,
        attrRaw: m[2] ?? '',
      }
    },
  },

  renderMarkdown: (node: JSONContent) => {
    const raw = String(node.attrs?.raw ?? '')
    if (raw.startsWith('<!--')) return raw
    const kind = node.attrs?.kind === 'todo' ? 'jotdex-todo' : 'jotdex-task'
    const parts = [`<!-- ${kind}`]
    for (const key of ['id', 'priority', 'due', 'remind']) {
      const v = node.attrs?.[key]
      if (v) parts.push(` ${key}="${v}"`)
    }
    parts.push(' -->')
    return parts.join('')
  },
})
