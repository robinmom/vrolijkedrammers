import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { describeProblem } from '../api/errors';
import { useApiMutation, type Schemas } from '../api/hooks';
import { uploadWithProgress } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { Dialog } from '../components/Dialog';
import { Checkbox } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, formatDateTime } from '../format';

type Kind = Schemas['ResultsExportKind'];

const points = (n: number) => n.toLocaleString('nl-NL', { maximumFractionDigits: 1 });

/**
 * Uitslag van de optocht (fase 22c, alleen de uitslagcommissie): per categorie zodra alle juryleden hebben ingediend,
 * Excel (uitslag per categorie en zaallijst) en publiceren — pas na de prijsuitreiking.
 */
export function ResultsPage() {
  const api = useApi();
  const results = useQuery({
    queryKey: ['results'],
    queryFn: async () => (await api.GET('/api/v1/admin/results')).data,
  });
  const [selected, setSelected] = useState<number | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [held, setHeld] = useState(false);
  const [busy, setBusy] = useState<Kind | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [message, setMessage] = useState<string | null>(null);
  const data = results.data;
  const auth = useAuth();
  const client = useQueryClient();
  const [uploading, setUploading] = useState<string | null>(null);

  // Fase 22d: foto's bij een inzending, ook achteraf; één voor één, net als bij Foto's.
  async function addPhotos(registrationId: string, groupName: string, files: File[]) {
    if (files.length === 0) return;
    setError(null);
    setUploading(registrationId);
    try {
      for (const file of files) {
        const form = new FormData();
        form.append('files', file);
        await uploadWithProgress(auth, `/api/v1/admin/results/entries/${registrationId}/photos`, form, () => undefined);
      }
      setMessage(`${files.length === 1 ? '1 foto' : `${files.length} foto's`} toegevoegd bij ${groupName}.`);
      await client.invalidateQueries({ queryKey: ['results'] });
    } catch (e) {
      setError(new Error(describeProblem(e)));
    } finally {
      setUploading(null);
    }
  }
  const publish = useApiMutation(
    () => api.POST('/api/v1/admin/results/publish', { body: { paradeId: data!.paradeId, prizeCeremonyHeld: true } }),
    [['results']],
  );

  const categories = data?.categories ?? [];
  const current = categories.find((c) => c.categoryId === selected) ?? categories[0];
  const allReady = categories.length > 0 && categories.every((c) => c.ready);

  async function download(kind: Kind) {
    setError(null);
    setBusy(kind);
    try {
      const { data: blob, response } = await api.GET('/api/v1/admin/results/export', {
        params: { query: { kind } },
        parseAs: 'blob',
      });
      if (blob) {
        const disposition = response.headers.get('content-disposition') ?? '';
        const name =
          /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1] ?? /filename="?([^";]+)"?/i.exec(disposition)?.[1];
        const url = URL.createObjectURL(blob as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name ? decodeURIComponent(name) : `${kind === 'Uitslag' ? 'uitslag' : 'zaallijst'}.xlsx`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (e) {
      setError(e);
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Uitslag</h1>
          <p className="page-subtitle">
            {data ? `${data.paradeName} · ${formatDate(data.paradeDate)} · ` : ''}
            alleen zichtbaar voor de uitslagcommissie ·{' '}
            {data?.publishedAt ? `gepubliceerd op ${formatDateTime(data.publishedAt)}` : 'nog niet gepubliceerd'}
          </p>
        </div>
        <div className="actions">
          <button
            type="button"
            className="button secondary"
            disabled={busy !== null}
            onClick={() => void download('Uitslag')}
          >
            {busy === 'Uitslag' ? 'Exporteren…' : 'Excel: uitslag per categorie'}
          </button>
          <button
            type="button"
            className="button secondary"
            disabled={busy !== null}
            onClick={() => void download('Zaallijst')}
          >
            {busy === 'Zaallijst' ? 'Exporteren…' : 'Excel: zaallijst'}
          </button>
          {data && !data.publishedAt ? (
            <button
              type="button"
              className="button"
              disabled={!allReady}
              title={allReady ? undefined : 'Kan pas als alle juryleden van alle categorieën hebben ingediend.'}
              onClick={() => {
                setHeld(false);
                setPublishing(true);
              }}
            >
              Nu publiceren
            </button>
          ) : null}
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={results.error ?? error} />

      {data && categories.length === 0 ? (
        <p className="muted">Nog geen beoordeelde categorieën met inzendingen.</p>
      ) : null}
      {categories.length > 0 ? (
        <div className="chips" role="tablist" aria-label="Categorieën">
          {categories.map((c) => (
            <button
              key={c.categoryId}
              type="button"
              role="tab"
              aria-selected={c.categoryId === current?.categoryId}
              className={c.categoryId === current?.categoryId ? 'chip active' : 'chip'}
              onClick={() => setSelected(c.categoryId)}
            >
              {c.name}{' '}
              <span className="chip-count">
                {c.submitted}/{c.jurors}
                {c.ready ? ' ✓' : ''}
              </span>
            </button>
          ))}
        </div>
      ) : null}

      {current ? (
        <section className="card" aria-labelledby="uitslag-categorie" role="tabpanel">
          <div className="card-header">
            <h2 id="uitslag-categorie">{current.name}</h2>
            <p className="muted">
              {current.jurors} juryleden · weging org {current.weightOriginality}x · carn {current.weightCarnivalesque}x
              · kwal {current.weightQuality}x · alg {current.weightOverall}x · max {current.maxPoints} punten
            </p>
          </div>
          {!current.ready ? (
            <div className="alert alert-warning">
              {current.jurors === 0
                ? 'Deze categorie heeft nog geen juryleden.'
                : `${current.submitted} van ${current.jurors} juryleden hebben ingediend. De uitslag van deze categorie verschijnt zodra alle juryleden hebben ingediend.`}
            </div>
          ) : (
            <div className="table-scroll" tabIndex={0} role="region" aria-label={`Uitslag ${current.name}`}>
              <table className="table">
                <caption className="visually-hidden">Uitslag {current.name}</caption>
                <thead>
                  <tr>
                    <th scope="col">Plaats</th>
                    <th scope="col">Nr.</th>
                    <th scope="col">Groep en motto</th>
                    <th scope="col" className="num">
                      Org.
                    </th>
                    <th scope="col" className="num">
                      Carn.
                    </th>
                    <th scope="col" className="num">
                      Kwal.
                    </th>
                    <th scope="col" className="num">
                      Alg.
                    </th>
                    <th scope="col" className="num">
                      Totaal
                    </th>
                    <th scope="col">Foto&apos;s</th>
                  </tr>
                </thead>
                <tbody>
                  {current.rows.map((r) => (
                    <tr key={r.registrationId}>
                      <td>{r.place}</td>
                      <td>{r.startNumber ?? '–'}</td>
                      <td>
                        <strong>{r.groupName}</strong>
                        {r.motto ? <span className="muted"> {r.motto}</span> : null}
                      </td>
                      <td className="num">{points(r.originality)}</td>
                      <td className="num">{points(r.carnivalesque)}</td>
                      <td className="num">{points(r.quality)}</td>
                      <td className="num">{points(r.overall)}</td>
                      <td className="num">
                        <strong>{points(r.total)}</strong>
                      </td>
                      <td>
                        <label className="button secondary small file-button">
                          {uploading === r.registrationId
                            ? 'Uploaden…'
                            : r.photoCount > 0
                              ? `${r.photoCount} · toevoegen`
                              : 'Toevoegen'}
                          <span className="visually-hidden"> foto&apos;s bij {r.groupName}</span>
                          <input
                            type="file"
                            accept="image/jpeg,image/png,image/heic,image/webp"
                            multiple
                            className="visually-hidden"
                            disabled={uploading !== null}
                            onChange={(e) => {
                              // Eerst kopiëren: na het leegmaken van de invoer is de FileList ook leeg.
                              void addPhotos(r.registrationId, r.groupName, Array.from(e.target.files ?? []));
                              e.target.value = '';
                            }}
                          />
                        </label>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : null}

      <Dialog open={publishing} title="Is de prijsuitreiking al geweest?" onClose={() => setPublishing(false)}>
        <p>
          Na publiceren staat de uitslag op de website en in de app. Dat kan niet worden teruggedraaid. De secretaris en
          de voorzitter krijgen hier een e-mail van.
        </p>
        <Checkbox
          label="Ja, de prijsuitreiking is geweest"
          checked={held}
          onChange={(e) => setHeld(e.target.checked)}
        />
        <ProblemAlert error={publish.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setPublishing(false)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button danger"
            disabled={!held || publish.isPending}
            onClick={() =>
              publish.mutate(undefined, {
                onSuccess: () => {
                  setPublishing(false);
                  setMessage('De uitslag is gepubliceerd: hij staat nu op de website en in de app.');
                },
              })
            }
          >
            Uitslag publiceren
          </button>
        </div>
      </Dialog>
    </>
  );
}
