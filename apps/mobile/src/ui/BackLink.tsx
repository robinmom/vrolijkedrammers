import { router } from 'expo-router';
import { Pressable, StyleSheet } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Icon } from './Icon';

/** iOS-terugknop met de naam van het vorige scherm ("‹ Meer", Figma 07). */
export function BackLink({ label }: { label: string }) {
  const { colors } = useTheme();
  const goBack = () => (router.canGoBack() ? router.back() : router.replace('/'));
  return (
    <Pressable onPress={goBack} accessibilityRole="button" accessibilityLabel={`Terug naar ${label}`} style={styles.link} hitSlop={8}>
      <Icon name="terug-chevron" size={24} color={colors.accentText} />
      <AppText variant="body" color={colors.accentText} style={styles.label}>
        {label}
      </AppText>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  link: { flexDirection: 'row', alignItems: 'center', gap: 2, height: 44, alignSelf: 'flex-start', paddingLeft: 12, paddingRight: 8 },
  label: { fontSize: 17 },
});
