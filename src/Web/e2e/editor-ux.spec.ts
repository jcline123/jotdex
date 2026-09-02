import { test, expect, type Page, type APIRequestContext } from '@playwright/test'

async function serverUp(request: APIRequestContext) {
  try {
    const res = await request.get('/api/health')
    if (!res.ok()) return false
    const body = await res.json()
    return typeof body.version === 'string' || body.status === 'ok' || body.status === 'healthy'
  } catch {
    return false
  }
}

async function openSampleNote(page: Page) {
  await page.goto('/')
  const named = page.getByRole('button', { name: /Nitrogen Cycle/i }).first()
  if (await named.count()) {
    await named.click()
  } else {
    await page.getByRole('button', { name: 'New note' }).first().click()
    await page.getByRole('button', { name: 'Create note' }).click()
  }
  await expect(page.locator('.tiptap-editor').first()).toBeVisible({ timeout: 15_000 })
  const auto = page.getByRole('button', { name: 'Auto' }).first()
  if (await auto.isVisible().catch(() => false)) await auto.click()
  await expect(page.getByRole('button', { name: 'Insert block' })).toBeVisible({ timeout: 15_000 })
}

test.describe('editor UX 1.3.0', () => {
  test('Insert toolbar is available in the visual editor', async ({ page, request }) => {
    if (process.env.JOTDEX_E2E_ISOLATED !== '1') test.skip()
    if (!(await serverUp(request))) test.skip()
    await openSampleNote(page)
    await expect(page.getByRole('button', { name: 'Insert block' })).toBeVisible()
  })

  test('Insert toolbar opens the command list', async ({ page, request }) => {
    if (process.env.JOTDEX_E2E_ISOLATED !== '1') test.skip()
    if (!(await serverUp(request))) test.skip()
    await openSampleNote(page)
    await page.getByRole('button', { name: 'Insert block' }).click()
    await expect(page.getByRole('listbox', { name: 'Insert' })).toBeVisible()
    await expect(page.getByRole('option', { name: 'Heading 1' })).toBeVisible()
  })

  test('mobile viewport keeps Insert at 44px', async ({ page, request }, testInfo) => {
    if (process.env.JOTDEX_E2E_ISOLATED !== '1') test.skip()
    if (!(await serverUp(request))) test.skip()
    if (testInfo.project.name !== 'mobile') {
      await page.setViewportSize({ width: 390, height: 844 })
    }
    await openSampleNote(page)
    const insert = page.getByRole('button', { name: 'Insert block' })
    await expect(insert).toBeVisible()
    const box = await insert.boundingBox()
    expect(box).toBeTruthy()
    expect(box!.height).toBeGreaterThanOrEqual(40)
    expect(box!.width).toBeGreaterThanOrEqual(40)
  })
})
