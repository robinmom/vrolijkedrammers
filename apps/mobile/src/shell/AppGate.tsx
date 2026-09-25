import Constants from 'expo-constants';
import type { ReactNode } from 'react';
import { Image, Linking, Platform, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAppConfig } from '../api/queries';
import { isUpdateRequired } from '../lib/version';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, Button } from '../ui';

const logo = require('../../assets/images/logo.png');

/** Geïnstalleerde versie volgens de app-config van deze build (ook juist na een OTA-update). */
export const installedVersion = Constants.expoConfig?.version ?? '0.0.0';

const storeUrl = Platform.select({
  ios: Constants.expoConfig?.extra?.appStoreUrl as string | undefined,
  android: 'market://details?id=nl.vrolijkedrammers.app',
});

/**
 * Blokkeert de app bij onderhoud of een te oude versie (`/app-config`, fase 6).
 * Zonder antwoord van de API (offline, eerste start) blijft de app gewoon bruikbaar.
 */
export function AppGate({ children }: { children: ReactNode }) {
  const { data } = useAppConfig();
  if (data?.maintenance.enabled) {
    return <Blocking title="Even geduld" message={data.maintenance.message || 'De app is tijdelijk in onderhoud. Probeer het later opnieuw.'} />;
  }
  const minimum = Platform.OS === 'ios' ? data?.minAppVersion.ios : data?.minAppVersion.android;
  if (minimum && isUpdateRequired(installedVersion, minimum)) {
    return (
      <Blocking
        title="Update nodig"
        message="Er is een nieuwe versie van de app. Werk de app bij om verder te gaan."
        action={storeUrl ? { label: 'App bijwerken', onPress: () => Linking.openURL(storeUrl) } : undefined}
      />
    );
  }
  return children;
}

function Blocking({ title, message, action }: { title: string; message: string; action?: { label: string; onPress: () => void } }) {
  const { colors } = useTheme();
  return (
    <SafeAreaView style={[styles.container, { backgroundColor: colors.canvas }]}>
      <View style={styles.content} accessibilityRole="alert">
        <Image source={logo} style={styles.logo} resizeMode="contain" accessibilityIgnoresInvertColors />
        <AppText variant="heroTitle" style={styles.center} accessibilityRole="header">
          {title}
        </AppText>
        <AppText variant="body" color={colors.textSecondary} style={styles.center}>
          {message}
        </AppText>
        {action ? <Button label={action.label} onPress={action.onPress} /> : null}
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  content: { flex: 1, justifyContent: 'center', paddingHorizontal: 32, gap: 16 },
  logo: { width: 120, height: 104, alignSelf: 'center' },
  center: { textAlign: 'center' },
});
