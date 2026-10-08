import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

export type AdvertiserKind = Schemas['AdvertiserKind'];
export type AdvertiserPayment = Schemas['AdvertiserPayment'];
export type AdvertiserYearStatus = Schemas['AdvertiserYearStatus'];
export type AdvertiserRequest = Schemas['AdvertiserRequest'];
export type AdvertiserImportPreview = Schemas['AdvertiserImportPreview'];

export const ADVERTISER_KEYS = [['advertisers']];

export const KIND_LABELS: Record<AdvertiserKind, string> = {
  Advertisement: 'Advertentie',
  FreeGift: 'Vrije gift',
  Gift: 'Gift',
};
export const PAYMENT_LABELS: Record<AdvertiserPayment, string> = {
  Mandate: 'Machtiging',
  Cash: 'Contant',
  Invoice: 'Rekening',
};

/** Jaar Y is het carnavalsjaar (Y-1)/Y, zoals de kolom BIJDRAGE Y in het Excel-overzicht. */
export const season = (year: number) => `${year - 1}/${year}`;

/** Kort, voor kolomkoppen: 2026 → 25/26. */
export const seasonShort = (year: number) => `${String(year - 1).slice(2)}/${String(year).slice(2)}`;
export const STATUS_LABELS: Record<AdvertiserYearStatus, string> = {
  Open: 'Open',
  Collected: 'Opgehaald',
  Stopped: 'Stopt',
};

const euroFormat = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });
export const euro = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : euroFormat.format(value);

function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

export interface AdvertiserFilters {
  search: string;
  collector: string;
  kind: AdvertiserKind | '';
  payment: AdvertiserPayment | '';
}

export function useAdvertisers(filters: AdvertiserFilters) {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'list', filters],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/advertisers', {
            params: {
              query: {
                search: filters.search || undefined,
                collector: filters.collector || undefined,
                kind: filters.kind || undefined,
                payment: filters.payment || undefined,
              },
            },
          })
        ).data,
      ),
  });
}

export function useAdvertiser(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'detail', id],
    enabled: id !== null,
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/advertisers/{id}', { params: { path: { id: id! } } })).data),
  });
}

export function useAdvertiserCollectors() {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'collectors'],
    queryFn: async () => required((await api.GET('/api/v1/admin/advertisers/collectors')).data),
  });
}

export function useCampaignYear() {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'campaign-year'],
    queryFn: async () => required((await api.GET('/api/v1/admin/advertisers/campaign-year')).data),
  });
}

/** Informatie voor de collectanten van een campagnejaar (fase 27h). */
export function useAdvertiserInfo(year: number | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'info', year],
    enabled: year !== null,
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/advertisers/info', { params: { query: { year: year! } } })).data),
  });
}

export function useAdvertiserStatus(year: number | null, collector: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['advertisers', 'status', year, collector],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/advertisers/status', {
            params: { query: { year: year ?? undefined, collector: collector || undefined } },
          })
        ).data,
      ),
  });
}
