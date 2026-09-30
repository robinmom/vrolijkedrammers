import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

/** Kaartverkoop (fase 19): typen en queries voor het portal. */
export type SaleProduct = Schemas['AdminSaleProductResponse'];
export type SaleProductKind = NonNullable<Schemas['SaleProductKind']>;
export type SaleOrderRow = Schemas['SaleOrderRow'];
export type SaleOrderStatus = Schemas['SaleOrderStatus'];
export type SalesSummary = Schemas['SalesSummary'];
export type Evening = Schemas['Evening'];
export type EveningRow = Schemas['EveningRow'];
export type WaitlistRow = Schemas['WaitlistRow'];
export type GroupAllowance = Schemas['GroupAllowance'];
export type PortalPayment = Schemas['PortalPayment'];

/** Alles wat na een wijziging opnieuw geladen moet worden. */
export const SALES_KEYS: string[][] = [['sales']];

function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

export const kindLabels: Record<SaleProductKind, string> = {
  Pronkzitting: 'Pronkzitting (avond)',
  DayTicket: 'Dagkaart carnaval',
  EventTicket: 'Kaart voor een activiteit',
  Tokens: 'Consumptiemunten',
};

export const statusLabels: Record<SaleOrderStatus, string> = {
  AwaitingPayment: 'Wacht op betaling',
  Confirmed: 'Betaald',
  Cancelled: 'Geannuleerd',
  Expired: 'Verlopen',
};

const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });

export function formatEuro(cents: number): string {
  return euro.format(cents / 100);
}

export function useSalesSummary(kind: SaleProductKind) {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'summary', kind],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/summary', { params: { query: { kind } } })).data),
  });
}

export function useSaleProducts() {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'products'],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/products')).data),
  });
}

export function useSaleOrders(kind: SaleProductKind, productId: string, status: string, search: string, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'orders', kind, productId, status, search, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/sales/orders', {
            params: {
              query: {
                kind,
                productId: productId || undefined,
                status: (status || undefined) as SaleOrderStatus | undefined,
                search: search || undefined,
                page,
                pageSize: 25,
              },
            },
          })
        ).data,
      ),
  });
}

export function useSaleGroups(enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'groups'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/groups')).data),
  });
}

export function useEvenings() {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'pronkzitting'],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/pronkzitting')).data),
  });
}

export function useWaitlist(productId: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'waitlist', productId],
    enabled: productId !== null,
    queryFn: async () =>
      required(
        (await api.GET('/api/v1/admin/sales/products/{id}/waitlist', { params: { path: { id: productId! } } })).data,
      ),
  });
}

/** Eén pagina per soort product onder Verkoop (portalmenu). */
export const kindPages: Record<SaleProductKind, { title: string; path: string; subtitle: string; unit: string }> = {
  Pronkzitting: {
    title: 'Pronkzitting',
    path: '/verkoop/pronkzitting',
    subtitle:
      'Wie komt er per avond: groepen met het aantal personen en losse kaarten op naam. Leden bestellen gratis voor hun groep; niet-leden betalen met iDEAL. Uitnodigen van de wachtlijst gaat op volgorde; het bestuur kan een plek ook zelf toekennen.',
    unit: 'kaarten',
  },
  DayTicket: {
    title: 'Dagkaarten',
    path: '/verkoop/dagkaarten',
    subtitle: 'Dagkaarten voor carnaval, één product per dag. Voor gasten: leden hebben hun ledenticket (Mijn QR).',
    unit: 'kaarten',
  },
  EventTicket: {
    title: 'Activiteiten',
    path: '/verkoop/activiteiten',
    subtitle: 'Kaarten voor een activiteit uit de agenda, met eigen prijs, aantal plaatsen en verkoopperiode.',
    unit: 'kaarten',
  },
  Tokens: {
    title: 'Munten',
    path: '/verkoop/munten',
    subtitle:
      'Consumptiemunten: alleen voor leden en persoonsgebonden. Het lid haalt de munten zelf op bij de kassa met de munten-QR in de app; afhalen kan pas als de betaling is afgerond.',
    unit: 'munten',
  },
};
