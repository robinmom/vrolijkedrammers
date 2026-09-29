import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import type { components } from '@drammers/api-client';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useArrivalTimes } from '../api/hooks';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, formatDateTime } from '../format';

type ImportPreview = components['schemas']['ArrivalImportPreview'];
type Changed = components['schemas']['ArrivalChangedResponse'];

/** "10:30:00" → "10:30" (invoer type="time" en weergave). */
const hhmm = (value: string | null | undefined) => (value ? value.slice(0, 5) : '');

/**
 * Optocht → Aanrijtijden (fase 16): alleen wagens krijgen een tijd bij de meldplek. Genereren op startnummervolgorde,
 * per groep aanpassen of de websitetabel (Stnr. + tijd) inlezen, en publiceren: groepen krijgen een melding en de lijst
 * wordt openbaar (app en webpagina).
 */
export function ArrivalTimesPage() {
  const api = useApi();
  const list = useArrivalTimes();
  const [message, setMessage] = useState<string | null>(null);
  const [location, setLocation] = useState('');
  const [first, setFirst] = useState('10:30');
  const [interval, setIntervalMinutes] = useState('4');
  const [onlyEmpty, setOnlyEmpty] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const invalidate = [['arrival-times'], ['parade-registration']];
  const d = list.data;

  useEffect(() => setLocation(d?.location ?? ''), [d?.location]);

  const saveLocation = useApiMutation(
    async () =>
      (await api.PUT('/api/v1/admin/parade/arrival-times/location', { body: { location: location.trim() || null } }))
        .data,
    invalidate,
  );
  const generate = useApiMutation(
    async () =>
      (
        await api.POST('/api/v1/admin/parade/arrival-times/generate', {
          body: { first: `${first}:00`, intervalMinutes: Number(interval) || 4, onlyEmpty },
        })
      ).data,
    invalidate,
  );
  const setTime = useApiMutation(
    async ({ id, time }: { id: string; time: string | null }) =>
      (
        await api.PUT('/api/v1/admin/parade/arrival-times/{registrationId}', {
          params: { path: { registrationId: id } },
          body: { arrivalTime: time ? `${time}:00` : null },
        })
      ).data,
    invalidate,
  );
  const publish = useApiMutation(
    async () => (await api.POST('/api/v1/admin/parade/arrival-times/publish')).data,
    invalidate,
  );

  function submitGenerate(event: FormEvent) {
    event.preventDefault();
    generate.mutate(undefined, {
      onSuccess: (data) => setMessage(`${(data as Changed | undefined)?.changed ?? 0} aanrijtijd(en) ingevuld.`),
    });
  }

  const withTime = d?.rows.filter((r) => r.arrivalTime).length ?? 0;
  const withoutNumber = d?.rows.filter((r) => r.startNumber === null || r.startNumber === undefined).length ?? 0;

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Aanrijtijden</h1>
          <p className="page-subtitle">
            Alleen wagens (getrokken en zelfrijdend, ook jeugd) krijgen een aanrijtijd bij de meldplek. Na publiceren
            krijgt elke groep een melding en staat de lijst in de app en op de openbare webpagina.
          </p>
        </div>
        <div className="actions">
          <ImportArrivals onDone={setMessage} />
          <button type="button" className="button" disabled={!withTime} onClick={() => setPublishing(true)}>
            {d?.publishedAt ? 'Opnieuw publiceren' : 'Publiceren'}
            {withTime ? ` (${withTime})` : ''}
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={list.error ?? generate.error ?? setTime.error ?? publish.error ?? saveLocation.error} />
      {d ? (
        <>
          <section className="kpis" aria-label="Kerncijfers">
            <div className="kpi">
              <span className="kpi-label">Wagens</span>
              <span className="kpi-value">{d.rows.length}</span>
              <span className="kpi-hint">
                {withoutNumber ? `${withoutNumber} zonder startnummer` : 'alle met startnummer'}
              </span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Met aanrijtijd</span>
              <span className="kpi-value">{withTime}</span>
              <span className="kpi-hint">optocht {formatDate(d.paradeDate)}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Status</span>
              <span className="kpi-value">{d.publishedAt ? 'Gepubliceerd' : 'Concept'}</span>
              <span className="kpi-hint">
                {d.publishedAt ? (
                  <>
                    {formatDateTime(d.publishedAt)} ·{' '}
                    <a href="/aanrijtijden/" target="_blank" rel="noreferrer">
                      openbare pagina
                    </a>
                  </>
                ) : (
                  'nog niet zichtbaar voor groepen'
                )}
              </span>
            </div>
          </section>

          <div className="columns">
            <section className="card" aria-labelledby="genereren">
              <h2 id="genereren">Genereren</h2>
              <p className="card-hint">
                Op startnummervolgorde: de eerste wagen op de begintijd, daarna steeds de gekozen minuten later.
              </p>
              <form onSubmit={submitGenerate}>
                <Field
                  label="Eerste aanrijtijd"
                  type="time"
                  value={first}
                  onChange={(e) => setFirst(e.target.value)}
                  required
                />
                <Field
                  label="Minuten per wagen"
                  type="number"
                  min={1}
                  max={60}
                  value={interval}
                  onChange={(e) => setIntervalMinutes(e.target.value)}
                  required
                />
                <Checkbox
                  label="Alleen lege tijden invullen"
                  checked={onlyEmpty}
                  onChange={(e) => setOnlyEmpty(e.target.checked)}
                />
                <button type="submit" className="button secondary" disabled={generate.isPending}>
                  Tijden genereren
                </button>
              </form>
            </section>
            <section className="card" aria-labelledby="meldplek">
              <h2 id="meldplek">Meldplek</h2>
              <p className="card-hint">Waar de wagens zich melden; ook de kolomkop van de openbare lijst.</p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  saveLocation.mutate(undefined, { onSuccess: () => setMessage('Meldplek opgeslagen.') });
                }}
              >
                <Field
                  label="Meldplek"
                  value={location}
                  maxLength={100}
                  onChange={(e) => setLocation(e.target.value)}
                />
                <button type="submit" className="button secondary" disabled={saveLocation.isPending}>
                  Opslaan
                </button>
              </form>
            </section>
          </div>

          <section className="card" aria-labelledby="wagens">
            <h2 id="wagens">Wagens</h2>
            {d.rows.length === 0 ? (
              <p className="muted">Nog geen goedgekeurde wagens in de optocht.</p>
            ) : (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Aanrijtijden per wagen">
                <table className="table">
                  <caption className="visually-hidden">Aanrijtijden per wagen</caption>
                  <thead>
                    <tr>
                      <th scope="col">Stnr.</th>
                      <th scope="col">Categorie</th>
                      <th scope="col">Naam</th>
                      <th scope="col">{d.location ?? 'Aanrijtijd'}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {d.rows.map((r) => (
                      <tr key={`${r.id}-${r.arrivalTime ?? ''}`}>
                        <td>{r.startNumber ?? '–'}</td>
                        <td>{r.category ?? '–'}</td>
                        <td>{r.groupName ?? '–'}</td>
                        <td>
                          <input
                            type="time"
                            aria-label={`Aanrijtijd ${r.groupName ?? ''}`}
                            defaultValue={hhmm(r.arrivalTime)}
                            onBlur={(e) => {
                              const value = e.target.value || null;
                              if (value !== (hhmm(r.arrivalTime) || null)) setTime.mutate({ id: r.id, time: value });
                            }}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {d.publishedAt ? (
              <p className="muted small-text">
                Al gepubliceerd: een groep waarvan de tijd wijzigt, krijgt direct een melding.
              </p>
            ) : null}
          </section>
        </>
      ) : null}
      <ConfirmDialog
        open={publishing}
        title="Aanrijtijden publiceren"
        message={`${withTime} groep(en) krijgen een melding met hun aanrijtijd${d?.location ? ` bij ${d.location}` : ''}. Daarna staat de lijst in de app en op de openbare webpagina.`}
        confirmLabel="Publiceren"
        busy={publish.isPending}
        onCancel={() => setPublishing(false)}
        onConfirm={() =>
          publish.mutate(undefined, {
            onSettled: () => setPublishing(false),
            onSuccess: (data) =>
              setMessage(`Gepubliceerd: ${(data as Changed | undefined)?.changed ?? 0} groep(en) krijgen een melding.`),
          })
        }
      />
    </>
  );
}

/** De tabel zoals op de website (Stnr., Categorie, Naam, meldplek) inlezen; eerst een voorbeeld, alles of niets. */
function ImportArrivals({ onDone }: { onDone: (message: string) => void }) {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function load(selected: File) {
    setFile(selected);
    setPreview(null);
    setError(null);
    setBusy(true);
    const form = new FormData();
    form.append('file', selected);
    try {
      setPreview(await uploadJson<ImportPreview>(auth, '/api/v1/admin/parade/arrival-times/import/preview', form));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  async function confirm() {
    if (!file) return;
    setBusy(true);
    const form = new FormData();
    form.append('file', file);
    try {
      const result = await uploadJson<Changed>(auth, '/api/v1/admin/parade/arrival-times/import', form);
      await queryClient.invalidateQueries({ queryKey: ['arrival-times'] });
      setOpen(false);
      onDone(`${result.changed} aanrijtijd(en) ingelezen.`);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button
        type="button"
        className="button secondary"
        onClick={() => {
          setFile(null);
          setPreview(null);
          setError(null);
          setOpen(true);
        }}
      >
        Importeren (Excel)
      </button>
      <Dialog open={open} title="Aanrijtijden importeren" onClose={() => setOpen(false)}>
        <p>
          Een Excel zoals de tabel op de website: kolom <strong>Stnr.</strong> en een tijdkolom (bijv. &quot;10:30
          uur&quot;). Heet de tijdkolom naar de meldplek (zoals &quot;Rotonde Holthuizen&quot;), dan wordt dat de
          meldplek.
        </p>
        <div className="field">
          <label htmlFor="aanrij-bestand">Excel-bestand (.xlsx)</label>
          <input
            id="aanrij-bestand"
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={(e) => {
              const selected = e.target.files?.[0];
              if (selected) void load(selected);
            }}
          />
        </div>
        <ProblemAlert error={error} />
        {preview ? (
          <>
            {preview.location ? <p>Meldplek: {preview.location}</p> : null}
            {preview.errors.length ? (
              <div className="alert alert-error" role="alert">
                <strong>
                  {preview.errors.length} fout{preview.errors.length === 1 ? '' : 'en'}: er wordt niets ingelezen.
                </strong>
                <ul>
                  {preview.errors.map((e, i) => (
                    <li key={i}>
                      Regel {e.row}: {e.message}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {preview.changes.length ? (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Wijzigingen">
                <table className="table compact">
                  <caption>{preview.changes.length} wijziging(en)</caption>
                  <thead>
                    <tr>
                      <th scope="col">Stnr.</th>
                      <th scope="col">Groep</th>
                      <th scope="col">Oud</th>
                      <th scope="col">Nieuw</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.changes.map((c) => (
                      <tr key={c.id}>
                        <td>{c.startNumber}</td>
                        <td>{c.groupName ?? '–'}</td>
                        <td>{hhmm(c.oldTime) || '–'}</td>
                        <td>{hhmm(c.newTime) || 'leeg'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : preview.errors.length === 0 ? (
              <p role="status">Geen wijzigingen.</p>
            ) : null}
          </>
        ) : null}
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setOpen(false)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button"
            disabled={busy || !preview || preview.errors.length > 0 || preview.changes.length === 0}
            onClick={() => void confirm()}
          >
            Inlezen
          </button>
        </div>
      </Dialog>
    </>
  );
}
