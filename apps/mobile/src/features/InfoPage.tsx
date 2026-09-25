import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { placeholder, type Section } from '../content/static';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, BackLink, Card, LargeTitleHeader, Screen } from '../ui';

interface InfoPageProps {
  title: string;
  backLabel?: string;
  sections?: Section[];
  /** Toont "Voorlopige informatie" zolang de teksten in content/static.ts plaatshouders zijn. */
  staticContent?: boolean;
  children?: ReactNode;
}

/** Informatiepagina onder Meer: titel, tekstblokken in een kaart en optioneel eigen inhoud (knoppen, adres). */
export function InfoPage({ title, backLabel = 'Meer', sections = [], staticContent, children }: InfoPageProps) {
  const { colors } = useTheme();
  return (
    <Screen>
      <BackLink label={backLabel} />
      <LargeTitleHeader title={title} />
      <View style={styles.content}>
        {sections.length > 0 ? (
          <Card style={styles.card}>
            {sections.map((section, index) => (
              <View key={section.heading ?? index} style={styles.section}>
                {section.heading ? (
                  <AppText variant="sectionHeader" accessibilityRole="header">
                    {section.heading}
                  </AppText>
                ) : null}
                {section.paragraphs.map((paragraph) => (
                  <AppText key={paragraph} variant="body" color={colors.textSecondary} style={styles.paragraph}>
                    {paragraph}
                  </AppText>
                ))}
              </View>
            ))}
          </Card>
        ) : null}
        {children}
        {staticContent && placeholder ? (
          <AppText variant="label" color={colors.textSecondary} style={styles.note}>
            Voorlopige informatie; het bestuur vult deze pagina binnenkort aan.
          </AppText>
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 16 },
  card: { padding: 16, gap: 16 },
  section: { gap: 6 },
  paragraph: { fontSize: 14, lineHeight: 22 },
  note: { textAlign: 'center' },
});
