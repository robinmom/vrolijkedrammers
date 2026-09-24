import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { AppText, LargeTitleHeader, Screen, SettingsList } from '../../ui';
import { useTheme } from '../../theme/ThemeProvider';

/** Fase 0: alleen de app-informatie; de tegels en instellingen volgen in fase 6 en 10. */
export default function MeerScreen() {
  const { colors } = useTheme();
  return (
    <Screen>
      <LargeTitleHeader title="Meer" />
      <View style={styles.content}>
        {__DEV__ ? (
          <SettingsList items={[{ type: 'link', key: 'componenten', label: 'Componenten (ontwikkeling)', onPress: () => router.push('/componenten') }]} />
        ) : null}
        <AppText variant="label" color={colors.textTertiary} style={styles.footer}>
          CV De Vrolijke Drammers · Loil · sinds 1958
        </AppText>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  footer: { textAlign: 'center' },
});
