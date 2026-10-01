import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useSessionStatus } from '../auth/useSession';
import { ApiError, api, unwrap } from './client';

/**
 * Querysleutels op één plek, zodat de persistente cache (offline) en pull-to-refresh dezelfde data raken.
 * De app haalt één ruime pagina op: de vereniging heeft per seizoen tientallen items, geen duizenden.
 */
export const queryKeys = {
  appConfig: ['app-config'] as const,
  carnivalYear: ['carnival-year'] as const,
  categories: ['event-categories'] as const,
  events: ['events'] as const,
  event: (id: string) => ['events', id] as const,
  news: ['news'] as const,
  /** Nieuws van een ouder carnavalsjaar (fase 21g); zonder jaar het actieve jaar. */
  newsOfSeason: (season: string) => ['news', 'season', season] as const,
  newsSeasons: ['news', 'seasons'] as const,
  newsItem: (id: string) => ['news', id] as const,
  albums: ['photo-albums'] as const,
  albumsOfSeason: (season: string) => ['photo-albums', 'season', season] as const,
  albumSeasons: ['photo-albums', 'seasons'] as const,
  album: (id: string) => ['photo-albums', id] as const,
  photos: (albumId: string) => ['photo-albums', albumId, 'photos'] as const,
  /** Persoonlijke gegevens: nooit in de persistente cache (zie QueryProvider). */
  me: ['me'] as const,
  myMember: ['me', 'member'] as const,
  myDevices: ['me', 'devices'] as const,
  myChildren: ['me', 'children'] as const,
  child: (id: string) => ['me', 'children', id] as const,
  childTicket: (id: string) => ['me', 'children', id, 'ticket'] as const,
  myGuardianRequests: ['me', 'guardian-requests'] as const,
  myNotifications: ['me', 'notifications'] as const,
  myNotificationPreferences: ['me', 'notification-preferences'] as const,
  parade: ['parade'] as const,
  /** Jureren (fase 22b): bewaard in de cache, zodat het jurylid ook zonder netwerk de optocht ziet. */
  jury: ['jury', 'current'] as const,
  paradeResults: ['parade', 'results'] as const,
  paradeCategories: ['parade', 'categories'] as const,
  arrivalTimes: ['parade', 'arrival-times'] as const,
  myRegistrations: ['me', 'parade-registrations'] as const,
  myRegistration: (id: string) => ['me', 'parade-registrations', id] as const,
  myBuildLocations: ['me', 'build-locations'] as const,
  myRegistrationDocuments: (id: string) => ['me', 'parade-registrations', id, 'documents'] as const,
  myTicket: ['me', 'ticket'] as const,
  accessStatus: ['me', 'access-status'] as const,
  /** Alleen in het geheugen (sleutels onder 'me' worden niet bewaard, zie QueryProvider). */
  offlinePack: ['me', 'offline-pack'] as const,
};

const PAGE = { page: 1, pageSize: 100 };

export const useAppConfig = () =>
  useQuery({ queryKey: queryKeys.appConfig, queryFn: () => unwrap(api.GET('/api/v1/app-config')) });

export const useCarnivalYear = () =>
  useQuery({ queryKey: queryKeys.carnivalYear, queryFn: () => unwrap(api.GET('/api/v1/carnival-years/current')) });

export const useEventCategories = () =>
  useQuery({ queryKey: queryKeys.categories, queryFn: () => unwrap(api.GET('/api/v1/event-categories')) });

/** Komende activiteiten (de API begint standaard bij vandaag), oplopend op datum. */
export const useEvents = () =>
  useQuery({
    queryKey: queryKeys.events,
    queryFn: async () => (await unwrap(api.GET('/api/v1/events', { params: { query: PAGE } }))).items,
  });

export const useEvent = (id: string) =>
  useQuery({
    queryKey: queryKeys.event(id),
    queryFn: () => unwrap(api.GET('/api/v1/events/{id}', { params: { path: { id } } })),
  });

