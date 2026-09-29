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

export function useExcludedMembers(enabled = true) {
  const api = useApi();
  return useQuery({
    queryKey: ['excluded-members'],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/members/excluded')).data),
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

// ----- Fase 9: accountverzoeken, provisioning en apparaten -------------------------------------------------------

export type AccountRequest = Schemas['AccountRequestResponse'];
export type AccountRequestStatus = Schemas['AccountRequestStatus'];
export type Provisioning = Schemas['ProvisioningResponse'];
export type Device = Schemas['DeviceResponse'];

export function useAccountRequests(status: AccountRequestStatus | '', page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['account-requests', status, page],
    queryFn: async () =>
      required(
        (await api.GET('/api/v1/admin/account-requests', { params: { query: { status: status || undefined, page, pageSize: 25 } } })).data,
      ),
  });
}

/** Openstaande (of mislukte) provisioning; ververst elke 5 s zolang er iets loopt. */
export function useProvisioning() {
  const api = useApi();
  return useQuery({
    queryKey: ['account-provisioning'],
    queryFn: async () => required((await api.GET('/api/v1/admin/account-provisioning')).data),
    refetchInterval: (query) => ((query.state.data ?? []).some((p) => !p.lastError) ? 5000 : false),
  });
}

export function useUserDevices(id: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['user-devices', id],
    enabled,
    queryFn: async () => required((await api.GET('/api/v1/admin/users/{id}/devices', { params: { path: { id } } })).data),
  });
}

// ----- Fase 10: pushmeldingen ----------------------------------------------------------------------------------

export type NotificationSummary = Schemas['NotificationSummaryResponse'];
export type NotificationDetail = Schemas['NotificationDetailResponse'];
export type NotificationAudience = Schemas['NotificationAudience'];
export type NotificationCategory = Schemas['NotificationCategory'];
export type CreateNotificationRequest = Schemas['CreateNotificationRequest'];
export type AudiencePreview = Schemas['AudiencePreview'];

export function useNotifications(page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['notifications', page],
    queryFn: async () => required((await api.GET('/api/v1/admin/notifications', { params: { query: { page } } })).data),
  });
}

export function useNotificationAudienceOptions() {
  const api = useApi();
  return useQuery({
    queryKey: ['notification-audience-options'],
    queryFn: async () => required((await api.GET('/api/v1/admin/notifications/audience-options')).data),
  });
}

export function useNotification(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['notification', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/notifications/{id}', { params: { path: { id } } })).data),
  });
}

// ----- Fase 11: optocht --------------------------------------------------------------------------------------------

export type AdminParade = Schemas['AdminParadeResponse'];
export type ParadeRequest = Schemas['ParadeRequest'];
export type ParadeCategory = Schemas['ParadeCategoryResponse'];
export type CategoryRequest = Schemas['CategoryRequest'];

export function useAdminParades() {
  const api = useApi();
  return useQuery({ queryKey: ['admin-parades'], queryFn: async () => required((await api.GET('/api/v1/admin/parades')).data) });
}

export function useParadeCategories() {
  const api = useApi();
  return useQuery({ queryKey: ['parade-categories'], queryFn: async () => required((await api.GET('/api/v1/admin/parade-categories')).data) });
}

export type ReviewSummary = Schemas['ReviewSummary'];
export type AdminRegistration = Schemas['AdminRegistrationResponse'];
export type ReviewAction = Schemas['ReviewAction'];

export type RegistrationFilter = {
  status?: string;
  search?: string;
  categoryId?: number;
  ageGroup?: string;
  hasVehicle?: boolean;
  missing?: string;
};
export type LineupSummary = Schemas['LineupSummary'];
export type PublishResult = Schemas['PublishResult'];
export type Composition = Schemas['Composition'];
export type TicketSummary = Schemas['TicketSummary'];
export type MemberAccess = Schemas['MemberAccess'];
export type AccessStats = Schemas['AccessStats'];
export type AccessDashboard = Schemas['AccessDashboard'];

export function useAccessDashboard(enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['access-stats', 'dashboard'],
    queryFn: async () => required((await api.GET('/api/v1/admin/access-stats/dashboard')).data),
    enabled,
    refetchInterval: 60_000,
  });
}

export function useAccessStats(key: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['access-stats', key],
    queryFn: async () => required((await api.GET('/api/v1/admin/access-stats', { params: { query: { key } } })).data),
    enabled: Boolean(key),
    refetchInterval: 60_000,
  });
}

