import { StyleSheet, View } from 'react-native';
import type { components } from '@drammers/api-client';
import { FilterChips, SectionHeader } from '../ui';

type Seasons = components['schemas']['SeasonsResponse'];

const CURRENT = 'actueel';

/**
 * Fase 21g: knoppen met het jaartal (bijvoorbeeld "2025-2026") onder het nieuws en de foto's. Zonder keuze toont het
 * scherm het actieve carnavalsjaar; in een ouder jaar staat vooraan een knop terug naar het actuele jaar.
 */
export function SeasonArchive({
  seasons,
  selected,
  onChange,
}: {
  seasons: Seasons | undefined;
  selected: string | null;
  onChange: (season: string | null) => void;
}) {
  const archive = seasons?.archive ?? [];
  if (!seasons || (archive.length === 0 && !selected)) return null;
  const options = [
    ...(selected ? [{ value: CURRENT, label: `Actueel (${seasons.current.slug})` }] : []),
    ...archive.map((s) => ({ value: s.slug, label: s.slug })),
  ];
  return (
    <View style={styles.section}>
      <View style={styles.padded}>
        <SectionHeader title={selected ? 'Andere jaren' : 'Eerdere jaren'} />
      </View>
      <FilterChips
        options={options}
        selected={selected ?? CURRENT}
        onChange={(value) => onChange(value === CURRENT ? null : value)}
        accessibilityLabel="Carnavalsjaren"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  section: { gap: 10, paddingTop: 16, paddingBottom: 8 },
  padded: { paddingHorizontal: 20 },
});
