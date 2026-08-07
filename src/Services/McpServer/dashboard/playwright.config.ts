import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: { timeout: 8_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    ...devices['Desktop Edge'],
    baseURL: 'http://127.0.0.1:4173/dashboard/',
    channel: process.platform === 'win32' ? 'msedge' : undefined,
    colorScheme: 'light',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 4173',
    url: 'http://127.0.0.1:4173/dashboard/',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
})
