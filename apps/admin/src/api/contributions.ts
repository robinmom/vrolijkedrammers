import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

export type ContributionOverview = Schemas['ContributionOverview'];
export type ContributionLine = Schemas['ContributionLine'];
export type ContributionRate = Schemas['ContributionRate'];
export type MembershipKind = NonNullable<Schemas['MembershipKind']>;
export type MembershipSettings = Schemas['MembershipSettings'];

/** openapi-fetch geeft `data | undefined`; fouten gooit de middleware al als ApiError. */
function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });

/** Bedragen komen als euro's (decimal) uit de API. */
export function formatAmount(value: number | undefined): string {
  return euro.format(value ?? 0);
}

export const kindLabels: Record<MembershipKind, string> = {
  OnePerson: 'Eén persoon',
  TwoPersons: 'Twee personen',
  Partner: 'Partner (tweede persoon)',
  Dansgarde: 'Dansgarde',
};

export function kindLabel(kind: MembershipKind | null | undefined, senior: boolean): string {
  if (!kind) return 'Onbekend';
  const label = kind === 'Partner' ? 'Partner' : kindLabels[kind];
  return senior && (kind === 'OnePerson' || kind === 'TwoPersons') ? `${label} (65+)` : label;
}

/** Contributie per actief lid op de peildatum (fase 23a). */
export function useContributions(date: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['contributions', date],
    queryFn: async () => required((await api.GET('/api/v1/admin/contributions', { params: { query: { date } } })).data),
  });
}

export function useContributionRates() {
  const api = useApi();
  return useQuery({
    queryKey: ['contributions', 'rates'],
    queryFn: async () => required((await api.GET('/api/v1/admin/contributions/rates')).data),
  });
}

export function useMembershipSettings(memberId: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['contributions', 'member', memberId],
    enabled,
    queryFn: async () =>
      required(
        (await api.GET('/api/v1/admin/contributions/members/{memberId}', { params: { path: { memberId } } })).data,
      ),
  });
}
