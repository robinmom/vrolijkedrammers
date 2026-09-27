import { brand } from '@drammers/design-tokens';
import { Pressable, StyleSheet, Switch, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Card } from './Card';
import { Icon } from './Icon';

export type SettingsItem =
  | { type: 'toggle'; key: string; label: string; value: boolean; onValueChange: (value: boolean) => void }
  | { type: 'link'; key: string; label: string; onPress: () => void }
  | { type: 'choice'; key: string; label: string; selected: boolean; onPress: () => void };

/** Instellingenlijst (Figma 05 Meer): rijen van 52 pt met toggle, chevron of vinkje (keuze). */
export function SettingsList({ items }: { items: SettingsItem[] }) {
  const { colors } = useTheme();
  return (
    <Card style={styles.card}>
      {items.map((item, index) => {
        const last = index === items.length - 1;
        const rowStyle = [styles.row, !last && { borderBottomWidth: StyleSheet.hairlineWidth * 2, borderBottomColor: colors.border }];
        if (item.type === 'toggle') {
          return (
            <View key={item.key} style={rowStyle}>
              <AppText variant="body" style={styles.label}>
                {item.label}
              </AppText>
              <Switch
                value={item.value}
                onValueChange={item.onValueChange}
                accessibilityLabel={item.label}
                trackColor={{ true: brand.green, false: colors.border }}
                thumbColor="#FFFFFF"
              />
            </View>
          );
        }
        if (item.type === 'choice') {
          return (
            <Pressable key={item.key} onPress={item.onPress} accessibilityRole="radio" accessibilityLabel={item.label} accessibilityState={{ checked: item.selected }} style={rowStyle}>
              <AppText variant="body" style={styles.label}>
                {item.label}
              </AppText>
              {item.selected ? (
                // Het vinkje is visueel; schermlezers krijgen de keuze via accessibilityState.checked.
                <AppText variant="bodyStrong" color={colors.accentText}>
                  ✓
                </AppText>
              ) : null}
            </Pressable>
          );
        }
        return (
          <Pressable key={item.key} onPress={item.onPress} accessibilityRole="button" style={rowStyle}>
            <AppText variant="body" style={styles.label}>
              {item.label}
            </AppText>
            <Icon name="chevron" size={20} color={colors.textTertiary} />
          </Pressable>
        );
      })}
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { paddingVertical: 0, paddingHorizontal: 16 },
  row: { minHeight: 52, flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 12 },
  label: { fontFamily: 'Inter_500Medium', flexShrink: 1 },
});
