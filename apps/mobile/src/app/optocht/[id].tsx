import { useQueryClient } from '@tanstack/react-query';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyRegistration, useParadeCategories } from '../../api/queries';
import { formatAddress, formFromRegistration, problemFrom, statusBadge, statusLabels } from '../../features/parade';
import { fullDate } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Badge, Button, Card, LargeTitleHeader, QueryState, Screen } from '../../ui';

/** Mijn inschrijving (Figma 10): status, opgave- en startnummer; bewerken zolang het statusbeleid dat toestaat, of intrekken. */
export default function InschrijvingScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { colors } = useTheme();
  const queryClient = useQueryClient();
  const registration = useMyRegistration(id);
  const categories = useParadeCategories();
  const [error, setError] = useState<string | null>(null);
  const r = registration.data;

  const removeDraft = () =>
    Alert.alert(
      'Concept verwijderen',
      'Weet je het zeker? Het concept en de toegevoegde documenten worden verwijderd.',
      [
        { text: 'Annuleren', style: 'cancel' },
        {
          text: 'Verwijderen',
          style: 'destructive',
          onPress: async () => {
            const { error: problem, response } = await api.DELETE('/api/v1/parade/registrations/{id}', {
              params: { path: { id } },
            });
            if (!response.ok) return setError(problemFrom(response.status, problem).message);
            queryClient.removeQueries({ queryKey: queryKeys.myRegistration(id) });
            await queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrations });
            router.back();
          },
        },
      ],
    );

  const withdraw = () =>
    Alert.alert(
      'Inschrijving intrekken',
      'Weet je het zeker? Jullie doen dan niet mee aan de optocht; het opgavenummer vervalt.',
      [
        { text: 'Annuleren', style: 'cancel' },
        {
          text: 'Intrekken',
          style: 'destructive',
          onPress: async () => {
            const {
              data,
              error: problem,
              response,
            } = await api.POST('/api/v1/parade/registrations/{id}/withdraw', {
              params: { path: { id } },
              body: { reason: null },
            });
            if (!data) return setError(problemFrom(response.status, problem).message);
            queryClient.setQueryData(queryKeys.myRegistration(id), data);
            queryClient.invalidateQueries({ queryKey: queryKeys.myRegistrations });
          },
        },
      ],
    );

  const explanation: Partial<Record<string, string>> = {
    Draft: 'Nog niet ingediend. Je krijgt pas een opgavenummer na het indienen.',
    Submitted: 'De optochtcommissie gaat jullie inschrijving beoordelen. Pas na goedkeuring is hij definitief.',
    UnderReview: 'De optochtcommissie beoordeelt jullie inschrijving.',
    AdditionalInformationRequired:
      'De optochtcommissie heeft meer informatie nodig. Pas de inschrijving aan en dien de aanvulling in; daarna wordt ze opnieuw beoordeeld.',
    Approved: 'Goedgekeurd: jullie inschrijving is definitief. Het startnummer en de aanrijtijd volgen.',
    Rejected: 'De inschrijving is afgewezen; de reden staat in de e-mail van de commissie.',
    Withdrawn: 'Deze inschrijving is ingetrokken.',
  };

  return (
    <Screen>
      <BackLink label="Optocht" />
      <LargeTitleHeader title={r?.groupName ?? 'Inschrijving'} />
      <QueryState query={registration} notFoundTitle="Inschrijving niet gevonden" />
      {r ? (
        <View style={styles.content}>
          <Card style={styles.card}>
            <View style={styles.badge}>
              <Badge label={statusLabels[r.status]} variant={statusBadge(r.status)} size="regular" />
            </View>
            <AppText variant="body" color={colors.textSecondary}>
              {explanation[r.status] ?? ''}
            </AppText>
            <Row label="Opgavenummer" value={r.registrationNumber ? String(r.registrationNumber) : 'Na het indienen'} />
            <Row label="Startnummer" value={r.startNumber ? String(r.startNumber) : 'Volgt na de indeling'} />
            {r.arrivalTime ? (
              <Row
                label="Aanrijtijd"
                value={`${r.arrivalTime} uur${r.arrivalLocation ? ` · ${r.arrivalLocation}` : ''}`}
              />
            ) : null}
            <Row
              label="Categorie"
              value={`${categories.data?.find((c) => c.id === r.categoryId)?.name ?? '–'} · ${r.adultCount + r.childrenCount} deelnemers`}
            />
            <Row label="Bouwlocatie" value={formatAddress(formFromRegistration(r).build) || '–'} />
            {r.submittedAt ? <Row label="Ingediend" value={fullDate(r.submittedAt)} /> : null}
          </Card>
          {error ? (
            <AppText variant="body" color={colors.accentText} accessibilityRole="alert">
              {error}
            </AppText>
          ) : null}
          {r.status === 'AdditionalInformationRequired' && r.reviewReason ? (
            <Card style={styles.card}>
              <AppText variant="bodyStrong">Gevraagd door de optochtcommissie</AppText>
              <AppText variant="body" color={colors.textSecondary}>
                {r.reviewReason}
              </AppText>
            </Card>
          ) : null}
          {r.editableFields.length > 0 ? (
            <Button
              label={
                r.status === 'Draft'
                  ? 'Verder invullen'
                  : r.status === 'AdditionalInformationRequired'
                    ? 'Aanvulling invullen'
                    : 'Gegevens wijzigen'
              }
              onPress={() => router.push({ pathname: '/optocht/inschrijven', params: { id: r.id } })}
            />
          ) : null}
          {r.status === 'Draft' ? (
            <Button label="Concept verwijderen" variant="secondary" onPress={removeDraft} />
          ) : null}
          {r.canWithdraw ? <Button label="Intrekken" variant="secondary" onPress={withdraw} /> : null}
        </View>
      ) : null}
    </Screen>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.row} accessible accessibilityLabel={`${label}: ${value}`}>
      <AppText variant="caption" color={colors.textSecondary}>
        {label}
      </AppText>
      <AppText variant="bodyStrong">{value}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { gap: 2 },
  content: { paddingHorizontal: 20, gap: 14 },
  card: { padding: 16, gap: 12 },
  badge: { alignSelf: 'flex-start' },
});
