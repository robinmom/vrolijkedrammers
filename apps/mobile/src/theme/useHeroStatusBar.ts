import { useFocusEffect } from 'expo-router';
import { setStatusBarStyle } from 'expo-status-bar';
import { useCallback } from 'react';

/** Witte statusbalk zolang een scherm met blauwe hero of foto zichtbaar is; daarna weer automatisch. */
export function useHeroStatusBar() {
  useFocusEffect(
    useCallback(() => {
      setStatusBarStyle('light');
      return () => setStatusBarStyle('auto');
    }, []),
  );
}
