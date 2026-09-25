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
  preview: { port: 4173 },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    exclude: ['e2e/**', 'node_modules/**'],
  },
});
