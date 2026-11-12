import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyTasks } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, FilterChips, LargeTitleHeader, QueryState, Screen, TextField } from '../../ui';

type Due = 'None' | 'Week' | 'Month' | 'Carnival';

const DUE: { value: Due; label: string }[] = [
  { value: 'None', label: 'Geen datum' },
  { value: 'Week', label: 'Deze week' },
  { value: 'Month', label: 'Deze maand' },
  { value: 'Carnival', label: 'Voor carnaval' },
];

const iso = (date: Date) => date.toISOString().slice(0, 10);

/**
 * "Verwacht klaar" als vier snelkeuzes in plaats van een datumkiezer: die is er in de app niet, en hem toevoegen zou
 * een nieuwe build in de winkels kosten. Het bestuur kan in het portal alsnog een precieze datum zetten.
 */
function dueDate(choice: Due, carnivalStartDate?: string | null): string | null {
  const today = new Date();
  if (choice === 'None') return null;
  if (choice === 'Week') {
    const sunday = new Date(today);
    sunday.setUTCDate(sunday.getUTCDate() + ((7 - sunday.getUTCDay()) % 7));
    return iso(sunday);
  }

  if (choice === 'Month') {
    return iso(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + 1, 0)));
  }

  if (!carnivalStartDate) return null;
  const before = new Date(`${carnivalStartDate}T00:00:00Z`);
  before.setUTCDate(before.getUTCDate() - 1);
  return iso(before);
}

/** Een lid zet zelf een taak op de lijst, onder een van de categorieën van het bestuur (2026-11-12). */
export default function NieuweTaakScreen() {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const mine = useMyTasks();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [due, setDue] = useState<Due>('None');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const categories = mine.data?.categories ?? [];
  const chosen = categoryId ?? categories[0]?.id ?? null;

  async function add() {
    if (!title.trim()) {
      setError('Geef de taak een naam.');
      return;
    }

    if (!chosen) {
      setError('Kies een categorie.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const { response, error: problem } = await api.POST('/api/v1/me/tasks', {
        body: {
          categoryId: chosen,
          title: title.trim(),
          description: description.trim() || null,
          dueOn: dueDate(due, mine.data?.carnivalStartDate),
          status: 'NotStarted',
          progress: 0,
        },
      });
      if (response.ok) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.myTasks });
        // Terug naar de lijst; zonder geschiedenis (deeplink) is er niets om op terug te vallen.
        if (router.canGoBack()) router.back();
        else router.replace('/taken');
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? 'Toevoegen lukt nu niet.');
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BackLink label="Mijn taken" />
      <LargeTitleHeader title="Nieuwe taak" />
      <View style={styles.content}>
        <AppText variant="caption" color={colors.textSecondary}>
          Hij komt meteen in de takenlijst van het bestuur te staan, op jouw naam.
        </AppText>
        {!mine.data ? (
          <QueryState query={mine} />
        ) : !mine.data.allowed ? (
          <AppText variant="body" color={colors.textSecondary}>
            De takenlijst is er voor het kader.
          </AppText>
        ) : (
          <>
            <TextField label="Taak" value={title} onChangeText={setTitle} maxLength={200} autoFocus />
            <TextField
              label="Omschrijving (niet verplicht)"
              placeholder="Bijv. wie er staat en op welke dagen."
              value={description}
              onChangeText={setDescription}
              multiline
              maxLength={2000}
            />
            <View style={styles.field}>
              <AppText variant="bodyStrong">Categorie</AppText>
              {categories.length === 0 ? (
                <AppText variant="caption" color={colors.textSecondary}>
                  Er zijn nog geen categorieën. Het bestuur maakt die aan in het portal.
                </AppText>
              ) : (
                <FilterChips<string>
                  accessibilityLabel="Categorie"
                  options={categories.map((c) => ({ value: c.id, label: c.name }))}
                  selected={chosen ?? ''}
                  onChange={setCategoryId}
                />
              )}
            </View>
            <View style={styles.field}>
              <AppText variant="bodyStrong">Verwacht klaar</AppText>
              <FilterChips<Due> accessibilityLabel="Verwacht klaar" options={DUE} selected={due} onChange={setDue} />
            </View>
            {error ? (
              <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
                {error}
              </AppText>
            ) : null}
            <Button label="Taak toevoegen" onPress={() => void add()} disabled={busy || categories.length === 0} />
          </>
        )}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, paddingTop: 4, gap: 16 },
  field: { gap: 8 },
});
