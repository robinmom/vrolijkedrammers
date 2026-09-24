/** Vormen, ruimte en typografie uit Figma (docs/17 §3–4). */
export const radius = { sm: 12, md: 16, lg: 20, hero: 28, pill: 100, iconBubble: 22 } as const;

export const space = { xxs: 4, xs: 6, s: 8, sm: 10, m: 12, ml: 14, l: 16, xl: 20, xxl: 24 } as const;

/** Paginamarge links/rechts. */
export const pagePadding = space.xl;

/** Minimale aanraakdoelen: 44 pt (iOS) / 48 dp (Android). */
export const touchTarget = { ios: 44, android: 48 } as const;

export const fontFamily = {
  display: { semibold: 'Poppins_600SemiBold', bold: 'Poppins_700Bold' },
  body: { regular: 'Inter_400Regular', medium: 'Inter_500Medium', semibold: 'Inter_600SemiBold' },
} as const;

type FontRole = 'display.semibold' | 'display.bold' | 'body.regular' | 'body.medium' | 'body.semibold';

export interface TextStyleToken {
  font: FontRole;
  size: number;
  lineHeight?: number;
  uppercase?: boolean;
}

export const typography = {
  largeTitle: { font: 'display.bold', size: 32, lineHeight: 40 },
  heroTitle: { font: 'display.bold', size: 28, lineHeight: 34 },
  countdown: { font: 'display.bold', size: 26, lineHeight: 30 },
  cardTitle: { font: 'display.bold', size: 19, lineHeight: 24 },
  dateDay: { font: 'display.bold', size: 20, lineHeight: 24 },
  sectionHeader: { font: 'display.semibold', size: 17 },
  appName: { font: 'display.semibold', size: 16 },
  listTitle: { font: 'body.semibold', size: 16 },
  body: { font: 'body.regular', size: 15 },
  bodyStrong: { font: 'body.semibold', size: 15 },
  link: { font: 'body.semibold', size: 14 },
  caption: { font: 'body.regular', size: 13 },
  label: { font: 'body.medium', size: 12 },
  overline: { font: 'body.semibold', size: 11, uppercase: true },
  tab: { font: 'body.medium', size: 10 },
} as const satisfies Record<string, TextStyleToken>;
