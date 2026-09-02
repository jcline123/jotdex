import { defineConfig, devices } from '@playwright/test'

const isolated = process.env.JOTDEX_E2E_ISOLATED === '1'
const authFile = 'playwright/.auth/user.json'

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  fullyParallel: false,
  retries: 0,
  use: {
    baseURL: process.env.JOTDEX_E2E_BASE ?? 'http://127.0.0.1:5180',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: { ...devices['Desktop Chrome'] } },
    {
      name: 'chromium',
      testIgnore: /auth\.setup\.ts/,
      dependencies: isolated ? ['setup'] : [],
      use: { ...devices['Desktop Chrome'], ...(isolated ? { storageState: authFile } : {}) },
    },
    {
      name: 'firefox',
      testIgnore: /auth\.setup\.ts/,
      dependencies: isolated ? ['setup'] : [],
      use: { ...devices['Desktop Firefox'], ...(isolated ? { storageState: authFile } : {}) },
    },
    {
      name: 'webkit',
      testIgnore: /auth\.setup\.ts/,
      dependencies: isolated ? ['setup'] : [],
      use: { ...devices['Desktop Safari'], ...(isolated ? { storageState: authFile } : {}) },
    },
    {
      name: 'mobile',
      testIgnore: /auth\.setup\.ts/,
      dependencies: isolated ? ['setup'] : [],
      use: { ...devices['Pixel 5'], ...(isolated ? { storageState: authFile } : {}) },
    },
  ],
})
