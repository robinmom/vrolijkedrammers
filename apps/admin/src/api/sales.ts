import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

/** Kaartverkoop (fase 19): typen en queries voor het portal. */
export type SaleProduct = Schemas['AdminSaleProductResponse'];
export type SaleProductKind = Schemas['SaleProductKind'];
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

export function useSalesSummary() {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'summary'],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/summary')).data),
  });
}

export function useSaleProducts() {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'products'],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/products')).data),
  });
}

export function useSaleOrders(productId: string, status: string, search: string, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'orders', productId, status, search, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/sales/orders', {
            params: {
              query: {
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

export function useTokenOrders() {
  const api = useApi();
  return useQuery({
    queryKey: ['sales', 'tokens'],
    queryFn: async () => required((await api.GET('/api/v1/admin/sales/tokens')).data),
  });
}