/** Nieuws van het actieve carnavalsjaar, of van een ouder jaar (bijvoorbeeld "2025-2026"); nieuwste eerst. */
export const useNews = (season?: string | null) =>
  useQuery({
    queryKey: season ? queryKeys.newsOfSeason(season) : queryKeys.news,
    queryFn: async () =>
      (await unwrap(api.GET('/api/v1/news', { params: { query: { ...PAGE, season: season ?? undefined } } }))).items,
  });

/** Het actieve carnavalsjaar en de oudere jaren met nieuws (voor de knoppen onder het nieuws). */
export const useNewsSeasons = () =>
  useQuery({ queryKey: queryKeys.newsSeasons, queryFn: () => unwrap(api.GET('/api/v1/news/seasons')) });

export const useNewsItem = (id: string) =>
  useQuery({
    queryKey: queryKeys.newsItem(id),
    queryFn: () => unwrap(api.GET('/api/v1/news/{id}', { params: { path: { id } } })),
  });

/** Albums van het actieve carnavalsjaar, of van een ouder jaar. */
export const usePhotoAlbums = (season?: string | null) =>
  useQuery({
    queryKey: season ? queryKeys.albumsOfSeason(season) : queryKeys.albums,
    queryFn: async () =>
      (await unwrap(api.GET('/api/v1/photo-albums', { params: { query: { ...PAGE, season: season ?? undefined } } }))).items,
  });

export const useAlbumSeasons = () =>
  useQuery({ queryKey: queryKeys.albumSeasons, queryFn: () => unwrap(api.GET('/api/v1/photo-albums/seasons')) });

export const usePhotoAlbum = (id: string | undefined) =>
  useQuery({
    queryKey: queryKeys.album(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/photo-albums/{id}', { params: { path: { id: id ?? '' } } })),
    enabled: Boolean(id),
  });

export const useAlbumPhotos = (albumId: string | undefined) =>
  useQuery({
    queryKey: queryKeys.photos(albumId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/photo-albums/{id}/photos', { params: { path: { id: albumId ?? '' } } })),
    enabled: Boolean(albumId),
  });

/** Profiel, rollen en permissions van de ingelogde gebruiker. */
export const useMe = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
    enabled: status === 'signedIn',
  });
};

/** Eigen lidgegevens uit e-Boekhouden (read-only); alleen als het account aan een lid is gekoppeld. */
export const useMyMember = (enabled = true) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myMember,
    queryFn: () => unwrap(api.GET('/api/v1/me/member')),
    enabled: status === 'signedIn' && enabled,
  });
};

/** Kinderen waarvan de gebruiker ouder/verzorger is (fase 9b; fase 17: tot 18, met QR en eigen account). */
export const useMyChildren = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myChildren,
    queryFn: () => unwrap(api.GET('/api/v1/me/children')),
    enabled: status === 'signedIn',
  });
};

/** Kind-detail voor de ouder (fase 17): gegevens, ouders, meldingen namens het kind en de optocht. */
export const useChild = (id: string) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.child(id),
    queryFn: () => unwrap(api.GET('/api/v1/me/children/{memberId}', { params: { path: { memberId: id } } })),
    enabled: status === 'signedIn',
  });
};

/** Koppelverzoeken van deze ouder (fase 17); het bestuur beoordeelt ze in het portal. */
export const useMyGuardianRequests = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myGuardianRequests,
    queryFn: () => unwrap(api.GET('/api/v1/me/guardian-requests')),
    enabled: status === 'signedIn',
  });
};

export const useMyDevices = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myDevices,
    queryFn: () => unwrap(api.GET('/api/v1/me/devices')),
    enabled: status === 'signedIn',
  });
};

/** Inbox (fase 10): per 30, nieuwste eerst; ook meldingen waarvoor push uit stond. */
export const useMyNotifications = () => {
  const status = useSessionStatus();
  return useInfiniteQuery({
    queryKey: queryKeys.myNotifications,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => unwrap(api.GET('/api/v1/me/notifications', { params: { query: { page: pageParam } } })),
    getNextPageParam: (last, pages) => (last.hasMore ? pages.length + 1 : undefined),
    enabled: status === 'signedIn',
  });
};

export const useMyNotificationPreferences = () => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myNotificationPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/notification-preferences')),
    enabled: status === 'signedIn',
  });
};

