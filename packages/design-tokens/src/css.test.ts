import { describe, expect, it } from 'vitest';
import { light, toCssVariables } from './index';

describe('toCssVariables', () => {
  it('zet camelCase-tokens om naar CSS-variabelen', () => {
    const css = toCssVariables(light);
    expect(css).toContain('--dvd-text-primary: #123047;');
    expect(css).toContain('--dvd-action-primary: #ED0012;');
  });
});
