import type { ReactNode } from 'react';
import { RefreshControl, ScrollView, StyleSheet } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTheme } from '../theme/ThemeProvider';

interface ScreenProps {
  children: ReactNode;
  /** Pull-to-refresh: nieuwe content uit het portal ophalen (fase 6). */
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Hero-schermen lopen door tot achter de statusbalk; de hero regelt zelf de safe area. */
  hero?: boolean;
}

/** Schermcontainer: canvaskleur, safe area (boven) en scrollen. De tabbalk regelt de onderkant. */
export function Screen({ children, onRefresh, refreshing = false, hero }: ScreenProps) {
  const { colors } = useTheme();
  return (
    <SafeAreaView edges={hero ? [] : ['top']} style={[styles.safe, { backgroundColor: colors.canvas }]}>
      <ScrollView
        testID="screen-scroll"
        contentContainerStyle={styles.content}
        // Knoppen reageren meteen, ook als het toetsenbord open staat (formulieren).
        keyboardShouldPersistTaps="handled"
        // iOS schuift de inhoud niet vanzelf omhoog voor het toetsenbord, waardoor de onderste
        // velden van een formulier erachter verdwijnen (gemeld voor adverteerders, 2026-11-04).
        // Android doet dit al via windowSoftInputMode; op iOS is dit de enige knop ervoor.
        automaticallyAdjustKeyboardInsets
        refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={hero ? '#FFFFFF' : undefined} /> : undefined}
      >
        {children}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { paddingBottom: 24 },
});
