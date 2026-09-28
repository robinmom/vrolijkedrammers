import { StyleSheet, View } from 'react-native';
import { useParade } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Card, LargeTitleHeader, RichText, Screen } from '../../ui';

/**
 * Meedoen aan de optocht (fase 11): voor leden zonder het recht Groepsverantwoordelijke. De tekst beheert de
 * optochtcommissie in het portal (Optocht → Informatie over meedoen).
 */
export default function OptochtInfoScreen() {
  const { colors } = useTheme();
  const parade = useParade();
  return (
    <Screen>
      <BackLink label="Optocht" />
      <LargeTitleHeader title="Meedoen aan de optocht" />
      <View style={styles.content}>
        {parade.data?.infoHtml ? (
          <Card style={styles.card}>
            <RichText html={parade.data.infoHtml} />
          </Card>
        ) : null}
        <Card style={styles.card}>
          <AppText variant="sectionHeader" accessibilityRole="header">
            Een groep inschrijven
          </AppText>
          <AppText variant="body" color={colors.textSecondary}>
            Groepen worden ingeschreven door een groepsverantwoordelijke. Ben jij dat? Vraag het bestuur of de
            optochtcommissie om je account als groepsverantwoordelijke aan te zetten; daarna zie je hier de knop
            Aanmelden optocht.
          </AppText>
        </Card>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 10 },
});
