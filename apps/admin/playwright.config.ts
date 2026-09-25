import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-endtests van het beheerportal (fase 4): gebouwd met een nep-login (VITE_E2E_AUTH=mock); de API wordt in de
 * browser gemockt (e2e/mock-api.ts). De echte login met e-mailcode wordt handmatig getest.
 */
export default defineConfig({
  testDir: 'e2e',
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  timeout: 20_000,
  reporter: process.env.CI ? 'github' : 'list',
  use: { baseURL: 'http://localhost:4173', trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } } },
    {
      name: 'mobiel',
      use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true },
    },
  ],
  // De build gebeurt vooraf (`pnpm e2e`); hier alleen de preview-server, direct via vite zodat afsluiten werkt.
  webServer: {
    command: 'node ../../node_modules/vite/bin/vite.js preview --strictPort',
    url: 'http://localhost:4173/beheer/',
    reuseExistingServer: false,
    timeout: 60_000,
    gracefulShutdown: { signal: 'SIGTERM', timeout: 2_000 },
  },
});
