import type { components } from '@drammers/api-client';
import { useQuery } from '@tanstack/react-query';
import * as SecureStore from 'expo-secure-store';
import { api, unwrap } from '../api/client';
import { useSessionStatus } from '../auth/useSession';

/** Kaartverkoop in de app (fase 19b): catalogus, bestellingen van gasten op dit toestel en Mijn kaarten. */
export type SaleCatalog = components['schemas']['SaleCatalogResponse'];
export type SaleProduct = components['schemas']['SaleProductResponse'];
export type OrderView = components['schemas']['OrderView'];
export type OrderTicketView = components['schemas']['OrderTicketView'];
export type ShareCandidate = components['schemas']['ShareCandidate'];

export const salesKeys = {
  catalog: ['sales', 'catalog'] as const,
  /** Onder 'me': bestellingen bevatten QR-codes en komen dus nooit in de persistente cache. */
  myOrders: ['me', 'orders'] as const,
  guestOrders: ['me', 'guest-orders'] as const,
  order: (id: string) => ['me', 'order', id] as const,
  candidates: (ticketId: string) => ['me', 'share-candidates', ticketId] as const,
};

const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });
export const formatEuro = (cents: number) => euro.format(cents / 100);

const day = new Intl.DateTimeFormat('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'Europe/Amsterdam' });
/** "vrijdag 5 februari" voor een datum zonder tijd (yyyy-mm-dd). */
export const formatDay = (date: string | null | undefined) => (date ? day.format(new Date(`${date}T12:00:00Z`)) : '');

export const useSaleCatalog = () => {
  // Opnieuw ophalen na in- of uitloggen: een lid ziet de groepskaarten.
  const status = useSessionStatus();
  return useQuery({
    queryKey: [...salesKeys.catalog, status],
    queryFn: () => unwrap(api.GET('/api/v1/sales/products')),
    staleTime: 30_000,
  });
};

export const useMyOrders = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: salesKeys.myOrders,
    queryFn: () => unwrap(api.GET('/api/v1/me/orders')),
    enabled: status === 'signedIn',
  });
};

// ---- Bestellingen van gasten ---------------------------------------------------------------------------------------

/** Een gast heeft geen account: de app onthoudt id en token van de bestelling (versleuteld op het toestel). */
export interface GuestOrder {
  id: string;
  token: string;
}

const STORE: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY };
const GUEST_ORDERS = 'dvd.orders.v1';

export async function loadGuestOrders(): Promise<GuestOrder[]> {
  const raw = await SecureStore.getItemAsync(GUEST_ORDERS, STORE).catch(() => null);
  return raw ? (JSON.parse(raw) as GuestOrder[]) : [];
}

export async function rememberOrder(order: GuestOrder): Promise<void> {
  const known = await loadGuestOrders();
  if (!known.some((o) => o.id === order.id)) {
    // Hooguit 20: oude bestellingen vallen eraf (ze staan ook in de e-mail).
    await SecureStore.setItemAsync(GUEST_ORDERS, JSON.stringify([order, ...known].slice(0, 20)), STORE);
  }
}

export async function forgetOrder(id: string): Promise<void> {
  const known = await loadGuestOrders();
  await SecureStore.setItemAsync(GUEST_ORDERS, JSON.stringify(known.filter((o) => o.id !== id)), STORE);
}

export const fetchOrder = (order: GuestOrder) =>
  unwrap(api.GET('/api/v1/sales/orders/{id}', { params: { path: { id: order.id }, query: { t: order.token } } }));

/** Bestellingen op dit toestel (ook zonder account); verlopen of onbekende vallen weg. */
export const useGuestOrders = () =>
  useQuery({
    queryKey: salesKeys.guestOrders,
    queryFn: async () => {
      const results = await Promise.all(
        (await loadGuestOrders()).map(async (o) => {
          try {
            return { ...(await fetchOrder(o)), token: o.token };
          } catch {
            return null;
          }
        }),
      );
      return results.filter((o): o is OrderView & { token: string } => o !== null && (o.status === 'Confirmed' || o.status === 'AwaitingPayment'));
    },
  });

/** Eén bestelling volgen na het betalen: elke 3 seconden tot de betaling binnen is (hooguit 2 minuten). */
export const useOrderStatus = (order: GuestOrder | null) =>
  useQuery({
    queryKey: salesKeys.order(order?.id ?? ''),
    queryFn: () => fetchOrder(order!),
    enabled: order !== null,
    refetchInterval: (query) =>
      query.state.data?.status === 'AwaitingPayment' && query.state.dataUpdateCount < 40 ? 3000 : false,
  });

export const useShareCandidates = (ticketId: string | null) =>
  useQuery({
    queryKey: salesKeys.candidates(ticketId ?? ''),
    enabled: ticketId !== null,
    queryFn: () =>
      unwrap(api.GET('/api/v1/me/orders/tickets/{ticketId}/share-candidates', { params: { path: { ticketId: ticketId! } } })),
  });

/** Munten die betaald zijn en nog opgehaald moeten worden. */
export function tokensToCollect(orders: OrderView[] | undefined): number {
  return (orders ?? [])
    .filter((o) => o.kind === 'Tokens' && o.status === 'Confirmed')
    .flatMap((o) => o.tickets)
    .filter((t) => t.status === 'Active')
    .reduce((sum, t) => sum + t.quantity, 0);
}

/** Probleemmelding van de API (ProblemDetails) of een algemene tekst. */
export function problemMessage(error: unknown, status: number): string {
  if (status === 429) return 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.';
  const detail = (error as { detail?: string } | undefined)?.detail;
  return detail ?? 'Dat lukt nu niet. Probeer het opnieuw.';
}

export const problemCode = (error: unknown) => (error as { code?: string } | undefined)?.code;
