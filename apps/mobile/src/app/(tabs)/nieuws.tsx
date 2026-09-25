import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { queryKeys, useNews } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { NewsFeed } from '../../features/NewsFeed';
import { EmptyState, LargeTitleHeader, QueryState, Screen, SearchField } from '../../ui';

/** 03 Nieuws (Figma 4:474). */
export default function NieuwsScreen() {
  const news = useNews();
  const refresh = useRefresh([queryKeys.news]);
  const [searching, setSearching] = useState(false);
  const [search, setSearch] = useState('');
  const toggleSearch = () => {
    setSearching(!searching);
    setSearch('');
  };

  const items = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (news.data ?? []).filter((n) => !term || n.title.toLowerCase().includes(term) || n.summary?.toLowerCase().includes(term));
  }, [news.data, search]);

  return (
    <Screen {...refresh}>
      <LargeTitleHeader
        title="Nieuws"
        actions={[
          { icon: 'zoeken', accessibilityLabel: searching ? 'Zoeken sluiten' : 'Zoeken', onPress: toggleSearch },
          { icon: 'meldingen', accessibilityLabel: 'Meldingen', onPress: () => router.push('/meldingen') },
        ]}
      />
      {searching ? <SearchField value={search} onChangeText={setSearch} placeholder="Zoek in het nieuws" /> : null}
      {!news.data ? (
        <QueryState query={news} />
      ) : items.length === 0 ? (
        <EmptyState icon="nieuws" title={news.data.length === 0 ? 'Nog geen nieuws' : 'Geen berichten gevonden'} />
      ) : (
        <NewsFeed items={items} />
      )}
    </Screen>
  );
}
