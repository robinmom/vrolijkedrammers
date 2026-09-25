import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import type { NewsSummary } from '../api/client';
import { newsDateLong, newsDateShort } from '../lib/dates';
import { AppText, FeaturedNewsCard, NewsRow } from '../ui';

/** Nieuwsoverzicht (Figma 03): het nieuwste bericht uitgelicht, daaronder "Eerder nieuws". */
export function NewsFeed({ items }: { items: NewsSummary[] }) {
  const [featured, ...rest] = items;
  const open = (id: string) => router.push(`/nieuws/${id}`);
  return (
    <View style={styles.content}>
      {featured ? (
        <FeaturedNewsCard
          id={featured.id}
          title={featured.title}
          summary={featured.summary}
          category={featured.category}
          imageUrl={featured.imageUrl}
          date={newsDateLong(featured.publishedAt)}
          onPress={() => open(featured.id)}
        />
      ) : null}
      {rest.length > 0 ? (
        <AppText variant="sectionHeader" accessibilityRole="header">
          Eerder nieuws
        </AppText>
      ) : null}
      {rest.map((item) => (
        <NewsRow
          key={item.id}
          variant="list"
          id={item.id}
          title={item.title}
          imageUrl={item.imageUrl}
          overline={item.category}
          date={newsDateShort(item.publishedAt)}
          onPress={() => open(item.id)}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, paddingTop: 4, gap: 12 },
});
