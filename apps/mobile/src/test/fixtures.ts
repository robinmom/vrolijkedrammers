import type { AppConfig, CarnivalYear, EventDetail, EventSummary, NewsDetail, NewsSummary, Photo, PhotoAlbum } from '../api/client';

/** Testdata volgens de API-contracten van fase 5, met de voorbeelden uit Figma. */
export const carnivalYear: CarnivalYear = {
  id: 1,
  name: '2026/2027',
  startDate: '2026-11-11',
  endDate: '2027-02-10',
  carnivalStartDate: '2027-02-06',
  carnivalEndDate: '2027-02-09',
};

export const appConfig: AppConfig = {
  minAppVersion: { ios: '1.0.0', android: '1.0.0' },
  recommendedAppVersion: '1.0.0',
  maintenance: { enabled: false, message: null },
  supportEmail: 'info@example.org',
  features: {},
};

const carnaval = { id: 1, code: 'carnaval', name: 'Carnaval' };
const jeugd = { id: 2, code: 'jeugd', name: 'Jeugd' };

const event = (e: Partial<EventSummary> & Pick<EventSummary, 'id' | 'title' | 'startAt'>): EventSummary => ({
  summary: null,
  endAt: null,
  allDay: false,
  locationName: null,
  category: carnaval,
  isHighlight: false,
  badgeText: null,
  imageUrl: null,
  ...e,
});

export const events: EventSummary[] = [
  event({ id: '11111111-1111-4111-8111-111111111111', title: 'Elfde van de Elfde', startAt: '2026-11-11T10:11:00Z', locationName: 'Dorpsplein Loil' }),
  event({ id: '22222222-2222-4222-8222-222222222222', title: 'Kindermiddag', startAt: '2027-01-24T13:00:00Z', locationName: 'Feesttent Loil', category: jeugd }),
  event({ id: '33333333-3333-4333-8333-333333333333', title: 'Optocht Loil', startAt: '2027-02-07T12:30:00Z', locationName: 'Centrum Loil', isHighlight: true }),
];

export const eventDetail: EventDetail = {
  id: '44444444-4444-4444-8444-444444444444',
  title: 'Pronkzitting 2027',
  summary: 'Zaal open om 19:30 uur',
  descriptionHtml: '<p>Een avond vol <strong>büttenreden</strong>, dans en muziek. Alaaf!</p>',
  startAt: '2027-01-16T19:00:00Z',
  endAt: '2027-01-16T23:30:00Z',
  allDay: false,
  locationName: 'De Drammersbühne',
  locationAddress: 'Dorpsstraat 12, Loil',
  latitude: null,
  longitude: null,
  category: carnaval,
  isHighlight: false,
  badgeText: 'Bijna uitverkocht',
  imageUrl: null,
  attachments: [],
};

export const news: NewsSummary[] = [
  {
    id: '55555555-5555-4555-8555-555555555555',
    title: 'De optocht-inschrijving is geopend!',
    summary: 'Schrijf je groep, wagen of loopgroep nu in.',
    category: 'Optocht',
    publishedAt: '2026-09-24T08:00:00Z',
    imageUrl: null,
  },
  { id: '66666666-6666-4666-8666-666666666666', title: 'Uitslag Dansgarde Festival 2026', summary: null, category: 'Uitslagen', publishedAt: '2026-09-20T08:00:00Z', imageUrl: null },
];

export const newsDetail: NewsDetail = { ...news[0]!, bodyHtml: '<p>Schrijf je groep nu in voor de <a href="https://example.org">optocht</a>.</p>' };

export const albums: PhotoAlbum[] = [
  { id: '77777777-7777-4777-8777-777777777777', title: 'Pronkzitting 2026', albumDate: '2026-01-17', description: null, photoCount: 2, coverUrl: null },
];

export const photos: Photo[] = [
  { id: '88888888-8888-4888-8888-888888888881', thumbnailUrl: 'https://example.org/t1', displayUrl: 'https://example.org/d1', width: 1600, height: 1067, caption: 'De Prins', photographer: 'Fotograaf', takenAt: null },
  { id: '88888888-8888-4888-8888-888888888882', thumbnailUrl: 'https://example.org/t2', displayUrl: 'https://example.org/d2', width: 1600, height: 1067, caption: null, photographer: null, takenAt: null },
];
