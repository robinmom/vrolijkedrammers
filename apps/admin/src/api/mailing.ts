import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

export type MailingKind = Schemas['MailingKind'];
export type MailingStatus = Schemas['MailingStatus'];
export type MailingBlock = Schemas['MailingBlockDto'];
export type MailingRequest = Schemas['MailingRequest'];
export type MailingListRequest = Schemas['MailingListRequest'];
export type MailingPreview = Schemas['MailingPreviewResponse'];

/** Na elke wijziging opnieuw ophalen. */
export const MAILING_KEYS = [['mailing']];

function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

export function useMailings() {
  const api = useApi();
  return useQuery({
    queryKey: ['mailing', 'mailings'],
    queryFn: async () => required((await api.GET('/api/v1/admin/mailing/mailings')).data),
  });
}

/** Tijdens het versturen elke 10 seconden de voortgang verversen. */
export function useMailing(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['mailing', 'mailings', id],
    enabled: id !== null,
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/mailing/mailings/{id}', { params: { path: { id: id! } } })).data),
    refetchInterval: (query) => (query.state.data?.status === 'Sending' ? 10_000 : false),
  });
}

export function useMailingLists() {
  const api = useApi();
  return useQuery({
    queryKey: ['mailing', 'lists'],
    queryFn: async () => required((await api.GET('/api/v1/admin/mailing/lists')).data),
  });
}

export function useMailingList(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['mailing', 'lists', id],
    enabled: id !== null,
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/mailing/lists/{id}', { params: { path: { id: id! } } })).data),
  });
}

export function useMailingUnsubscribes() {
  const api = useApi();
  return useQuery({
    queryKey: ['mailing', 'unsubscribes'],
    queryFn: async () => required((await api.GET('/api/v1/admin/mailing/unsubscribes')).data),
  });
}

export const KIND_LABELS: Record<MailingKind, string> = { Newsletter: 'Nieuwsbrief', Invitation: 'Uitnodiging' };

export const STATUS_LABELS: Record<MailingStatus, string> = {
  Draft: 'Concept',
  Sending: 'Wordt verstuurd',
  Sent: 'Verstuurd',
};
