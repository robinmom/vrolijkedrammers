import * as data from './fixtures';

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length });

/** Antwoorden per API-pad voor schermtests (zie mockApi). */
export const api = {
  '/api/v1/app-config': data.appConfig,
  '/api/v1/carnival-years/current': data.carnivalYear,
  '/api/v1/event-categories': [
    { id: 1, code: 'carnaval', name: 'Carnaval' },
    { id: 2, code: 'jeugd', name: 'Jeugd' },
    { id: 3, code: 'vereniging', name: 'Vereniging' },
  ],
  '/api/v1/events': paged(data.events),
  [`/api/v1/events/${data.eventDetail.id}`]: data.eventDetail,
  '/api/v1/news': paged(data.news),
  [`/api/v1/news/${data.newsDetail.id}`]: data.newsDetail,
  '/api/v1/photo-albums': paged(data.albums),
  [`/api/v1/photo-albums/${data.albums[0]!.id}`]: data.albums[0],
  [`/api/v1/photo-albums/${data.albums[0]!.id}/photos`]: data.photos,
};
