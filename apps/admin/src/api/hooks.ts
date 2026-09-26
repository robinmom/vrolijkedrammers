import type { components } from '@drammers/api-client';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useApi } from './ApiContext';

export type Schemas = components['schemas'];
export type Me = Schemas['MeResponse'];
export type UserSummary = Schemas['UserSummaryResponse'];
export type Role = Schemas['RoleResponse'];
export type AuditEntry = Schemas['AuditLogEntryResponse'];
export type CarnivalYear = Schemas['AdminCarnivalYearResponse'];
export type RoleAssignment = Schemas['RoleAssignmentRequest'];

/** openapi-fetch geeft `data | undefined`; fouten gooit de middleware al als ApiError. */
function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

export function useMe() {
  const api = useApi();
  return useQuery({ queryKey: ['me'], queryFn: async () => required((await api.GET('/api/v1/me')).data) });
}

export function useDashboard(enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['dashboard'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/dashboard')).data),
  });
}

export function useUsers(search: string, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['users', search, page],
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/users', { params: { query: { search, page, pageSize: 25 } } })).data),
  });
}

export function useUser(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['user', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/users/{id}', { params: { path: { id } } })).data),
  });
}

export function useUserRoles(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['user-roles', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/users/{id}/roles', { params: { path: { id } } })).data),
  });
}

export function useRoles() {
  const api = useApi();
  return useQuery({ queryKey: ['roles'], queryFn: async () => required((await api.GET('/api/v1/admin/roles')).data) });
}

export function usePermissions() {
  const api = useApi();
  return useQuery({
    queryKey: ['permissions'],
    queryFn: async () => required((await api.GET('/api/v1/admin/permissions')).data),
  });
}

export interface AuditFilters {
  action: string;
  entityType: string;
  from: string;
  to: string;
}

export function useAuditLog(filters: AuditFilters, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['audit-log', filters, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/audit-log', {
            params: {
              query: {
                action: filters.action || undefined,
                entityType: filters.entityType || undefined,
                from: filters.from || undefined,
                to: filters.to || undefined,
                page,
                pageSize: 25,
              },
            },
          })
        ).data,
      ),
  });
}

export function useAppConfigSettings() {
  const api = useApi();
  return useQuery({
    queryKey: ['app-config'],
    queryFn: async () => required((await api.GET('/api/v1/admin/config/app-config')).data),
  });
}

export function useFeatureFlags(enabled = true) {
  const api = useApi();
  return useQuery({
    queryKey: ['feature-flags'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/config/feature-flags')).data),
  });
}

export function useRetention() {
  const api = useApi();
  return useQuery({
    queryKey: ['retention'],
    queryFn: async () => required((await api.GET('/api/v1/admin/config/retention')).data),
  });
}

export function useCarnivalYears() {
  const api = useApi();
  return useQuery({
    queryKey: ['carnival-years'],
    queryFn: async () => required((await api.GET('/api/v1/admin/carnival-years')).data),
  });
}

/** Mutatie die na succes de genoemde queries (en de auditlog) ververst. */
export function useApiMutation<TVariables>(fn: (variables: TVariables) => Promise<unknown>, invalidate: string[][]) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await Promise.all([...invalidate, ['audit-log']].map((queryKey) => client.invalidateQueries({ queryKey })));
    },
  });
}

export type AdminEvent = Schemas['AdminEventResponse'];
export type EventRequest = Schemas['EventRequest'];
export type NewsRequest = Schemas['NewsRequest'];
export type AlbumRequest = Schemas['AlbumRequest'];

export function useEventCategories() {
  const api = useApi();
  return useQuery({ queryKey: ['event-categories'], queryFn: async () => required((await api.GET('/api/v1/event-categories')).data) });
}

export function useAdminEvents(includePast: boolean, enabled = true) {
  const api = useApi();
  return useQuery({
    queryKey: ['admin-events', includePast],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/events', { params: { query: { includePast } } })).data),
  });
}

export function useAdminEvent(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['admin-event', id],
    enabled: id !== null,
    queryFn: async () => required((await api.GET('/api/v1/admin/events/{id}', { params: { path: { id: id! } } })).data),
  });
}

export function useAdminNews(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: ['admin-news'], enabled, queryFn: async () => required((await api.GET('/api/v1/admin/news')).data) });
}

export function useAdminNewsItem(id: string | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['admin-news-item', id],
    enabled: id !== null,
    queryFn: async () => required((await api.GET('/api/v1/admin/news/{id}', { params: { path: { id: id! } } })).data),
  });
}

export function useAdminAlbums() {
  const api = useApi();
  return useQuery({ queryKey: ['admin-albums'], queryFn: async () => required((await api.GET('/api/v1/admin/photo-albums')).data) });
}

export function useAdminAlbum(id: string | null, pollWhilePending = false) {
  const api = useApi();
  return useQuery({
    queryKey: ['admin-album', id],
    enabled: id !== null,
    // Foto's worden op de achtergrond verwerkt: ververs zolang er nog foto's in verwerking zijn.
    refetchInterval: (query) =>
      pollWhilePending && query.state.data?.photos.some((p) => p.processingStatus === 'Pending') ? 3000 : false,
    queryFn: async () => required((await api.GET('/api/v1/admin/photo-albums/{id}', { params: { path: { id: id! } } })).data),
  });
}

