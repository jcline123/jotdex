import { exactSaveEqual } from '../markdown/documentSameness'

export type SaveRequest = {
  noteId: string
  noteSessionId: string
  revision: number
  markdown: string
  etag: string
  force?: boolean
}

export type SaveResponse = {
  ok: boolean
  status: number
  etag?: string
  markdown?: string
  conflict?: boolean
  error?: string
  note?: unknown
}

export type SaveCoordinatorHooks = {
  put: (req: SaveRequest) => Promise<SaveResponse>
  onStatus: (status: 'saving' | 'saved' | 'conflict' | 'error' | 'editing') => void
  onEtag: (etag: string, revision: number) => void
  onConflict: (res: SaveResponse) => void
  onError: (message: string) => void
  getLatest: () => { noteId: string; noteSessionId: string; revision: number; markdown: string; etag: string }
  sameDocument?: (a: string, b: string) => boolean
}

export class SaveCoordinator {
  private inFlight = false
  private queued: SaveRequest | null = null
  lastSavedRevision = 0
  private readonly hooks: SaveCoordinatorHooks
  private readonly same: (a: string, b: string) => boolean

  constructor(hooks: SaveCoordinatorHooks) {
    this.hooks = hooks
    this.same = hooks.sameDocument ?? exactSaveEqual
  }

  enqueue(req: SaveRequest): void {
    if (this.inFlight) {
      this.queued = req
      return
    }
    void this.send(req)
  }

  async send(req: SaveRequest): Promise<void> {
    this.inFlight = true
    this.hooks.onStatus('saving')
    try {
      const res = await this.hooks.put(req)
      const latest = this.hooks.getLatest()
      if (req.noteId !== latest.noteId || req.noteSessionId !== latest.noteSessionId) return

      if (res.status === 401) {
        this.hooks.onStatus('saved')
        return
      }

      if (res.status === 409 || res.conflict) {
        const disk = res.markdown ?? ''
        if (this.same(disk, req.markdown)) {
          if (res.etag) this.hooks.onEtag(res.etag, req.revision)
          if (latest.revision === req.revision) {
            this.lastSavedRevision = req.revision
            this.hooks.onStatus('saved')
          }
          return
        }
        this.hooks.onConflict(res)
        this.hooks.onStatus('conflict')
        return
      }

      if (!res.ok) {
        this.hooks.onError(res.error ?? 'Save failed')
        this.hooks.onStatus('error')
        return
      }

      if (res.etag) this.hooks.onEtag(res.etag, req.revision)
      if (latest.revision === req.revision) {
        this.lastSavedRevision = req.revision
        this.hooks.onStatus('saved')
      } else {
        this.hooks.onStatus('editing')
        const n = this.hooks.getLatest()
        this.queued = {
          noteId: n.noteId,
          noteSessionId: n.noteSessionId,
          revision: n.revision,
          markdown: n.markdown,
          etag: res.etag ?? n.etag,
        }
      }
    } finally {
      this.inFlight = false
      const next = this.queued
      this.queued = null
      if (next) this.enqueue(next)
    }
  }
}
