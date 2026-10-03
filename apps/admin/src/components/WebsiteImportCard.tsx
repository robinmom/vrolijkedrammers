import { useQuery } from '@tanstack/react-query';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { formatDateTime } from '../format';
import { ProblemAlert } from './ProblemAlert';

type ImportKind = NonNullable<Schemas['WebsiteImportKind']>;

const kindLabels: Record<ImportKind, string> = {
  Post: 'Nieuwsberichten',
  Page: "Pagina's",
  Gallery: 'Galerijen',
  GalleryPhoto: "Foto's in galerijen",
  Prince: 'Prinsen',
  YouthPrince: 'Jeugdprinsen',
  Award: 'Onderscheidingen',
  Kader: 'Kader',
  CarnivalPage: "Pagina's onder Carnaval",
};

/**
 * Oude website overzetten (fase 21e): berichten, pagina's, foto's, prinsen, onderscheidingen en kader van de
 * WordPress-site. Het werk gebeurt op de achtergrond; deze kaart toont de voortgang en ververst zichzelf.
 */
export function WebsiteImportCard() {
  const api = useApi();
  const status = useQuery({
    queryKey: ['website', 'import'],
    queryFn: async () => (await api.GET('/api/v1/admin/website/import')).data,
    refetchInterval: (query) => (query.state.data?.running ? 5000 : false),
  });
  const start = useApiMutation(() => api.POST('/api/v1/admin/website/import'), [['website']]);
  const retry = useApiMutation(() => api.POST('/api/v1/admin/website/import/retry'), [['website']]);
  const data = status.data;
  const counts = (data?.counts ?? []).filter((c) => c.pending + c.done + c.skipped + c.failed > 0);
  const total = counts.reduce((sum, c) => sum + c.pending + c.done + c.skipped + c.failed, 0);
  const pending = counts.reduce((sum, c) => sum + c.pending, 0);
  const failed = data?.failures.length ?? 0;

  return (
    <section className="card" aria-labelledby="import-titel">
      <h2 id="import-titel">Oude website overzetten</h2>
      <p className="muted">
        Haalt de berichten, pagina's, foto's, prinsen, onderscheidingen en het kader op van de huidige WordPress-site en
        zet ze in de nieuwe website. Wat al is overgezet, wordt niet nog een keer gedaan. Oude adressen sturen daarna
        door naar de nieuwe pagina.
      </p>
      <ProblemAlert error={status.error ?? start.error ?? retry.error} />
      {data?.running ? (
        <p role="status">
          <span className="badge info">Bezig</span> Nog {pending} van {total} onderdelen. Je kunt deze pagina sluiten;
          het gaat op de achtergrond door.
        </p>
      ) : total > 0 ? (
        <p role="status">
          {pending === 0 ? <span className="badge ok">Klaar</span> : <span className="badge warn">Gepauzeerd</span>}{' '}
          Laatste activiteit: {formatDateTime(data?.lastActivity)}
        </p>
      ) : null}
      {counts.length > 0 ? (
        <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Voortgang">
          <table className="table">
            <caption className="visually-hidden">Voortgang per soort</caption>
            <thead>
              <tr>
                <th scope="col">Soort</th>
                <th scope="col">Overgezet</th>
                <th scope="col">Overgeslagen</th>
                <th scope="col">Nog te doen</th>
                <th scope="col">Mislukt</th>
              </tr>
            </thead>
            <tbody>
              {counts.map((c) => (
                <tr key={c.kind}>
                  <td>{kindLabels[c.kind as ImportKind] ?? c.kind}</td>
                  <td>{c.done}</td>
                  <td>{c.skipped}</td>
                  <td>{c.pending}</td>
                  <td>{c.failed > 0 ? <span className="badge error">{c.failed}</span> : 0}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      {failed > 0 ? (
        <details>
          <summary>Wat is er mislukt? ({failed})</summary>
          <ul>
            {data!.failures.map((f) => (
              <li key={f.id}>
                <strong>{f.title ?? f.sourceUrl}</strong>: {f.error}
              </li>
            ))}
          </ul>
        </details>
      ) : null}
      <div className="actions">
        {failed > 0 ? (
          <button
            type="button"
            className="button secondary"
            disabled={retry.isPending || data?.running}
            onClick={() => retry.mutate(undefined)}
          >
            Mislukte opnieuw proberen
          </button>
        ) : null}
        <button
          type="button"
          className="button"
          disabled={start.isPending || data?.running}
          onClick={() => start.mutate(undefined)}
        >
          {total === 0 ? 'Start overzetten' : pending > 0 ? 'Hervatten' : 'Opnieuw controleren'}
        </button>
      </div>
    </section>
  );
}
