import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

/** Websitebeheer (fase 21a): typen, labels en queries voor de menukop Website. */
export type WebsiteSettings = Schemas['WebsiteSettingsResponse'];
export type WebsiteSettingsRequest = Schemas['WebsiteSettingsRequest'];
export type WebsiteLink = NonNullable<Schemas['WebsiteLink']>;
export type WebsitePageSummary = Schemas['WebsitePageSummaryResponse'];
export type WebsitePageRequest = Schemas['WebsitePageRequest'];
export type Committee = Schemas['CommitteeResponse'];
export type CommitteeMember = Schemas['CommitteeMemberResponse'];
export type CommitteeMemberRequest = Schemas['CommitteeMemberRequest'];
export type Prince = Schemas['PrinceResponse'];
export type PrinceKind = NonNullable<Schemas['PrinceKind']>;
export type PrinceRequest = Schemas['PrinceRequest'];
export type Award = Schemas['AwardResponse'];
export type AwardType = NonNullable<Schemas['AwardType']>;
export type AwardRequest = Schemas['AwardRequest'];

/** Alles wat na een wijziging opnieuw geladen moet worden. */
export const WEBSITE_KEYS: string[][] = [['website']];

export const linkLabels: Record<WebsiteLink, string> = {
  Agenda: 'Agenda',
  News: 'Nieuws',
  Photos: "Foto's",
  Parade: 'Optocht (informatie)',
  ParadeRegistration: 'Optocht inschrijven',
  Membership: 'Lid worden',
  Tickets: 'Kaarten en munten',
  App: 'Download de app',
  Contact: 'Contact',
};

export const awardLabels: Record<AwardType, string> = {
  Drammertje: "'t Drammertje",
  VerdienstelijkeDidammer: 'De Verdienstelijke Didammer',
  EikenloofVanBoschslag: 'Het Eikenloof van Boschslag',
};

export const princeKindLabels: Record<PrinceKind, string> = {
  Prince: 'Prinsen',
  YouthPrince: 'Jeugdprinsen',
};

function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

export function useWebsiteSettings() {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'settings'],
    queryFn: async () => required((await api.GET('/api/v1/admin/website/settings')).data),
  });
}

/** Het verzoek met alle huidige instellingen; de hero- en instellingenpagina wijzigen elk een deel. */
export function settingsRequest(s: WebsiteSettings): WebsiteSettingsRequest {
  return {
    heroEyebrow: s.heroEyebrow,
    heroTitle: s.heroTitle,
    heroSubtitle: s.heroSubtitle,
    heroPrimaryLabel: s.heroPrimaryLabel,
    heroPrimaryLink: s.heroPrimaryLink,
    heroSecondaryLabel: s.heroSecondaryLabel,
    heroSecondaryLink: s.heroSecondaryLink,
    heroImage: null,
    facebookPageUrl: s.facebookPageUrl,
    instagramUrl: s.instagramUrl,
    showYouthPrinces: s.showYouthPrinces,
  };
}

export function useWebsitePages() {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'pages'],
    queryFn: async () => required((await api.GET('/api/v1/admin/website/pages')).data),
  });
}

export function useWebsitePage(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'page', id],
    enabled: id !== null,
    queryFn: async () => required((await api.GET('/api/v1/admin/website/pages/{id}', { params: { path: { id: id! } } })).data),
  });
}

export function useCommittees() {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'committees'],
    queryFn: async () => required((await api.GET('/api/v1/admin/website/committees')).data),
  });
}

export function usePrinces(kind: PrinceKind) {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'princes', kind],
    queryFn: async () => required((await api.GET('/api/v1/admin/website/princes', { params: { query: { kind } } })).data),
  });
}

export function useAwards(type: AwardType | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['website', 'awards', type],
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/website/awards', { params: { query: type ? { type } : {} } })).data),
  });
}
