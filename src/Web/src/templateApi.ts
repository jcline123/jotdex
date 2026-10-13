export type VaultTemplateSummary = {
  noteId: string
  title: string
  folderPath: string
  relativePath: string
  description?: string | null
  tags: string[]
  modified?: string | null
  hasAttachments: boolean
}

async function readError(res: Response): Promise<string> {
  try {
    const data = await res.json()
    return (data?.error as string) || res.statusText || 'Request failed'
  } catch {
    return res.statusText || 'Request failed'
  }
}

export async function fetchTemplates(query?: string): Promise<VaultTemplateSummary[]> {
  const qs = query?.trim() ? `?q=${encodeURIComponent(query.trim())}` : ''
  const res = await fetch(`/api/templates${qs}`)
  if (!res.ok) throw new Error(await readError(res))
  const data = await res.json()
  return (data.items ?? []) as VaultTemplateSummary[]
}

export async function createTemplate(input: {
  title: string
  markdown?: string
  description?: string
}): Promise<VaultTemplateSummary> {
  const res = await fetch('/api/templates', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!res.ok) throw new Error(await readError(res))
  const data = await res.json()
  return data.template as VaultTemplateSummary
}

export async function saveNoteAsTemplate(input: {
  noteId: string
  title?: string
  description?: string
}): Promise<VaultTemplateSummary> {
  const res = await fetch('/api/templates/from-note', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  if (!res.ok) throw new Error(await readError(res))
  const data = await res.json()
  return data.template as VaultTemplateSummary
}

export async function instantiateTemplate(input: {
  templateId: string
  title: string
  folder?: string
}): Promise<{ id: string }> {
  const res = await fetch(`/api/templates/${input.templateId}/instantiate`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title: input.title, folder: input.folder ?? '' }),
  })
  if (!res.ok) throw new Error(await readError(res))
  return (await res.json()) as { id: string }
}

export async function prepareTemplateInsert(
  templateId: string,
  targetNoteId: string,
): Promise<{ markdown: string; note?: { etag?: string; attachments?: unknown[] } }> {
  const res = await fetch(`/api/templates/${templateId}/prepare-insert`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetNoteId }),
  })
  if (!res.ok) throw new Error(await readError(res))
  const data = await res.json()
  return { markdown: data.markdown as string, note: data.note }
}

export async function deleteTemplate(templateId: string): Promise<void> {
  const res = await fetch(`/api/templates/${templateId}`, { method: 'DELETE' })
  if (!res.ok) throw new Error(await readError(res))
}
