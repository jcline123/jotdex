import { useId, useState } from 'react'

export type NoteMetaFields = {
  created?: string | null
  modified?: string | null
  createdVia?: string | null
  createdBy?: string | null
  updatedVia?: string | null
  updatedBy?: string | null
  lastApiUpdateAt?: string | null
  lastApiUpdateBy?: string | null
  provenanceInferred?: boolean
}

function parseWhen(iso?: string | null): Date | null {
  if (!iso) return null
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? null : d
}

function formatShort(d: Date): string {
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

function formatExact(d: Date): string {
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
    timeZoneName: 'short',
  })
}

function viaLabel(via?: string | null, by?: string | null): string {
  if (!via || via === 'unknown') return ''
  if (via === 'api') return by ? ` via API (${by})` : ' via API'
  if (via === 'ui') return ''
  if (via === 'import') return ' (imported)'
  if (via === 'external') return ' (edited outside Jotdex)'
  return ''
}

export function NoteMetadata({ meta }: { meta: NoteMetaFields }) {
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const created = parseWhen(meta.created)
  const modified = parseWhen(meta.modified)
  const lastApi = parseWhen(meta.lastApiUpdateAt)

  const createdText = created ? formatShort(created) : 'unavailable'
  const updatedText = modified ? formatShort(modified) : 'unavailable'
  const via = viaLabel(meta.updatedVia, meta.updatedBy)

  return (
    <div className="note-meta">
      <button
        type="button"
        className="note-meta-line"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((v) => !v)}
        title="Show exact timestamps"
      >
        <span>
          Created{' '}
          {created ? (
            <time dateTime={created.toISOString()}>{createdText}</time>
          ) : (
            createdText
          )}
          {' · '}
          Updated{' '}
          {modified ? (
            <time dateTime={modified.toISOString()}>{updatedText}</time>
          ) : (
            updatedText
          )}
          {via}
        </span>
      </button>
      {open && (
        <div id={panelId} className="note-meta-detail" role="region" aria-label="Note timestamps">
          <p>
            <strong>Created:</strong>{' '}
            {created ? formatExact(created) : 'unavailable'}
            {meta.createdVia && meta.createdVia !== 'unknown'
              ? ` · ${meta.createdVia}${meta.createdBy ? ` (${meta.createdBy})` : ''}`
              : ''}
          </p>
          <p>
            <strong>Updated:</strong>{' '}
            {modified ? formatExact(modified) : 'unavailable'}
            {meta.updatedVia
              ? ` · ${meta.updatedVia}${meta.updatedBy ? ` (${meta.updatedBy})` : ''}`
              : ''}
          </p>
          {lastApi && meta.updatedVia !== 'api' && (
            <p>
              <strong>Last API edit:</strong> {formatExact(lastApi)}
              {meta.lastApiUpdateBy ? ` (${meta.lastApiUpdateBy})` : ''}
            </p>
          )}
          {meta.provenanceInferred && (
            <p className="note-meta-inferred">Some provenance was inferred from disk content.</p>
          )}
        </div>
      )}
    </div>
  )
}
