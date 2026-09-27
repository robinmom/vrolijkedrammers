import { brand } from '@drammers/design-tokens';
import { useNetInfo } from '@react-native-community/netinfo';
import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { AppText } from './AppText';

/** Balk bovenin zolang er geen netwerk is; de app toont dan de laatst geladen content (fase 6). */
export function OfflineBanner() {
  const { isConnected } = useNetInfo();
  const insets = useSafeAreaInsets();
  // `null` = nog onbekend: dan geen melding, anders knippert de balk bij elke start.
  if (isConnected !== false) {
    return null;
  }
  return (
    <View style={[styles.banner, { paddingTop: insets.top + 6 }]} accessibilityRole="alert" accessibilityLiveRegion="polite">
      <AppText variant="label" color="#FFFFFF" style={styles.text}>
        Je bent offline. Je ziet de laatst geladen gegevens.
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  banner: { position: 'absolute', top: 0, left: 0, right: 0, backgroundColor: brand.navy, paddingBottom: 8, paddingHorizontal: 20 },
  text: { textAlign: 'center' },
});
