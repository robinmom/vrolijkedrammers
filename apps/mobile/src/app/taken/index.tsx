import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyTasks } from '../../api/queries';
import { useTheme } from '../../theme/ThemeProvider';
import {
  AppText,
  BackLink,
  Button,
  Card,
  FilterChips,
  LargeTitleHeader,
  QueryState,
  ScoreSlider,
  Screen,
  SectionHeader,
  TextField,
} from '../../ui';

type TaskList = NonNullable<ReturnType<typeof useMyTasks>['data']>;
type Task = TaskList['items'][number];
type Status = Task['status'];
type Filter = 'Todo' | 'Overdue' | 'Done' | 'All';

const STATUS: Record<Status, string> = {
  NotStarted: 'Nog niet gestart',
  InProgress: 'Bezig',
  Done: 'Afgerond',
  Cancelled: 'Niet van toepassing',
};

/** De drie standen die een lid zelf zet, plus "niet van toepassing" om een taak van de lijst te halen. */
const CHOICES: Status[] = ['NotStarted', 'InProgress', 'Done', 'Cancelled'];

const dayMonth = new Intl.DateTimeFormat('nl-NL', { day: 'numeric', month: 'long', timeZone: 'UTC' });
export const taskDate = (date?: string | null) => (date ? dayMonth.format(new Date(`${date}T00:00:00Z`)) : null);

const open = (t: Task) => t.status === 'NotStarted' || t.status === 'InProgress';

/**
 * Takenlijst carnaval in de app (2026-11-12): het lid ziet alleen zijn eigen taken, gegroepeerd per categorie, met de
 * afgeronde taken onderaan. Alleen het kader komt hier; anderen zien de knop onder Meer niet eens.
 */
export default function TakenScreen() {
  const { colors } = useTheme();
  const mine = useMyTasks();
  const [filter, setFilter] = useState<Filter>('Todo');

  const items = mine.data?.items ?? [];
  const counts = {
    Todo: items.filter(open).length,
    Overdue: items.filter((t) => t.overdue).length,
    Done: items.filter((t) => t.status === 'Done').length,
    All: items.length,
  };
  // Eerst het werk dat nog ligt, per categorie; afgerond en "niet van toepassing" gaan naar onderen. De afgeronde
  // taken blijven ook bij "te doen" onderaan staan, zodat ze aan te passen blijven als er toch iets verandert.
  const todo = items.filter(open).filter((t) => (filter === 'Overdue' ? t.overdue : filter !== 'Done'));
  const done = filter === 'Overdue' ? [] : items.filter((t) => t.status === 'Done');
  const cancelled = filter === 'All' ? items.filter((t) => t.status === 'Cancelled') : [];
  const categories: { name: string; tasks: Task[] }[] = [];
  for (const task of todo) {
    const group = categories.find((c) => c.name === task.categoryName);
    if (group) group.tasks.push(task);
    else categories.push({ name: task.categoryName, tasks: [task] });
  }

  return (
    <Screen>
      <BackLink label="Meer" />
      <LargeTitleHeader title="Mijn taken" />
      <View style={styles.content}>
        <AppText variant="caption" color={colors.textSecondary}>
          Je ziet alleen de taken waar jij op staat.
        </AppText>
        {!mine.data ? (
          <QueryState query={mine} />
        ) : !mine.data.allowed ? (
          <AppText variant="body" color={colors.textSecondary}>
            De takenlijst is er voor het kader. Staat er wel werk voor jou klaar? Vraag het bestuur je op de taak te
            zetten.
          </AppText>
        ) : (
          <>
            <Card style={styles.card}>
              <AppText variant="bodyStrong">
                {mine.data.summary.done} van {mine.data.summary.total}{' '}
                {mine.data.summary.total === 1 ? 'taak' : 'taken'} afgerond
              </AppText>
              <AppText variant="caption" color={colors.textSecondary}>
                Carnaval {mine.data.yearName} · nog {mine.data.daysUntilCarnival}{' '}
                {mine.data.daysUntilCarnival === 1 ? 'dag' : 'dagen'}
              </AppText>
              <View
                style={[styles.track, { backgroundColor: colors.surfaceMuted }]}
                accessibilityRole="progressbar"
                accessibilityValue={{ min: 0, max: 100, now: mine.data.summary.percent }}
              >
                <View style={[styles.bar, { width: `${mine.data.summary.percent}%` }]} />
              </View>
            </Card>
            <Button label="Nieuwe taak" icon="plus" variant="secondary" onPress={() => router.push('/taken/nieuw')} />
            <FilterChips<Filter>
              accessibilityLabel="Toon"
              options={[
                { value: 'Todo', label: `Te doen (${counts.Todo})` },
                { value: 'Overdue', label: `Te laat (${counts.Overdue})` },
                { value: 'Done', label: `Afgerond (${counts.Done})` },
                { value: 'All', label: `Alle (${counts.All})` },
              ]}
              selected={filter}
              onChange={setFilter}
            />
            {categories.map((group) => (
              <View key={group.name} style={styles.group}>
                <SectionHeader title={group.name} />
                {group.tasks.map((task) => (
                  <TaskCard key={task.id} task={task} />
                ))}
              </View>
            ))}
            {done.length > 0 ? (
              <View style={styles.group}>
                <SectionHeader title={`Afgerond (${done.length})`} />
                <AppText variant="caption" color={colors.textSecondary}>
                  Toch nog iets veranderd? Tik erop en pas hem aan.
                </AppText>
                {done.map((task) => (
                  <TaskCard key={task.id} task={task} />
                ))}
              </View>
            ) : null}
            {cancelled.length > 0 ? (
              <View style={styles.group}>
                <SectionHeader title="Niet van toepassing" />
                {cancelled.map((task) => (
                  <TaskCard key={task.id} task={task} />
                ))}
              </View>
            ) : null}
            {todo.length + done.length + cancelled.length === 0 ? (
              <AppText variant="body" color={colors.textSecondary}>
                {filter === 'Todo' && items.length > 0 ? 'Alles afgerond. Alaaf!' : 'Geen taken.'}
              </AppText>
            ) : null}
          </>
        )}
      </View>
    </Screen>
  );
}

