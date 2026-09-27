import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';

interface HeaderAction {
  icon: IconName;
  accessibilityLabel: string;
  onPress: () => void;
}

/** Grote paginatitel (Figma 02/03/05) met optionele ondertitel en actie-iconen. */
export function LargeTitleHeader({ title, subtitle, actions = [] }: { title: string; subtitle?: string; actions?: HeaderAction[] }) {
  const { colors } = useTheme();
  return (
    <View style={styles.container}>
      <View style={styles.row}>
        <AppText variant="largeTitle" accessibilityRole="header">
          {title}
        </AppText>
        <View style={styles.actions}>
          {actions.map((action) => (
            <Pressable
              key={action.icon}
              onPress={action.onPress}
              accessibilityRole="button"
              accessibilityLabel={action.accessibilityLabel}
              hitSlop={10}
              style={styles.action}
            >
              <Icon name={action.icon} size={24} color={colors.textPrimary} />
            </Pressable>
          ))}
        </View>
      </View>
      {subtitle ? (
        <AppText variant="body" color={colors.textSecondary} style={styles.subtitle}>
          {subtitle}
        </AppText>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { paddingHorizontal: 20, paddingTop: 4, paddingBottom: 12, gap: 2 },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  actions: { flexDirection: 'row', gap: 16 },
  action: { minWidth: 44, minHeight: 44, alignItems: 'center', justifyContent: 'center' },
  subtitle: { fontSize: 14 },
});
