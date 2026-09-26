import { Inter_400Regular, Inter_500Medium, Inter_600SemiBold } from '@expo-google-fonts/inter';
import { Poppins_600SemiBold, Poppins_700Bold } from '@expo-google-fonts/poppins';
import { useFonts } from 'expo-font';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';
import { QueryProvider } from '../api/QueryProvider';
import { restoreSession } from '../auth/session';
import { AppGate } from '../shell/AppGate';
import { ThemeProvider, useTheme } from '../theme/ThemeProvider';
import { OfflineBanner } from '../ui';

SplashScreen.preventAutoHideAsync();

export default function RootLayout() {
  const [loaded, error] = useFonts({
    Poppins_600SemiBold,
    Poppins_700Bold,
    Inter_400Regular,
    Inter_500Medium,
    Inter_600SemiBold,
  });

  useEffect(() => {
    restoreSession().catch(() => undefined);
  }, []);

  useEffect(() => {
    if (loaded || error) {
      SplashScreen.hideAsync();
    }
  }, [loaded, error]);

  // Bij een fout bij het laden van fonts valt de app terug op systeemfonts in plaats van te blokkeren.
  if (!loaded && !error) {
    return null;
  }

  return (
    <ThemeProvider>
      <QueryProvider>
        <StatusBar style="auto" />
        <Shell />
      </QueryProvider>
    </ThemeProvider>
  );
}

function Shell() {
  const { colors } = useTheme();
  return (
    <View style={[styles.flex, { backgroundColor: colors.canvas }]}>
      <AppGate>
        <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.canvas } }} />
      </AppGate>
      {/* Als laatste gerenderd: de banner ligt bovenop de schermen en verschuift hun layout niet. */}
      <OfflineBanner />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
});
