import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

/** Sectiekop met optionele link ("Alles", "Meer"), Figma 01 Home. */
export function SectionHeader({ title, linkLabel, onLinkPress }: { title: string; linkLabel?: string; onLinkPress?: () => void }) {
  const { colors } = useTheme();
  return (
    <View style={styles.row}>
      <AppText variant="sectionHeader" accessibilityRole="header">
        {title}
      </AppText>
      {linkLabel ? (
        <Pressable onPress={onLinkPress} accessibilityRole="link" hitSlop={12}>
          <AppText variant="link" color={colors.accentText}>
            {linkLabel}
          </AppText>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
});
