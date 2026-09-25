import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { EventSummary } from '../../api/client';
import { queryKeys, useCarnivalYear, useEventCategories, useEvents } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { dateBlockParts, monthSection, seasonLabel, timeRange } from '../../lib/dates';
import { eventBadge } from '../../lib/events';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, EmptyState, EventCard, FilterChips, LargeTitleHeader, QueryState, Screen, SearchField } from '../../ui';

const ALL = 'alle';

/** 02 Programma (Figma 4:46): filterchips, maandsecties en eventkaarten. */
export default function ProgrammaScreen() {
  const { colors } = useTheme();
  const events = useEvents();
  const categories = useEventCategories();
  const year = useCarnivalYear();
  const refresh = useRefresh([queryKeys.events, queryKeys.categories, queryKeys.carnivalYear]);
  const [category, setCategory] = useState<string>(ALL);
  const [searching, setSearching] = useState(false);
  const [search, setSearch] = useState('');
  const toggleSearch = () => {
    setSearching(!searching);
    setSearch('');
  };

  // Alleen categorieën met activiteiten; een chip die niets oplevert is verwarrend.
  const options = useMemo(() => {
    const used = new Set((events.data ?? []).map((e) => e.category.code));
    return [{ value: ALL, label: 'Alle' }, ...(categories.data ?? []).filter((c) => used.has(c.code)).map((c) => ({ value: c.code, label: c.name }))];
  }, [events.data, categories.data]);

  const sections = useMemo(() => {
    const term = search.trim().toLowerCase();
    const filtered = (events.data ?? []).filter(
      (e) => (category === ALL || e.category.code === category) && (!term || e.title.toLowerCase().includes(term) || e.locationName?.toLowerCase().includes(term)),
    );
    const grouped = new Map<string, EventSummary[]>();
    for (const event of filtered) {
      const key = monthSection(event.startAt);
      grouped.set(key, [...(grouped.get(key) ?? []), event]);
    }
    return [...grouped.entries()];
  }, [events.data, category, search]);

  return (
    <Screen {...refresh}>
      <LargeTitleHeader
        title="Programma"
        subtitle={year.data ? seasonLabel(year.data.name) : undefined}
        actions={[{ icon: 'zoeken', accessibilityLabel: searching ? 'Zoeken sluiten' : 'Zoeken', onPress: toggleSearch }]}
      />
      {searching ? <SearchField value={search} onChangeText={setSearch} placeholder="Zoek een activiteit" /> : null}
      <View style={styles.filters}>
        <FilterChips options={options} selected={category} onChange={setCategory} accessibilityLabel="Filter op categorie" />
      </View>
      <View style={styles.content}>
        {!events.data ? (
          <QueryState query={events} />
        ) : sections.length === 0 ? (
          <EmptyState
            title={events.data.length === 0 ? 'Nog geen activiteiten' : 'Geen activiteiten gevonden'}
            message={events.data.length === 0 ? 'Het programma wordt binnenkort bekendgemaakt.' : 'Probeer een andere categorie of zoekterm.'}
          />
        ) : (
          sections.map(([month, items]) => (
            <View key={month} style={styles.section}>
              <AppText variant="overline" color={colors.textSecondary} style={styles.month} accessibilityRole="header">
                {month}
              </AppText>
              {items.map((event) => (
                <EventCard
                  key={event.id}
                  title={event.title}
                  date={dateBlockParts(event.startAt)}
                  time={event.allDay ? 'Hele dag' : timeRange(event.startAt, null, false)}
                  location={event.locationName ?? undefined}
                  badge={eventBadge(event)}
                  highlighted={event.isHighlight}
                  onPress={() => router.push(`/activiteit/${event.id}`)}
                  testID="event-card"
                />
              ))}
            </View>
          ))
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  filters: { paddingBottom: 4 },
  content: { paddingHorizontal: 20, paddingTop: 12, gap: 10 },
  section: { gap: 10 },
  month: { fontSize: 12, letterSpacing: 0.72 },
});
