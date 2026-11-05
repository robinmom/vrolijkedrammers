import type { ReactNode } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTheme } from '../theme/ThemeProvider';
import { MAX_CONTENT_WIDTH, MAX_READING_WIDTH } from './layout';

interface ScreenProps {
  children: ReactNode;
  /** Pull-to-refresh: nieuwe content uit het portal ophalen (fase 6). */
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Hero-schermen lopen door tot achter de statusbalk; de hero regelt zelf de safe area. */
  hero?: boolean;
  /**
   * Voor een scherm dat uit kaarten bestaat in plaats van lopende tekst: dan mag de inhoud breder dan de leesbreedte.
   * Zet dit niet op een scherm met lopende tekst (een nieuwsbericht, een infopagina), want daar worden de regels dan
   * onleesbaar lang.
   */
  wide?: boolean;
}

/** Schermcontainer: canvaskleur, safe area (boven) en scrollen. De tabbalk regelt de onderkant. */
export function Screen({ children, onRefresh, refreshing = false, hero, wide }: ScreenProps) {
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
        {/* Leeskolom: op een tablet rekt de inhoud niet mee tot de volle breedte, want dan worden
            tekstregels onleesbaar lang. Een scherm met kaarten in plaats van lopende tekst zet `wide` en
            mag breder. Op een telefoon is het scherm smaller dan beide grenzen, dus daar verandert niets. */}
        <View testID="screen-column" style={wide ? styles.columnWide : styles.column}>
          {children}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1 },
  content: { paddingBottom: 24 },
  column: { width: '100%', maxWidth: MAX_READING_WIDTH, alignSelf: 'center' },
  columnWide: { width: '100%', maxWidth: MAX_CONTENT_WIDTH, alignSelf: 'center' },
});
