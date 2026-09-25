import { useMemo } from 'react';
import { queryKeys, useNews } from '../api/queries';
import { useRefresh } from '../api/useRefresh';
import { NewsFeed } from '../features/NewsFeed';
import { BackLink, EmptyState, LargeTitleHeader, QueryState, Screen } from '../ui';

/** Uitslagen = nieuwsberichten met categorie "Uitslagen" (OQ-40). */
export default function UitslagenScreen() {
  const news = useNews();
  const refresh = useRefresh([queryKeys.news]);
  const items = useMemo(() => (news.data ?? []).filter((n) => n.category?.trim().toLowerCase() === 'uitslagen'), [news.data]);
  return (
    <Screen {...refresh}>
      <BackLink label="Terug" />
      <LargeTitleHeader title="Uitslagen" />
      {!news.data ? (
        <QueryState query={news} />
      ) : items.length === 0 ? (
        <EmptyState icon="uitslagen" title="Nog geen uitslagen" message="Na de optocht en andere wedstrijden verschijnen de uitslagen hier." />
      ) : (
        <NewsFeed items={items} />
      )}
    </Screen>
  );
}
