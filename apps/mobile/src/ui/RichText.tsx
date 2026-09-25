import { useMemo } from 'react';
import { Linking, StyleSheet, Text, View } from 'react-native';
import { parseHtml } from '../lib/html';
import { useTheme } from '../theme/ThemeProvider';
import { AppText } from './AppText';

/** Toont de gesanitizede HTML van een event of nieuwsbericht als native tekst (geen WebView). */
export function RichText({ html }: { html: string }) {
  const { colors } = useTheme();
  const blocks = useMemo(() => parseHtml(html), [html]);
  return (
    <View style={styles.container}>
      {blocks.map((block, index) => {
        const content = block.spans.map((span, i) => (
          <Text
            key={i}
            style={[span.bold && styles.bold, span.italic && styles.italic, span.href ? { color: colors.linkText, textDecorationLine: 'underline' } : null]}
            onPress={span.href ? () => Linking.openURL(span.href as string) : undefined}
            accessibilityRole={span.href ? 'link' : undefined}
          >
            {span.text}
          </Text>
        ));
        if (block.type === 'heading') {
          return (
            <AppText key={index} variant="sectionHeader" accessibilityRole="header">
              {content}
            </AppText>
          );
        }
        const paragraph = (
          <AppText variant="body" color={colors.textSecondary} style={styles.paragraph}>
            {content}
          </AppText>
        );
        if (block.type === 'listItem') {
          return (
            <View key={index} style={styles.listItem}>
              <AppText variant="body" color={colors.textSecondary} style={styles.paragraph}>
                {block.marker}
              </AppText>
              <View style={styles.flex}>{paragraph}</View>
            </View>
          );
        }
        if (block.type === 'quote') {
          return (
            <View key={index} style={[styles.quote, { borderLeftColor: colors.border }]}>
              {paragraph}
            </View>
          );
        }
        return <View key={index}>{paragraph}</View>;
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: 12 },
  // Figma 06: 14/22 met 80 % dekking; textSecondary is de toegankelijke variant daarvan.
  paragraph: { fontSize: 14, lineHeight: 22 },
  bold: { fontFamily: 'Inter_600SemiBold' },
  italic: { fontStyle: 'italic' },
  listItem: { flexDirection: 'row', gap: 8 },
  flex: { flex: 1 },
  quote: { borderLeftWidth: 3, paddingLeft: 12 },
});
