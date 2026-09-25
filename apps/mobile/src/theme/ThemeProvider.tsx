import AsyncStorage from '@react-native-async-storage/async-storage';
import { dark, light, type ColorScheme } from '@drammers/design-tokens';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useColorScheme } from 'react-native';

export type ThemeMode = 'light' | 'dark';
/** Keuze in Meer → Weergave; `system` volgt de instelling van het toestel. */
export type ThemePreference = 'system' | ThemeMode;

export interface Theme {
  mode: ThemeMode;
  colors: ColorScheme;
  preference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
}

const ThemeContext = createContext<Theme>({ mode: 'light', colors: light, preference: 'system', setPreference: () => undefined });

const STORAGE_KEY = 'drammers-theme';

interface ThemeProviderProps {
  /** Forceert een thema (tests, componentenpagina); standaard volgt de app de voorkeur of de systeeminstelling. */
  mode?: ThemeMode;
  children: ReactNode;
}

export function ThemeProvider({ mode, children }: ThemeProviderProps) {
  const system = useColorScheme();
  const [preference, setPreferenceState] = useState<ThemePreference>('system');

  useEffect(() => {
    if (mode) {
      return;
    }
    AsyncStorage.getItem(STORAGE_KEY)
      .then((stored) => {
        if (stored === 'light' || stored === 'dark' || stored === 'system') {
          setPreferenceState(stored);
        }
      })
      .catch(() => undefined);
  }, [mode]);

  const setPreference = useCallback((value: ThemePreference) => {
    setPreferenceState(value);
    AsyncStorage.setItem(STORAGE_KEY, value).catch(() => undefined);
  }, []);

  const resolved: ThemeMode = mode ?? (preference === 'system' ? (system === 'dark' ? 'dark' : 'light') : preference);
  const theme = useMemo<Theme>(
    () => ({ mode: resolved, colors: resolved === 'dark' ? dark : light, preference, setPreference }),
    [resolved, preference, setPreference],
  );
  return <ThemeContext.Provider value={theme}>{children}</ThemeContext.Provider>;
}

export function useTheme(): Theme {
  return useContext(ThemeContext);
}
