import { Redirect } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { ThemeProvider, useTheme, type ThemeMode } from '../theme/ThemeProvider';
import {
  AppText,
  Button,
  EmptyState,
  ErrorState,
  EventCard,
  FilterChips,
  LargeTitleHeader,
  Screen,
  SectionHeader,
  SettingsList,
  ShortcutTile,
} from '../ui';

type Filter = 'alle' | 'carnaval' | 'jeugd' | 'vereniging';

/** Componentenoverzicht voor visuele controle tegen Figma (alleen in development builds). */
export default function ComponentenScreen() {
  const [mode, setMode] = useState<ThemeMode>('light');
  if (!__DEV__) {
    return <Redirect href="/" />;
  }
  return (
    <ThemeProvider mode={mode}>
      <Overview mode={mode} onToggle={() => setMode(mode === 'light' ? 'dark' : 'light')} />
    </ThemeProvider>
  );
}

function Overview({ mode, onToggle }: { mode: ThemeMode; onToggle: () => void }) {
  const { colors } = useTheme();
  const [filter, setFilter] = useState<Filter>('alle');
  const [push, setPush] = useState(true);
  return (
    <Screen>
      <LargeTitleHeader title="Componenten" subtitle={`Thema: ${mode === 'light' ? 'licht' : 'donker'}`} />
      <View style={styles.section}>
        <Button label={mode === 'light' ? 'Toon donker thema' : 'Toon licht thema'} variant="secondary" onPress={onToggle} />
      </View>
      <FilterChips
        accessibilityLabel="Filter programma"
        selected={filter}
        onChange={setFilter}
        options={[
          { value: 'alle', label: 'Alle' },
          { value: 'carnaval', label: 'Carnaval' },
          { value: 'jeugd', label: 'Jeugd' },
          { value: 'vereniging', label: 'Vereniging' },
        ]}
      />
      <View style={styles.section}>
        <SectionHeader title="Eerstvolgende activiteit" linkLabel="Alles" onLinkPress={() => undefined} />
        <EventCard title="Elfde van de Elfde" date={{ weekday: 'WO', day: '11', month: 'NOV' }} time="11:11 uur" location="Dorpsplein Loil" />
        <EventCard title="Kindermiddag" date={{ weekday: 'ZO', day: '24', month: 'JAN' }} time="14:00 uur" location="Feesttent Loil" badge={{ label: 'Jeugd', variant: 'youth' }} />
        <EventCard highlighted title="Optocht Loil" date={{ weekday: 'ZO', day: '07', month: 'FEB' }} time="13:30 uur" location="Centrum Loil" badge={{ label: 'Hoogtepunt', variant: 'highlight' }} />
        <View style={styles.tiles}>
          <ShortcutTile icon="fotos" label="Foto's" tint="blue" />
          <ShortcutTile icon="uitslagen" label="Uitslagen" tint="yellow" />
          <ShortcutTile icon="meldingen" label="Meldingen" tint="red" />
          <ShortcutTile icon="locatie" label="Locatie" tint="green" />
        </View>
        <View style={styles.tilesRegular}>
          <ShortcutTile size="regular" icon="vereniging" label="Vereniging" tint="red" />
          <ShortcutTile size="regular" icon="contact" label="Contact" tint="blue" />
          <ShortcutTile size="regular" icon="megafoon" label="Meldingen" tint="red" />
        </View>
        <View style={styles.buttons}>
          <View style={styles.flex}>
            <Button label="Groep inschrijven" icon="plus" />
          </View>
          <Button label="Route" icon="locatie" variant="secondary" />
        </View>
        <SectionHeader title="Instellingen" />
        <SettingsList
          items={[
            { type: 'toggle', key: 'push', label: 'Pushmeldingen', value: push, onValueChange: setPush },
            { type: 'link', key: 'over', label: 'Over de app', onPress: () => undefined },
          ]}
        />
        <AppText variant="caption" color={colors.textSecondary}>
          Lege en fouttoestand:
        </AppText>
        <EmptyState title="Nog geen activiteiten" message="Zodra er activiteiten gepland zijn, zie je ze hier." />
        <ErrorState action={{ label: 'Opnieuw proberen', onPress: () => undefined }} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  section: { paddingHorizontal: 20, paddingTop: 16, gap: 14 },
  tiles: { flexDirection: 'row', gap: 10 },
  tilesRegular: { flexDirection: 'row', gap: 10, flexWrap: 'wrap' },
  buttons: { flexDirection: 'row', gap: 10 },
  flex: { flex: 1 },
});
