import { StyleSheet, View } from 'react-native';
import { themeLabels } from '../../theme/labels';
import { useTheme, type ThemePreference } from '../../theme/ThemeProvider';
import { BackLink, LargeTitleHeader, Screen, SettingsList } from '../../ui';

const options: ThemePreference[] = ['system', 'light', 'dark'];

/** Meer → Weergave: licht, donker of de instelling van het toestel volgen. */
export default function WeergaveScreen() {
  const { preference, setPreference } = useTheme();
  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Weergave" />
      <View style={styles.content} accessibilityRole="radiogroup">
        <SettingsList
          items={options.map((option) => ({
            type: 'choice',
            key: option,
            label: themeLabels[option],
            selected: preference === option,
            onPress: () => setPreference(option),
          }))}
        />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20 },
});
