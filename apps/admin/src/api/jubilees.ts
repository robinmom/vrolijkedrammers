import { useQuery } from '@tanstack/react-query';
import { useApi } from './ApiContext';
import type { Schemas } from './hooks';

export type JubileeReport = Schemas['JubileeReport'];
export type Jubilarian = Schemas['Jubilarian'];

/** openapi-fetch geeft `data | undefined`; fouten gooit de middleware al als ApiError. */
function required<T>(data: T | undefined): T {
  if (data === undefined) {
    throw new Error('Leeg antwoord van de API');
  }
  return data;
}

/** Jubilarissen (fase 20); zonder carnavalsjaar het actieve. */
export function useJubilees(carnivalYearId: number | null) {
  const api = useApi();
  return useQuery({
    queryKey: ['jubilees', carnivalYearId],
    queryFn: async () =>
      required(
        (
          await api.GET('/api/v1/admin/jubilees', {
            params: { query: { carnivalYearId: carnivalYearId ?? undefined } },
          })
        ).data,
      ),
  });
}
