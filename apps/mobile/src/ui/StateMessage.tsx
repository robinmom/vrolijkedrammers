import { StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';
import { Button } from './Button';
import { Icon, type IconName } from './Icon';

interface StateMessageProps {
  title: string;
  message?: string;
  icon?: IconName;
  action?: { label: string; onPress: () => void };
}

/** Lege toestand, bijv. "Nog geen activiteiten" (docs/17 §5). */
export function EmptyState({ title, message, icon = 'programma', action }: StateMessageProps) {
  return <StateMessage title={title} message={message} icon={icon} action={action} />;
}

/** Fouttoestand met herstelactie; toont nooit technische details (docs/16 §6). */
export function ErrorState({ title = 'Er ging iets mis', message = 'Controleer je verbinding en probeer het opnieuw.', action }: Partial<StateMessageProps>) {
  return <StateMessage title={title} message={message} icon="meldingen" action={action} tone="error" />;
}

function StateMessage({ title, message, icon, action, tone }: StateMessageProps & { tone?: 'error' }) {
  const { colors } = useTheme();
  return (
    <View style={styles.container} accessibilityRole={tone === 'error' ? 'alert' : undefined}>
      {icon ? <Icon name={icon} size={32} color={tone === 'error' ? colors.accentText : colors.textTertiary} /> : null}
      <AppText variant="sectionHeader" style={styles.center}>
        {title}
      </AppText>
      {message ? (
        <AppText variant="body" color={colors.textSecondary} style={styles.center}>
          {message}
        </AppText>
      ) : null}
      {action ? <Button label={action.label} onPress={action.onPress} variant="secondary" /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { alignItems: 'center', justifyContent: 'center', gap: 10, paddingHorizontal: 32, paddingVertical: 40 },
  center: { textAlign: 'center' },
});
