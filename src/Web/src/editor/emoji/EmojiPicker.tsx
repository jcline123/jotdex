import { filterEmoji } from './emojiList'

type Props = {
  query: string
  onQuery: (q: string) => void
  onPick: (ch: string) => void
  onClose: () => void
}

export function EmojiPicker({ query, onQuery, onPick, onClose }: Props) {
  const items = filterEmoji(query)
  return (
    <div className="jotdex-emoji-picker" role="dialog" aria-label="Emoji">
      <input
        autoFocus
        value={query}
        placeholder="Search emoji"
        aria-label="Search emoji"
        onChange={(e) => onQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') onClose()
          if (e.key === 'Enter' && items[0]) {
            e.preventDefault()
            onPick(items[0].ch)
          }
        }}
      />
      <div className="jotdex-emoji-grid">
        {items.map((e) => (
          <button key={e.name} type="button" title={e.name} onClick={() => onPick(e.ch)}>
            {e.ch}
          </button>
        ))}
      </div>
    </div>
  )
}