/** Eén taak; tikken klapt hem open, net als een adverteerder. */
function TaskCard({ task }: { task: Task }) {
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const [status, setStatus] = useState<Status>(task.status);
  const [progress, setProgress] = useState(task.progress);
  const [note, setNote] = useState(task.note ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const color =
    task.status === 'Done'
      ? colors.successText
      : task.overdue
        ? colors.accentText
        : task.status === 'InProgress'
          ? colors.linkText
          : colors.textSecondary;

  async function save(next: Status = status) {
    setBusy(true);
    setError(null);
    try {
      const { response, error: problem } = await api.PUT('/api/v1/me/tasks/{id}', {
        params: { path: { id: task.id } },
        body: { status: next, progress: next === 'InProgress' ? progress : 0, note: note.trim() || null },
      });
      if (response.ok) {
        setExpanded(false);
        await queryClient.invalidateQueries({ queryKey: queryKeys.myTasks });
      } else {
        setError((problem as { detail?: string } | undefined)?.detail ?? 'Opslaan lukt nu niet.');
      }
    } catch {
      setError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      setBusy(false);
    }
  }

  const due = taskDate(task.dueOn);
  const subtitle =
    task.status === 'Done' && task.completedOn
      ? `Afgerond op ${taskDate(task.completedOn)}`
      : due
        ? `Verwacht klaar ${due}`
        : task.categoryName;

  return (
    <Card style={styles.card}>
      <Pressable
        onPress={() => setExpanded(!expanded)}
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        accessibilityLabel={`${task.title}, ${task.overdue ? 'te laat, ' : ''}${STATUS[task.status]}`}
        style={styles.header}
      >
        <View style={styles.grow}>
          <AppText variant="listTitle">{task.title}</AppText>
          <AppText variant="caption" color={colors.textSecondary}>
            {subtitle}
          </AppText>
        </View>
        <View style={styles.right}>
          <AppText variant="label" color={color}>
            {task.overdue ? 'Te laat' : STATUS[task.status]}
          </AppText>
          {task.status === 'InProgress' || task.status === 'Done' ? (
            <AppText variant="caption" color={colors.textSecondary}>
              {task.status === 'Done' ? '100%' : `${task.progress}%`}
            </AppText>
          ) : null}
        </View>
      </Pressable>
      {task.status === 'InProgress' && !expanded ? (
        <View style={[styles.slimTrack, { backgroundColor: colors.surfaceMuted }]}>
          <View style={[styles.bar, { width: `${task.progress}%` }]} />
        </View>
      ) : null}
      {expanded ? (
        <View style={styles.details}>
          {task.description ? <AppText variant="body">{task.description}</AppText> : null}
          {task.people ? (
            <AppText variant="caption" color={colors.textSecondary}>
              {task.people}
            </AppText>
          ) : null}
          <AppText variant="caption" color={colors.textSecondary}>
            {[
              task.startedOn ? `Gestart op ${taskDate(task.startedOn)}` : null,
              due ? `verwacht klaar ${due}` : null,
            ]
              .filter(Boolean)
              .join(' · ') || task.categoryName}
          </AppText>
          <AppText variant="label" color={colors.textSecondary}>
            Status
          </AppText>
          <FilterChips<Status>
            accessibilityLabel="Status"
            options={CHOICES.map((value) => ({ value, label: STATUS[value] }))}
            selected={status}
            onChange={(value) => {
              setStatus(value);
              if (value === 'Done') setProgress(100);
              if (value === 'NotStarted') setProgress(0);
            }}
          />
          {status === 'InProgress' ? (
            <ScoreSlider label="Hoe ver ben je?" value={progress} onChange={setProgress} step={5} />
          ) : null}
          <TextField label="Opmerking" value={note} onChangeText={setNote} multiline maxLength={500} />
          {error ? (
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
          <Button label="Opslaan" onPress={() => void save()} disabled={busy} />
          {status !== 'Done' ? (
            <Button label="Markeer als afgerond" variant="secondary" onPress={() => void save('Done')} disabled={busy} />
          ) : null}
        </View>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  bar: { height: '100%', backgroundColor: '#39A935', borderRadius: 6 },
  card: { padding: 16, gap: 10 },
  content: { paddingHorizontal: 20, paddingTop: 4, gap: 16 },
  details: { gap: 10 },
  group: { gap: 10 },
  grow: { flex: 1, gap: 2 },
  header: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  right: { alignItems: 'flex-end', gap: 2 },
  slimTrack: { height: 6, borderRadius: 4, overflow: 'hidden' },
  track: { height: 8, borderRadius: 6, overflow: 'hidden' },
});
