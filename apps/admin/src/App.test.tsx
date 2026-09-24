import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { App } from './App';
import { applyTheme, themeStylesheet } from './theme';

describe('App', () => {
  it('toont het lege dashboard', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Navigatie' })).toBeInTheDocument();
  });
});

describe('theme', () => {
  it('bevat licht en donker thema als CSS-variabelen', () => {
    const css = themeStylesheet();
    expect(css).toContain('--dvd-canvas: #FAFAF7;');
    expect(css).toContain('prefers-color-scheme: dark');
    expect(css).toContain('--dvd-canvas: #0D1A25;');
  });

  it('voegt het thema toe aan het document', () => {
    applyTheme(document);
    expect(document.head.querySelector('style[data-dvd-theme]')).not.toBeNull();
  });
});
