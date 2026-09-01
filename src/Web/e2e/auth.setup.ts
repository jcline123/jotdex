import { test as setup, expect } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'

const authFile = 'playwright/.auth/user.json'

setup('authenticate ephemeral instance', async ({ request, baseURL }) => {
  const isolated = process.env.JOTDEX_E2E_ISOLATED === '1'
  if (!isolated) {
    setup.skip()
    return
  }
  const status = await request.get('/api/auth/status')
  expect(status.ok()).toBeTruthy()
  const body = await status.json()
  if (!body.passwordSet) {
    const password = `e2e-${crypto.randomUUID()}`
    const setupRes = await request.post('/api/auth/setup', {
      data: { username: 'admin', password, displayName: 'E2E' },
    })
    expect(setupRes.ok()).toBeTruthy()
    process.env.JOTDEX_E2E_PASSWORD = password
  }
  mkdirSync(dirname(authFile), { recursive: true })
  const storage = await request.storageState()
  writeFileSync(authFile, JSON.stringify(storage, null, 2))
  expect(baseURL).toBeTruthy()
})
