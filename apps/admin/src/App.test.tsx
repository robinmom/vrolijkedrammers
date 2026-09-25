import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ApiError, describeProblem } from './api/errors';
import { ProblemAlert } from './components/ProblemAlert';
import { navItems, visibleNavItems } from './navigation';
import { applyTheme, themeStylesheet } from './theme';

describe('navigatie', () => {
  it('toont alleen menu-items waarvoor de gebruiker de permission heeft', () => {
    const items = visibleNavItems(['role.manage', 'audit.read']);
    expect(items.map((i) => i.label)).toEqual(['Gebruikers', 'Rollen en rechten', 'Auditlog']);
  });

  it('toont niets zonder beheerpermissions', () => {
    expect(visibleNavItems(['member.read.own', 'news.read'])).toEqual([]);
  });

  it('elk menu-item heeft een permission (UI verbergt, API dwingt af)', () => {
    expect(navItems.every((i) => i.permission.length > 0)).toBe(true);
  });
});

describe('foutweergave', () => {
  it('toont de Nederlandse detailtekst uit ProblemDetails', () => {
    render(<ProblemAlert error={new ApiError({ status: 409, detail: 'Er blijft niemand over met rollen beheren.', code: 'LOCKOUT_PREVENTED' })} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Er blijft niemand over met rollen beheren.');
  });

  it('valt terug op een standaardtekst per status', () => {
    expect(describeProblem(new ApiError({ status: 403 }))).toBe('Je hebt geen rechten voor deze actie.');
  });

  it('toont validatiefouten per veld', () => {
    expect(describeProblem(new ApiError({ status: 400, errors: { Name: ['Naam is verplicht.'] } }))).toBe('Naam is verplicht.');
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
