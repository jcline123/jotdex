export type SnippetSummary = {
  noteId: string
  title: string
  trigger: string
  language: string
  folderPath: string
  relativePath: string
  description?: string | null
  code: string
  tags: string[]
}

type SnippetsResponse = {
  items?: Array<{
    noteId: string
    title: string
    trigger: string
    language: string
    folderPath: string
    relativePath: string
    description?: string | null
    code: string
    tags: string[]
  }>
}

function mapItem(raw: NonNullable<SnippetsResponse['items']>[number]): SnippetSummary {
  return {
    noteId: raw.noteId,
    title: raw.title,
    trigger: raw.trigger,
    language: raw.language,
    folderPath: raw.folderPath,
    relativePath: raw.relativePath,
    description: raw.description,
    code: raw.code,
    tags: raw.tags ?? [],
  }
}

export async function fetchSnippets(q?: string, language?: string): Promise<SnippetSummary[]> {
  const params = new URLSearchParams()
  if (q?.trim()) params.set('q', q.trim())
  if (language?.trim()) params.set('language', language.trim())
  params.set('limit', '100')
  const res = await fetch(`/api/snippets?${params.toString()}`, { credentials: 'same-origin' })
  const data = (await res.json()) as SnippetsResponse
  if (!res.ok) throw new Error('Could not load snippets')
  return (data.items ?? []).map(mapItem)
}

export async function deleteSnippet(noteId: string): Promise<void> {
  const res = await fetch(`/api/notes/${noteId}`, { method: 'DELETE', credentials: 'same-origin' })
  if (!res.ok) {
    const data = (await res.json().catch(() => ({}))) as { error?: string }
    throw new Error(data.error ?? 'Could not delete snippet')
  }
}

export async function updateSnippet(body: {
  noteId: string
  etag: string
  title: string
  trigger: string
  language: string
  code: string
  description?: string
  tags?: string[]
}): Promise<SnippetSummary> {
  const res = await fetch(`/api/snippets/${body.noteId}`, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: body.title,
      trigger: body.trigger,
      language: body.language,
      code: body.code,
      description: body.description,
      tags: body.tags,
      etag: body.etag,
    }),
  })
  const data = (await res.json()) as { snippet?: SnippetsResponse['items'] extends (infer U)[] | undefined ? U : never; error?: string }
  if (!res.ok || !data.snippet) throw new Error(data.error ?? 'Could not save snippet')
  return mapItem(data.snippet)
}

export async function fetchSnippetEtag(noteId: string): Promise<string> {
  const res = await fetch(`/api/notes/${noteId}`, { credentials: 'same-origin' })
  if (!res.ok) throw new Error('Could not load snippet')
  const data = (await res.json()) as { etag?: string }
  return data.etag ?? ''
}

export async function createSnippet(body: {
  title: string
  trigger: string
  language: string
  code: string
  folder: string
  description?: string
  tags?: string[]
}): Promise<SnippetSummary> {
  const res = await fetch('/api/snippets', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  const data = (await res.json()) as { snippet?: SnippetsResponse['items'] extends (infer U)[] | undefined ? U : never; error?: string }
  if (!res.ok || !data.snippet) throw new Error(data.error ?? 'Could not save snippet')
  return mapItem(data.snippet)
}
