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

export function useFeatureFlags() {
  const api = useApi();
  return useQuery({
    queryKey: ['feature-flags'],
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
