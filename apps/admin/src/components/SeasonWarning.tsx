import { useQuery } from '@tanstack/react-query';
import { useApi } from '../api/ApiContext';
import { formatDate } from '../format';

/** De datum (yyyy-mm-dd) in Nederlandse tijd, voor een datum of een tijdstip. */
function localDay(value: string) {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
  return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Amsterdam' }).format(new Date(value));
}

/**
 * Fase 21g: nieuws en foto's verschijnen per carnavalsjaar. Valt de datum ná het laatste carnavalsjaar, dan is er nog geen
 * jaar om het onder te zetten; het komt dan bij het actieve jaar tot het nieuwe jaar is aangemaakt.
 */
export function SeasonWarning({ date }: { date: string | null | undefined }) {
  const api = useApi();
  const years = useQuery({
    queryKey: ['carnival-years', 'public'],
    queryFn: async () => (await api.GET('/api/v1/carnival-years')).data ?? [],
    staleTime: 5 * 60_000,
  });
  if (!date || !years.data) return null;
  const last = years.data.at(-1);
  if (last && localDay(date) <= last.endDate) return null;
  return (
    <div role="status" className="alert alert-warning">
      {last
        ? `Er is nog geen carnavalsjaar voor ${formatDate(localDay(date))}: het laatste jaar (${last.name}) loopt tot en met ${formatDate(last.endDate)}.`
        : 'Er is nog geen carnavalsjaar.'}{' '}
      Op de website en in de app komt dit bij het actieve jaar te staan tot het nieuwe carnavalsjaar is aangemaakt
      (Instellingen → Carnavalsjaren).
    </div>
  );
}
