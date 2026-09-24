// Gedeelde ESLint-configuratie (flat config) voor packages en het beheerportal.
// De mobiele app gebruikt eslint-config-expo (apps/mobile/eslint.config.js).
import js from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['**/dist/**', '**/node_modules/**', '**/*.d.ts', 'apps/mobile/**', 'src/**', 'tests/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
);