// --- Leden en ledensync (fase 8) ---

export type MemberSummary = Schemas['MemberSummaryResponse'];
export type MemberDetail = Schemas['MemberDetailResponse'];
export type MembershipStatus = Schemas['MembershipStatus'];
export type MemberSyncState = Schemas['MemberSyncState'];
export type SyncJob = Schemas['SyncJobResponse'];
export type SyncJobItem = Schemas['SyncJobItemResponse'];
export type SyncItemAction = Schemas['SyncItemAction'];
export type SyncConflict = Schemas['SyncConflictResponse'];
export type MemberFieldMapping = Schemas['MemberFieldMapping'];
export type MemberPurgeResult = Schemas['MemberPurgeResponse'];

export interface MemberFilters {
  search: string;
  status: MembershipStatus | '';
  syncState: MemberSyncState | '';
}

export function useMembers(filters: MemberFilters, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['members', filters, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/members', {
            params: {
              query: {
                search: filters.search || undefined,
                status: filters.status || undefined,
                syncState: filters.syncState || undefined,
                page,
                pageSize: 50,
              },
            },
          })
        ).data,
      ),
  });
}

export function useMember(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['member', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/members/{id}', { params: { path: { id } } })).data),
  });
}

const isBusy = (status: string | undefined) => status === 'Queued' || status === 'Running';

/** Syncruns; ververst elke 3 seconden zolang er een run in de wachtrij staat of loopt. */
export function useSyncJobs(page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['sync-jobs', page],
    queryFn: async () =>
      required((await api.GET('/api/v1/admin/sync-jobs', { params: { query: { page, pageSize: 20 } } })).data),
    refetchInterval: (query) => (query.state.data?.items.some((j) => isBusy(j.status)) ? 3000 : false),
  });
}

export function useSyncJob(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['sync-job', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/sync-jobs/{id}', { params: { path: { id } } })).data),
    refetchInterval: (query) => (isBusy(query.state.data?.status) ? 3000 : false),
  });
}

export function useSyncJobItems(id: string, action: SyncItemAction | '', page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['sync-job-items', id, action, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/sync-jobs/{id}/items', {
            params: { path: { id }, query: { action: action || undefined, page, pageSize: 50 } },
          })
        ).data,
      ),
  });
}

export function useSyncConflicts(enabled = true) {
  const api = useApi();
  return useQuery({
    queryKey: ['sync-conflicts'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/sync-conflicts')).data),
  });
}

export function useMemberMapping(enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['member-mapping'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/config/member-mapping')).data),
  });
}

// --- Groepen en rapportage (fase 8c) ---

export type GroupSummary = Schemas['GroupSummaryResponse'];
export type GroupDetail = Schemas['GroupDetailResponse'];
export type GroupType = Schemas['GroupType'];
export type GroupFunction = Schemas['GroupFunction'];
export type MemberReport = Schemas['MemberReportResponse'];

/** Groepen als keuzelijst bij publiceren (alleen naam); ook voor redacteuren zonder toegang tot ledengegevens. */
export function useAudienceGroups() {
  const api = useApi();
  return useQuery({
    queryKey: ['audience-groups'],
    queryFn: async () => required((await api.GET('/api/v1/admin/content-audiences/groups')).data),
  });
}

export function useGroups() {
  const api = useApi();
  return useQuery({ queryKey: ['groups'], queryFn: async () => required((await api.GET('/api/v1/admin/groups')).data) });
}

export function useGroup(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['group', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/groups/{id}', { params: { path: { id } } })).data),
  });
}

export function useMemberReport() {
  const api = useApi();
  return useQuery({ queryKey: ['member-report'], queryFn: async () => required((await api.GET('/api/v1/admin/reports/members')).data) });
}

export type MemberSummaryCounts = Schemas['MemberSummaryCountsResponse'];

export function useMemberSummary(enabled = true) {
  const api = useApi();
  return useQuery({ queryKey: ['members', 'summary'], enabled, queryFn: async () => required((await api.GET('/api/v1/admin/members/summary')).data) });
}

/** Auditregels van één lid (nieuwste eerst), voor de historie op het lid-detail. */
export function useMemberHistory(memberId: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['audit-log', 'member', memberId],
    enabled,
    queryFn: async () =>
      required(
        (await api.GET('/api/v1/admin/audit-log', { params: { query: { entityType: 'Member', entityId: memberId, page: 1, pageSize: 5 } } }))
          .data,
      ),
  });
}

/** Het actieve carnavalsjaar (publiek endpoint), voor de countdown op het dashboard. */
export function useCurrentCarnivalYear() {
  const api = useApi();
  return useQuery({
    queryKey: ['carnival-year', 'current'],
    retry: false,
    queryFn: async () => required((await api.GET('/api/v1/carnival-years/current')).data),
  });
}