export function useAccessOverview() {
  const api = useApi();
  return useQuery({ queryKey: ['access-stats', 'overview'], queryFn: async () => required((await api.GET('/api/v1/admin/access-stats/overview')).data) });
}
export type AccessResult = Schemas['AccessResult'];

export function useMemberAccess(memberId: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['member-access', memberId],
    queryFn: async () => required((await api.GET('/api/v1/admin/members/{memberId}/access', { params: { path: { memberId } } })).data),
    enabled,
    refetchInterval: 30_000,
  });
}

export function useAccessEvents() {
  const api = useApi();
  return useQuery({ queryKey: ['access-events'], queryFn: async () => required((await api.GET('/api/v1/admin/access-scans/events')).data) });
}

export function useAccessScans(key: string, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['access-scans', key, page],
    queryFn: async () => required((await api.GET('/api/v1/admin/access-scans', { params: { query: { key, page } } })).data),
    enabled: Boolean(key),
  });
}
export type TicketAction = Schemas['TicketAction'];

export function useTickets(search: string, status: string, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['tickets', search, status, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/tickets', {
            params: { query: { search: search || undefined, status: (status || undefined) as Schemas['TicketStatus'] | undefined, page } },
          })
        ).data,
      ),
  });
}
export type CompositionCard = Schemas['CompositionCard'];
export type StartNumberPreview = Schemas['StartNumberPreview'];
export type StartNumberMode = Schemas['StartNumberMode'];

export function useParadeComposition() {
  const api = useApi();
  return useQuery({
    queryKey: ['parade-composition'],
    queryFn: async () => required((await api.GET('/api/v1/admin/parade-composition')).data),
    retry: false,
  });
}

/** Overzicht voor de commissie: één ruime pagina (tot 100), sorteren gebeurt in de tabel. */
export function useParadeRegistrations(filter: RegistrationFilter, page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['parade-registrations', filter, page],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/parade-registrations', {
            params: {
              query: {
                status: (filter.status || undefined) as Schemas['RegistrationStatus'] | undefined,
                search: filter.search || undefined,
                categoryId: filter.categoryId,
                ageGroup: filter.ageGroup || undefined,
                hasVehicle: filter.hasVehicle,
                missing: filter.missing || undefined,
                page,
                pageSize: 100,
              },
            },
          })
        ).data,
      ),
  });
}

export function useParadeLineupSummary() {
  const api = useApi();
  return useQuery({
    queryKey: ['parade-registrations', 'summary'],
    queryFn: async () => required((await api.GET('/api/v1/admin/parade-registrations/summary')).data),
    retry: false,
  });
}

export function useParadeRegistration(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['parade-registration', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/parade-registrations/{id}', { params: { path: { id } } })).data),
  });
}

export type TestAccessStatus = Schemas['TestAccessStatus'];

export function useTestAccess(id: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['test-access', id],
    enabled,
    retry: false,
    queryFn: async () => required((await api.GET('/api/v1/admin/users/{id}/test-access', { params: { path: { id } } })).data),
  });
}

// ----- Fase 9b: aanmeldingen (lid worden) ------------------------------------------------------------------------

export type ApplicationSummary = Schemas['ApplicationSummaryResponse'];
export type ApplicationDetail = Schemas['ApplicationDetailResponse'];
export type ApplicationStatus = Schemas['ApplicationStatus'];

export function useApplications(status: ApplicationStatus | '', page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['applications', status, page],
    queryFn: async () =>
      required(
        (await api.GET('/api/v1/admin/membership-applications', { params: { query: { status: status || undefined, page, pageSize: 25 } } })).data,
      ),
  });
}

/** Ververst elke 5 s zolang de aanmelding wordt verwerkt (lid in e-Boekhouden, account, mail). */
export function useApplication(id: string) {
  const api = useApi();
  return useQuery({
    queryKey: ['application', id],
    queryFn: async () => required((await api.GET('/api/v1/admin/membership-applications/{id}', { params: { path: { id } } })).data),
    refetchInterval: (query) => (['Approved', 'Provisioning'].includes(query.state.data?.status ?? '') ? 5000 : false),
  });
}

// ----- Fase 9b-2: AVG-verzoeken ----------------------------------------------------------------------------------

export type PrivacyRequest = Schemas['PrivacyRequestResponse'];

export function usePrivacyRequests(page: number) {
  const api = useApi();
  return useQuery({
    queryKey: ['privacy-requests', page],
    queryFn: async () => required((await api.GET('/api/v1/admin/privacy-requests', { params: { query: { page, pageSize: 25 } } })).data),
  });
}