/** Huidige optocht met datum, inschrijfperiode en de informatietekst uit het portal (fase 11). 404 = nog geen optocht. */
export const useParade = () =>
  useQuery({ queryKey: queryKeys.parade, queryFn: () => unwrap(api.GET('/api/v1/parade/current')) });

/** Openbare aanrijtijdenlijst (fase 16): alleen wagens, pas na publiceren. */
export const useArrivalTimes = () =>
  useQuery({ queryKey: queryKeys.arrivalTimes, queryFn: () => unwrap(api.GET('/api/v1/parade/arrival-times')) });

export const useParadeCategories = () =>
  useQuery({ queryKey: queryKeys.paradeCategories, queryFn: () => unwrap(api.GET('/api/v1/parade/categories')) });

/** Eigen inschrijvingen (als beheerder of mede-beheerder); alleen met het recht parade.register. */
export const useMyRegistrations = (enabled: boolean) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myRegistrations,
    queryFn: () => unwrap(api.GET('/api/v1/parade/registrations')),
    enabled: status === 'signedIn' && enabled,
  });
};

export const useMyRegistration = (id: string | undefined) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myRegistration(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/parade/registrations/{id}', { params: { path: { id: id ?? '' } } })),
    enabled: status === 'signedIn' && Boolean(id),
  });
};

/** Bewaarde bouwlocaties: "zelfde locatie" of een nieuwe; oude kunnen weg. */
export const useMyBuildLocations = (enabled: boolean) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myBuildLocations,
    queryFn: () => unwrap(api.GET('/api/v1/parade/build-locations')),
    enabled: status === 'signedIn' && enabled,
  });
};

export const useRegistrationDocuments = (id: string | undefined) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myRegistrationDocuments(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/parade/registrations/{id}/documents', { params: { path: { id: id ?? '' } } })),
    enabled: status === 'signedIn' && Boolean(id),
  });
};

/** Ledenticket voor Mijn QR (fase 13); de app bewaart de gegevens ook op het toestel voor gebruik zonder internet. */
export const useMyTicket = (enabled = true) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.myTicket,
    queryFn: () => unwrap(api.GET('/api/v1/me/ticket')),
    enabled: status === 'signedIn' && enabled,
    retry: false,
  });
};

/** Het ticket van een kind, getoond op de telefoon van de ouder (fase 17). */
export const useChildTicket = (id: string | null) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.childTicket(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/me/children/{memberId}/ticket', { params: { path: { memberId: id ?? '' } } })),
    enabled: status === 'signedIn' && Boolean(id),
    retry: false,
  });
};

/** Activiteit met toegangscontrole van dit moment en de tellers (fase 14, rol Deurcontrole). */
export const useAccessStatus = (enabled: boolean) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.accessStatus,
    queryFn: () => unwrap(api.GET('/api/v1/access/status')),
    enabled: status === 'signedIn' && enabled,
    refetchInterval: 30_000,
  });
};

/** Controlelijst voor offline scannen (fase 15, lichte variant): alleen in het geheugen, elke 5 minuten ververst. */
export const useOfflinePack = (enabled: boolean) => {
  const status = useSessionStatus();
  return useQuery({
    queryKey: queryKeys.offlinePack,
    queryFn: () => unwrap(api.GET('/api/v1/access/offline-pack')),
    enabled: status === 'signedIn' && enabled,
    refetchInterval: 5 * 60_000,
    gcTime: 0,
  });
};

/** De optocht om te jureren met de eigen scores (alleen met <c>parade.judge</c>). */
export const useJurySession = (enabled: boolean) =>
  useQuery({ queryKey: queryKeys.jury, queryFn: () => unwrap(api.GET('/api/v1/jury/current')), enabled });

/** De gepubliceerde uitslag van de optocht (fase 22c), of <c>null</c> zolang er nog niets is gepubliceerd. */
export const useParadeResults = () =>
  useQuery({
    queryKey: queryKeys.paradeResults,
    queryFn: async () => {
      try {
        return await unwrap(api.GET('/api/v1/parade/results'));
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
  });
