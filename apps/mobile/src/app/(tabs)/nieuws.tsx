import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { queryKeys, useNews, useNewsSeasons } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { NewsFeed } from '../../features/NewsFeed';
import { SeasonArchive } from '../../features/SeasonArchive';
import { EmptyState, LargeTitleHeader, QueryState, Screen, SearchField, SectionHeader } from '../../ui';

/** 03 Nieuws (Figma 4:474): het actieve carnavalsjaar; oudere jaren via de knoppen eronder (fase 21g). */
export default function NieuwsScreen() {
  const [season, setSeason] = useState<string | null>(null);
  const news = useNews(season);
  const seasons = useNewsSeasons();
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
      {season ? (
        <View style={styles.padded}>
          <SectionHeader title={`Nieuws ${season}`} />
        </View>
      ) : null}
      {!news.data ? (
        <QueryState query={news} />
      ) : items.length === 0 ? (
        <EmptyState
          icon="nieuws"
          title={news.data.length > 0 ? 'Geen berichten gevonden' : season ? 'Geen nieuws in dit jaar' : 'Nog geen nieuws dit carnavalsjaar'}
        />
      ) : (
        <NewsFeed items={items} />
      )}
      <SeasonArchive seasons={seasons.data} selected={season} onChange={setSeason} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  padded: { paddingHorizontal: 20, paddingBottom: 8 },
});
