/**
 * Kleuren uit Figma "Vrolijke Drammers – App Design" (docs/17 §2).
 * Merkkleuren zijn ongewijzigd; voor kleine tekst gelden toegankelijke varianten (besluit OQ-66, docs/17 §7).
 */
export const brand = {
  red: '#ED0012', // color/drammers-rood
  blue: '#087BC1', // color/loils-blauw
  navy: '#123047', // color/donkerblauw
  green: '#39A935', // color/eikenloof-groen
  offWhite: '#FAFAF7', // color/warm-wit
  lightGrey: '#F0F1F2', // color/lichtgrijs
  yellow: '#F4B942', // accent (badges, uitslagen)
} as const;

export interface ColorScheme {
  canvas: string;
  surface: string;
  /** Rustig vlak binnen een kaart, bijv. tabelkoppen (Lichtgrijs uit de branding). */
  surfaceMuted: string;
  tabBar: string;
  textPrimary: string;
  /** Figma: donkerblauw 65 %; iets donkerder gemaakt voor ≥ 4,5:1 op canvas. */
  textSecondary: string;
  /** Figma: 55 % (inactieve tabs, 3,4:1); vervangen door een toegankelijke variant. */
  textTertiary: string;
  border: string;
  /** Rode tekstlinks ("Alles", "Meer") en actieve tab. */
  accentText: string;
  /** Blauwe tekstlinks op canvas/surface. */
  linkText: string;
  /** Tekst op witte (pill)knoppen binnen blauwe vlakken, bijv. "Lid worden" (Figma donker: #5AB0E6, 2,4:1). */
  textOnLightButton: string;
  successText: string;
  actionPrimary: string;
  onActionPrimary: string;
  /** Destructieve knoppen ("Alle leden verwijderen"): wit erop ≥ 4,5:1 en duidelijk zichtbaar op licht én donker canvas. */
  actionDanger: string;
  actionSecondary: string;
  heroBackground: string;
  onHero: string;
  tintRed: string;
  tintBlue: string;
  tintYellow: string;
  tintGreen: string;
  shadow: string;
}

export const light: ColorScheme = {
  canvas: brand.offWhite,
  surface: '#FFFFFF',
  surfaceMuted: brand.lightGrey,
  tabBar: 'rgba(255,255,255,0.96)',
  textPrimary: brand.navy,
  textSecondary: '#5B6C7B',
  textTertiary: '#5F7080',
  border: 'rgba(18,48,71,0.10)',
  accentText: '#D4000F',
  linkText: '#066AA6',
  textOnLightButton: '#066AA6',
  successText: '#287A26',
  actionPrimary: brand.red,
  onActionPrimary: '#FFFFFF',
  actionDanger: '#D4000F',
  actionSecondary: brand.blue,
  heroBackground: brand.blue,
  onHero: '#FFFFFF',
  tintRed: 'rgba(237,0,18,0.14)',
  tintBlue: 'rgba(8,123,193,0.14)',
  tintYellow: 'rgba(244,185,66,0.14)',
  tintGreen: 'rgba(57,169,53,0.14)',
  shadow: 'rgba(18,48,71,0.08)',
};

export const dark: ColorScheme = {
  canvas: '#0D1A25',
  surface: '#172939',
  surfaceMuted: '#1A2D3E',
  tabBar: '#172939',
  textPrimary: '#F2F4F6',
  textSecondary: '#8F99A1',
  textTertiary: '#8F99A1',
  border: 'rgba(242,244,246,0.08)',
  accentText: '#FF5A6A',
  linkText: '#5AB0E6',
  textOnLightButton: '#066AA6',
  successText: '#4FB84B',
  actionPrimary: brand.red,
  onActionPrimary: '#FFFFFF',
  actionDanger: '#D4000F',
  actionSecondary: brand.blue,
  heroBackground: brand.blue,
  onHero: '#FFFFFF',
  tintRed: 'rgba(237,0,18,0.14)',
  tintBlue: 'rgba(8,123,193,0.14)',
  tintYellow: 'rgba(244,185,66,0.14)',
  tintGreen: 'rgba(57,169,53,0.14)',
  shadow: 'rgba(0,0,0,0.35)',
};

/**
 * Scanresultaten (docs/02 §5.4): kleur is nooit de enige drager; altijd icoon + tekst.
 * Wit op groen haalt alleen 3:1 → uitsluitend grote tekst (≥ 24 pt vet) op groene vlakken.
 */
export const scan = {
  valid: { background: brand.green, foreground: '#FFFFFF' },
  warning: { background: brand.yellow, foreground: brand.navy },
  invalid: { background: brand.red, foreground: '#FFFFFF' },
} as const;
