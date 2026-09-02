export const UNICODE_EMOJI: { ch: string; name: string }[] = [
  { ch: '✅', name: 'check' },
  { ch: '❌', name: 'x' },
  { ch: '⚠️', name: 'warning' },
  { ch: 'ℹ️', name: 'info' },
  { ch: '⭐', name: 'star' },
  { ch: '🔥', name: 'fire' },
  { ch: '💡', name: 'idea' },
  { ch: '📝', name: 'memo' },
  { ch: '📌', name: 'pin' },
  { ch: '🔗', name: 'link' },
  { ch: '🛠️', name: 'tools' },
  { ch: '⚙️', name: 'gear' },
  { ch: '🔒', name: 'lock' },
  { ch: '🔑', name: 'key' },
  { ch: '📦', name: 'package' },
  { ch: '🖥️', name: 'desktop' },
  { ch: '📁', name: 'folder' },
  { ch: '📄', name: 'page' },
  { ch: '➡️', name: 'arrow' },
  { ch: '⏳', name: 'hourglass' },
]

export function filterEmoji(query: string): { ch: string; name: string }[] {
  const q = query.trim().toLowerCase()
  if (!q) return UNICODE_EMOJI
  return UNICODE_EMOJI.filter((e) => e.name.includes(q) || e.ch.includes(q))
}
