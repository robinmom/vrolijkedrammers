import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from './client';

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
  newsItem: (id: string) => ['news', id] as const,
  albums: ['photo-albums'] as const,
  album: (id: string) => ['photo-albums', id] as const,
  photos: (albumId: string) => ['photo-albums', albumId, 'photos'] as const,
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

/** Nieuws, nieuwste eerst. */
export const useNews = () =>
  useQuery({
    queryKey: queryKeys.news,
    queryFn: async () => (await unwrap(api.GET('/api/v1/news', { params: { query: PAGE } }))).items,
  });

export const useNewsItem = (id: string) =>
  useQuery({
    queryKey: queryKeys.newsItem(id),
    queryFn: () => unwrap(api.GET('/api/v1/news/{id}', { params: { path: { id } } })),
  });

export const usePhotoAlbums = () =>
  useQuery({
    queryKey: queryKeys.albums,
    queryFn: async () => (await unwrap(api.GET('/api/v1/photo-albums', { params: { query: PAGE } }))).items,
  });

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
