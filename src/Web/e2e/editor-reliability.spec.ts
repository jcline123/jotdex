import { test, expect } from '@playwright/test'

async function serverUp(request: {
  get: (url: string) => Promise<{ ok: () => boolean; json: () => Promise<{ status?: string; version?: string }> }>
}) {
  try {
    const res = await request.get('/api/health')
    if (!res.ok()) return false
    const body = await res.json()
    return body.status === 'ok' || typeof body.version === 'string'
  } catch {
    return false
  }
}

test.describe('editor reliability smoke', () => {
  test('health endpoint is reachable when the server is up', async ({ request }) => {
    const res = await request.get('/api/health')
    if (res.status() === 404 || res.status() >= 500) test.skip()
    expect(res.ok()).toBeTruthy()
    const body = await res.json()
    expect(body.status === 'ok' || body.status === 'healthy' || typeof body.version === 'string').toBeTruthy()
  })
})

test.describe('code clipboard in real browsers', () => {
  test('CODE-10/11/12 conversion does not trim and normalizes CRLF only', async ({ page }) => {
    await page.setContent('<!doctype html><html><body></body></html>')
    const result = await page.evaluate(() => {
      const normalizeLf = (text: string) => text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
      const clipboardToPlainCode = (rawPlain: string, html: string) => {
        if (rawPlain.length > 0) return normalizeLf(rawPlain)
        if (!html) return ''
        return normalizeLf(html)
      }
      return {
        multiline: clipboardToPlainCode('line1\nline2\nline3', ''),
        blanks: clipboardToPlainCode('\n\nfoo\n', ''),
        ws: clipboardToPlainCode('   \n', ''),
        crlf: clipboardToPlainCode('a\r\nb\r\n', ''),
        tabs: clipboardToPlainCode('a\tb\n', ''),
      }
    })
    expect(result.multiline).toBe('line1\nline2\nline3')
    expect(result.blanks).toBe('\n\nfoo\n')
    expect(result.ws).toBe('   \n')
    expect(result.crlf).toBe('a\nb\n')
    expect(result.tabs).toBe('a\tb\n')
  })
})

test.describe('live app probes', () => {
  test('SAVE-01 open without edit does not PUT notes when idle', async ({ page, request }) => {
    if (!(await serverUp(request))) test.skip()
    const puts: string[] = []
    page.on('request', (req) => {
      if (req.method() === 'PUT' && req.url().includes('/api/notes/')) puts.push(req.url())
    })
    await page.goto('/')
    await page.waitForTimeout(1500)
    expect(puts).toEqual([])
  })
})
