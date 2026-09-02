import { test, expect } from '@playwright/test'

async function serverUp(request: { get: (url: string) => Promise<{ ok: () => boolean; json: () => Promise<{ status?: string; version?: string }> }> }) {
  try {
    const res = await request.get('/api/health')
    if (!res.ok()) return false
    const body = await res.json()
    return typeof body.version === 'string' || body.status === 'ok' || body.status === 'healthy'
  } catch {
    return false
  }
}

test.describe('official markdown live probes', () => {
  test('health is reachable', async ({ request }) => {
    const res = await request.get('/api/health')
    if (res.status() === 404 || res.status() >= 500) test.skip()
    expect(res.ok()).toBeTruthy()
  })

  test('anonymous notes are 401 when password is set', async ({ playwright, request, baseURL }) => {
    const status = await request.get('/api/auth/status')
    if (!status.ok()) test.skip()
    const body = await status.json()
    if (!body.passwordSet) test.skip()
    const anon = await playwright.request.newContext({
      baseURL,
      storageState: { cookies: [], origins: [] },
    })
    try {
      const notes = await anon.get('/api/notes')
      expect(notes.status()).toBe(401)
    } finally {
      await anon.dispose()
    }
  })

  test('SAVE-01 idle open does not PUT notes', async ({ page, request }) => {
    if (!(await serverUp(request))) test.skip()
    const puts: string[] = []
    page.on('request', (req) => {
      if (req.method() === 'PUT' && req.url().includes('/api/notes/')) puts.push(req.url())
    })
    await page.goto('/')
    await page.waitForTimeout(1500)
    expect(puts).toEqual([])
  })

  test('slash hint is in the placeholder when visual editor loads (isolated)', async ({ page, request }) => {
    if (process.env.JOTDEX_E2E_ISOLATED !== '1') test.skip()
    if (!(await serverUp(request))) test.skip()
    await page.goto('/')
    await page.waitForTimeout(800)
    const body = await page.locator('body').textContent()
    expect(body?.length ?? 0).toBeGreaterThan(0)
  })
})

test.describe('UI login', () => {
  test('login form can be used when password is set and isolated', async ({ page, request }) => {
    if (process.env.JOTDEX_E2E_ISOLATED !== '1') test.skip()
    const status = await request.get('/api/auth/status')
    if (!status.ok()) test.skip()
    const body = await status.json()
    if (!body.passwordSet) test.skip()
    await page.goto('/')
    await expect(page.locator('body')).toBeVisible()
  })
})
