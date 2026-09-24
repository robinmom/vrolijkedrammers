import { dark, light, type ColorScheme } from '@drammers/design-tokens';
import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useColorScheme } from 'react-native';

export type ThemeMode = 'light' | 'dark';

export interface Theme {
  mode: ThemeMode;
  colors: ColorScheme;
}

const ThemeContext = createContext<Theme>({ mode: 'light', colors: light });

interface ThemeProviderProps {
  /** Forceert een thema (tests, componentenpagina); standaard volgt de app de systeeminstelling. */
  mode?: ThemeMode;
  children: ReactNode;
}

export function ThemeProvider({ mode, children }: ThemeProviderProps) {
  const system = useColorScheme();
  const resolved: ThemeMode = mode ?? (system === 'dark' ? 'dark' : 'light');
  const theme = useMemo<Theme>(() => ({ mode: resolved, colors: resolved === 'dark' ? dark : light }), [resolved]);
  return <ThemeContext.Provider value={theme}>{children}</ThemeContext.Provider>;
}

export function useTheme(): Theme {
  return useContext(ThemeContext);
}
