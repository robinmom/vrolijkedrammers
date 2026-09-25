import { describe, expect, it } from 'vitest';
import { brand, contrastRatio, dark, light, scan } from './index';

const AA_NORMAL = 4.5;
const AA_LARGE = 3;

describe('contrastRatio', () => {
  it('berekent de bekende extremen', () => {
    expect(contrastRatio('#FFFFFF', '#000000')).toBeCloseTo(21, 1);
    expect(contrastRatio('#123047', '#123047')).toBeCloseTo(1, 5);
  });
});

describe.each([
  ['licht', light],
  ['donker', dark],
])('tekstkleuren in het %s thema (WCAG AA, docs/17 §7)', (_name, scheme) => {
  const textTokens = ['textPrimary', 'textSecondary', 'textTertiary', 'accentText', 'successText'] as const;
  const backgrounds = ['canvas', 'surface'] as const;

  it.each(textTokens.flatMap((text) => backgrounds.map((bg) => [text, bg] as const)))(
    '%s op %s ≥ 4,5:1',
    (text, bg) => {
      expect(contrastRatio(scheme[text], scheme[bg])).toBeGreaterThanOrEqual(AA_NORMAL);
    },
  );

  it('tekst op de primaire knop ≥ 4,5:1', () => {
    expect(contrastRatio(scheme.onActionPrimary, scheme.actionPrimary)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  it('tekst op de destructieve knop ≥ 4,5:1 en de knop zelf zichtbaar op canvas (≥ 3:1)', () => {
    expect(contrastRatio(scheme.onActionPrimary, scheme.actionDanger)).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(contrastRatio(scheme.actionDanger, scheme.canvas)).toBeGreaterThanOrEqual(AA_LARGE);
  });

  it('tekst op het blauwe hero-vlak ≥ 4,5:1', () => {
    expect(contrastRatio(scheme.onHero, scheme.heroBackground)).toBeGreaterThanOrEqual(AA_NORMAL);
  });
});

describe.each([
  ['licht', light],
  ['donker', dark],
])('blauwe tekst in het %s thema', (_name, scheme) => {
  it('links op canvas en surface ≥ 4,5:1', () => {
    expect(contrastRatio(scheme.linkText, scheme.canvas)).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(contrastRatio(scheme.linkText, scheme.surface)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  it('tekst op een witte knop ≥ 4,5:1', () => {
    expect(contrastRatio(scheme.textOnLightButton, '#FFFFFF')).toBeGreaterThanOrEqual(AA_NORMAL);
  });
});

describe('scanresultaten', () => {
  it('oranje en rood halen AA voor normale tekst', () => {
    expect(contrastRatio(scan.warning.foreground, scan.warning.background)).toBeGreaterThanOrEqual(AA_NORMAL);
    expect(contrastRatio(scan.invalid.foreground, scan.invalid.background)).toBeGreaterThanOrEqual(AA_NORMAL);
  });

  it('groen haalt alleen AA voor grote tekst (daarom ≥ 24 pt vet op groene vlakken)', () => {
    const ratio = contrastRatio(scan.valid.foreground, scan.valid.background);
    expect(ratio).toBeGreaterThanOrEqual(AA_LARGE);
    expect(ratio).toBeLessThan(AA_NORMAL);
  });
});

describe('merkkleuren', () => {
  it('zijn de exacte Figma-waarden', () => {
    expect(brand).toEqual({
      red: '#ED0012',
      blue: '#087BC1',
      navy: '#123047',
      green: '#39A935',
      offWhite: '#FAFAF7',
      yellow: '#F4B942',
    });
  });
});
