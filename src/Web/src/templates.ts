export type NoteTemplate = {
  id: string
  name: string
  description: string
  body: (title: string) => string
}

export const NOTE_TEMPLATES: NoteTemplate[] = [
  {
    id: 'blank',
    name: 'Blank',
    description: 'Title only',
    body: (title) => `# ${title}\n\n`,
  },
  {
    id: 'meeting',
    name: 'Meeting',
    description: 'Attendees, notes, actions',
    body: (title) =>
      `# ${title}\n\n**Date:** ${today()}\n\n## Attendees\n\n- \n\n## Notes\n\n\n## Action items\n\n- [ ] \n`,
  },
  {
    id: 'howto',
    name: 'How-to',
    description: 'Steps + troubleshooting',
    body: (title) =>
      `# ${title}\n\n## Goal\n\n\n## Steps\n\n1. \n2. \n3. \n\n## Notes\n\n\n## Troubleshooting\n\n`,
  },
  {
    id: 'incident',
    name: 'Incident / ticket',
    description: 'Symptoms, fix, follow-up',
    body: (title) =>
      `# ${title}\n\n**Date:** ${today()}\n\n## Symptoms\n\n\n## Cause\n\n\n## Fix\n\n\n## Follow-up\n\n- [ ] \n`,
  },
  {
    id: 'daily',
    name: 'Daily note',
    description: 'Quick day log',
    body: (title) => `# ${title}\n\n## Focus\n\n- [ ] \n\n## Notes\n\n\n## Tomorrow\n\n- [ ] \n`,
  },
]

function today(): string {
  const d = new Date()
  return d.toISOString().slice(0, 10)
}
