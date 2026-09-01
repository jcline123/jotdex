import { describe, expect, it } from 'vitest'
import { SaveCoordinator } from '../revisions/SaveCoordinator'

describe('SaveCoordinator', () => {
  it('SAVE-03 older response cannot mark a newer revision saved', async () => {
    let latestRevision = 1
    const coord = new SaveCoordinator({
      put: async (req) => {
        if (req.revision === 1) {
          await new Promise((r) => setTimeout(r, 30))
          return { ok: true, status: 200, etag: 'e1' }
        }
        return { ok: true, status: 200, etag: 'e2' }
      },
      onStatus: () => {},
      onEtag: () => {},
      onConflict: () => {},
      onError: () => {},
      getLatest: () => ({
        noteId: 'n',
        noteSessionId: 's',
        revision: latestRevision,
        markdown: 'new',
        etag: 'e0',
      }),
    })
    const p1 = coord.send({
      noteId: 'n',
      noteSessionId: 's',
      revision: 1,
      markdown: 'old',
      etag: 'e0',
    })
    latestRevision = 2
    await p1
    expect(coord.lastSavedRevision).not.toBe(1)
  })

  it('SAVE-02 typing during a slow save keeps the newer revision dirty then saves it', async () => {
    const puts: number[] = []
    let latestRevision = 1
    let latestMarkdown = 'one'
    let status = ''
    const coord = new SaveCoordinator({
      put: async (req) => {
        puts.push(req.revision)
        if (req.revision === 1) await new Promise((r) => setTimeout(r, 25))
        return { ok: true, status: 200, etag: `e${req.revision}` }
      },
      onStatus: (s) => {
        status = s
      },
      onEtag: () => {},
      onConflict: () => {},
      onError: () => {},
      getLatest: () => ({
        noteId: 'n',
        noteSessionId: 's',
        revision: latestRevision,
        markdown: latestMarkdown,
        etag: 'e0',
      }),
    })
    const p1 = coord.send({
      noteId: 'n',
      noteSessionId: 's',
      revision: 1,
      markdown: 'one',
      etag: 'e0',
    })
    latestRevision = 2
    latestMarkdown = 'two'
    await p1
    await new Promise((r) => setTimeout(r, 40))
    expect(puts).toContain(1)
    expect(puts).toContain(2)
    expect(coord.lastSavedRevision).toBe(2)
    expect(status).toBe('saved')
  })

  it('SAVE-02/SAVE-03 stress 50x', async () => {
    for (let i = 0; i < 50; i++) {
      let latestRevision = 1
      const coord = new SaveCoordinator({
        put: async (req) => {
          if (req.revision === 1) await new Promise((r) => setTimeout(r, 2))
          return { ok: true, status: 200, etag: 'e' }
        },
        onStatus: () => {},
        onEtag: () => {},
        onConflict: () => {},
        onError: () => {},
        getLatest: () => ({
          noteId: 'n',
          noteSessionId: 's',
          revision: latestRevision,
          markdown: 'x',
          etag: 'e0',
        }),
      })
      const p1 = coord.send({
        noteId: 'n',
        noteSessionId: 's',
        revision: 1,
        markdown: 'old',
        etag: 'e0',
      })
      latestRevision = 2
      await p1
      expect(coord.lastSavedRevision).not.toBe(1)
      await new Promise((r) => setTimeout(r, 8))
      expect(coord.lastSavedRevision).toBe(2)
    }
  })

  it('SAVE-05 true conflict is surfaced', async () => {
    let status = ''
    let conflict = false
    const coord = new SaveCoordinator({
      put: async () => ({ ok: false, status: 409, conflict: true, markdown: 'disk' }),
      onStatus: (s) => {
        status = s
      },
      onEtag: () => {},
      onConflict: () => {
        conflict = true
      },
      onError: () => {},
      getLatest: () => ({
        noteId: 'n',
        noteSessionId: 's',
        revision: 1,
        markdown: 'local',
        etag: 'e0',
      }),
    })
    await coord.send({
      noteId: 'n',
      noteSessionId: 's',
      revision: 1,
      markdown: 'local',
      etag: 'e0',
    })
    expect(conflict).toBe(true)
    expect(status).toBe('conflict')
  })
})
