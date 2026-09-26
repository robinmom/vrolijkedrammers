import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  // Het portal wordt vanuit de API-app geserveerd onder /beheer (OQ-76).
  base: '/beheer/',
  plugins: [react()],
  server: {
    port: 5173,
    // Lokaal: API op http://localhost:5162 (dotnet run --project src/Drammers.Api).
    proxy: { '/api': 'http://localhost:5162', '/health': 'http://localhost:5162' },
  },
  // Preview (e2e) met dezelfde CSP als de API voor /beheer (src/Drammers.Api/Portal/PortalHosting.cs), zodat
  // bijvoorbeeld inline <style> in de tests net zo geblokkeerd wordt als live.
  preview: {
    port: 4173,
    headers: {
      'Content-Security-Policy':
        "default-src 'self'; img-src 'self' data: https:; connect-src 'self' https://*.ciamlogin.com; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'",
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    exclude: ['e2e/**', 'node_modules/**'],
  },
});
