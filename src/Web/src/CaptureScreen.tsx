import { useState } from 'react'

type Props = {
  onOpenNote?: (id: string) => void
}

export function CaptureScreen({ onOpenNote }: Props) {
  const [title, setTitle] = useState('')
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [hint, setHint] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [file, setFile] = useState<File | null>(null)

  const origin = typeof window !== 'undefined' ? window.location.origin : ''
  const bookmarklet = `javascript:(function(){var t=document.title||'Clip',u=location.href,s=String(window.getSelection()||'');fetch(${JSON.stringify(origin + '/api/clip')},{method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify({title:t,sourceUrl:u,text:s||t})}).then(async function(r){var d=await r.json();alert(r.ok?'Saved to Jotdex Inbox':'Clip failed: '+(d.error||r.status));}).catch(function(e){alert(String(e));});})();`

  async function save(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setHint(null)
    try {
      const res = await fetch('/api/clip', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: title.trim() || undefined,
          text: text.trim(),
        }),
      })
      const data = await res.json()
      if (!res.ok || !data.success) throw new Error(data.error ?? 'Save failed')
      const noteId = data.noteId as string
      if (file && noteId) {
        const fd = new FormData()
        fd.append('file', file)
        const up = await fetch(`/api/notes/${noteId}/attachments`, {
          method: 'POST',
          credentials: 'same-origin',
          body: fd,
        })
        if (!up.ok) {
          setHint('Note saved, but attachment upload failed.')
        } else {
          setHint('Saved to Inbox with attachment.')
        }
      } else {
        setHint('Saved to Inbox.')
      }
      setTitle('')
      setText('')
      setFile(null)
      if (noteId && onOpenNote) onOpenNote(noteId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="capture-screen">
      <div className="capture-card">
        <p className="brand">Jotdex</p>
        <h1>Quick capture</h1>
        <p className="muted">Saves to the vault <code>Inbox</code> folder. Ideal for phone-on-VPN scribbles and photos.</p>
        <form className="auth-form" onSubmit={(e) => void save(e)}>
          <label>
            Title (optional)
            <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Auto-named if blank" />
          </label>
          <label>
            Note
            <textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              rows={8}
              required
              placeholder="What do you need to remember?"
            />
          </label>
          <label>
            Photo or file (optional)
            <input
              type="file"
              accept="image/*,.pdf,.txt,.log,.md"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </label>
          {error && <p className="error">{error}</p>}
          {hint && <p className="muted">{hint}</p>}
          <button type="submit" disabled={busy || !text.trim()}>
            {busy ? 'Saving…' : 'Save to Inbox'}
          </button>
        </form>
        <div className="capture-bookmarklet">
          <h2>Bookmarklet</h2>
          <p className="muted">
            Drag this link to your bookmarks bar (desktop). On a web page, click it to save the selection (or title) into
            Jotdex Inbox. You must already be signed in to Jotdex in this browser.
          </p>
          <a className="capture-bookmarklet-link" href={bookmarklet}>
            Save to Jotdex
          </a>
        </div>
        <p className="muted">
          <a href="/">← Full app</a>
        </p>
      </div>
    </div>
  )
}
