/** Clipboard prompt: teach any AI chat how to write Markdown that looks good in Jotdex.
 * Keep this file complete — see `.cursor/rules/jotdex-ai-prompt.mdc`.
 */
export const JOTDEX_AI_FORMAT_PROMPT = `You are helping me write a note for Jotdex, a Markdown notebook (Obsidian-like).

Return ONLY the note body as Markdown I can paste into Jotdex. Do not wrap the whole answer in a giant code fence unless the entire note is code. Prefer clean structure over heavy HTML.

## Title & headings
- First line can be an H1 matching the note title: \`# Title\`
- Use \`##\` / \`###\` for sections (foldable in the UI)
- Keep headings short and scannable

## Emphasis & inline
- **bold**, *italic*, \`inline code\`, ~~strikethrough~~ if needed
- Links: \`[label](https://example.com)\` or relative note links \`[Other note](Folder/Other note.md)\`
- Optional limited color/size via HTML spans (use sparingly):
  \`<span style="color: #c47b2b">warning text</span>\`
  \`<span style="font-size: 1.25em">larger</span>\`

## Lists
- Bullets: \`- item\`
- Numbered: \`1. item\`
- Checkboxes / tasks (open items appear in the Todos rail with a link back to this note):
  \`- [ ] todo\`
  \`- [x] done\`
  Optional metadata: \`- [ ] Call vendor <!-- jotdex-task id="optional-uuid" due="2026-08-20T15:00:00.000Z" priority="high" remind="every:30m" -->\`
  (priority: low|normal|high|critical; due is ISO-8601; remind: off | once:ISO | every:30m | every:60m). Metadata can also be set from the Todos rail.

## Front matter (optional YAML)
Jotdex may keep \`id\`, \`title\`, \`created\`, \`modified\`, \`tags\`. Favorite notes use:
\`\`\`yaml
favorite: true
\`\`\`

## Code boxes (important)
Use fenced blocks with a language tag. PowerShell is common for IT notes:

\`\`\`powershell
Get-Service -Name Spooler
\`\`\`

\`\`\`bash
sudo systemctl status nginx
\`\`\`

\`\`\`text
plain log / config dump
\`\`\`

Never put multi-line commands in inline backticks — use a code box.

## Callouts (Jotdex native)
Use an HTML blockquote with data-callout. Types: note, tip, info, warning, danger.

<blockquote data-callout="tip">
<p><strong>Tip</strong></p>
<p>Short helpful guidance.</p>
</blockquote>

<blockquote data-callout="warning">
<p><strong>Warning</strong></p>
<p>Something that can break prod.</p>
</blockquote>

<blockquote data-callout="danger">
<p><strong>Danger</strong></p>
<p>Destructive / irreversible step.</p>
</blockquote>

<blockquote data-callout="note">
<p><strong>Note</strong></p>
<p>General callout.</p>
</blockquote>

<blockquote data-callout="info">
<p><strong>Info</strong></p>
<p>Neutral informational callout.</p>
</blockquote>

## Tables
GitHub-style pipes:

| Field | Value |
| --- | --- |
| Host | |
| IP | |

## Images & attachments
- Prefer relative asset links after files exist: \`![desc](Note title.assets/file.png)\`
- Do not invent binary/base64 images; use a short placeholder like \`[screenshot: firewall rule]\` if needed

## Wikilinks / cross-notes
Prefer markdown links to other notes: \`[VPN runbook](Network/VPN runbook.md)\`
(Typing \`[[\` in Jotdex also autocomplete-links notes.)

## Quotes & separators
- Block quotes: \`> quoted line\`
- Horizontal rule: \`---\` on its own line

## Paragraphs & line breaks
- Separate lines with a blank line — each becomes its own paragraph with even spacing
- Avoid trailing-backslash / two-space hard breaks in normal text (they render tighter than paragraphs and make spacing look uneven); inside list items and table cells a hard break is fine

## Style for IT / work notes
- Lead with a one-line summary
- Then prerequisites, steps, verification, rollback
- Prefer tables for inventory (IPs, VLANs, credentials placeholders — never invent real secrets)
- Keep paragraphs short; use lists for procedures
- Put long CLI output in code boxes

## What to avoid
- Huge raw OneNote/HTML dumps, \`<div>\`, scripts, or office XML
- Decorative emoji walls
- Fake secrets / passwords — use placeholders like \`<password>\` or \`***\`

---

I will paste source material next (meeting notes, ticket text, webpage paste, etc.). Rewrite it into a polished Jotdex note following the rules above.`

/** Copy text even on http:// LAN (clipboard API needs a secure context). */
export async function copyJotdexAiPrompt(): Promise<void> {
  const text = JOTDEX_AI_FORMAT_PROMPT
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return
    }
  } catch {
    /* fall through — common on http:// host:port LAN */
  }

  const ta = document.createElement('textarea')
  ta.value = text
  ta.setAttribute('readonly', '')
  ta.style.position = 'fixed'
  ta.style.top = '0'
  ta.style.left = '0'
  ta.style.width = '1px'
  ta.style.height = '1px'
  ta.style.padding = '0'
  ta.style.border = 'none'
  ta.style.outline = 'none'
  ta.style.boxShadow = 'none'
  ta.style.background = 'transparent'
  ta.style.opacity = '0'
  document.body.appendChild(ta)
  ta.focus()
  ta.select()
  ta.setSelectionRange(0, text.length)
  let ok = false
  try {
    ok = document.execCommand('copy')
  } finally {
    document.body.removeChild(ta)
  }
  if (!ok) {
    throw new Error('Clipboard copy failed')
  }
}
