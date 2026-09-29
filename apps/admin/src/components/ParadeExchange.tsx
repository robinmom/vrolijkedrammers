import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { components } from '@drammers/api-client';
import { useApi } from '../api/ApiContext';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { Dialog } from './Dialog';
import { ProblemAlert } from './ProblemAlert';

type Preview = components['schemas']['StartNumberImportPreview'];
type Result = components['schemas']['StartNumberImportResult'];

/** Download van het deelnemersbestand (fase 12c): Excel in de kolommen van de optochtcommissie. */
export function ExportButton() {
  const api = useApi();
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function exportExcel() {
    setError(null);
    setBusy(true);
    try {
      const { data, response } = await api.GET('/api/v1/admin/parade/export', { parseAs: 'blob' });
      if (data) {
        const disposition = response.headers.get('content-disposition') ?? '';
        const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name ? decodeURIComponent(name) : 'Opgaven optocht.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button type="button" className="button secondary" disabled={busy} onClick={() => void exportExcel()}>
        {busy ? 'Exporteren…' : 'Exporteren (Excel)'}
      </button>
      {error ? <ProblemAlert error={error} /> : null}
    </>
  );
}

/**
 * Startnummers importeren (fase 12c): het geëxporteerde bestand met ingevulde startnummers uploaden, het voorbeeld
 * (oud → nieuw en alle fouten) bekijken en bevestigen. Koppelen op het opgavenummer; bij één fout wordt niets ingelezen.
 */
export function ImportStartNumbers({ onDone }: { onDone: (message: string) => void }) {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  function reset() {
    setFile(null);
    setPreview(null);
    setError(null);
  }

  async function load(selected: File) {
    setFile(selected);
    setPreview(null);
    setError(null);
    setBusy(true);
    const form = new FormData();
    form.append('file', selected);
    try {
      setPreview(await uploadJson<Preview>(auth, '/api/v1/admin/parade/start-numbers/import/preview', form));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  async function confirm() {
    if (!file || !preview) return;
    setBusy(true);
    setError(null);
    const form = new FormData();
    form.append('file', file);
    form.append('version', String(preview.version));
    try {
      const result = await uploadJson<Result>(auth, '/api/v1/admin/parade/start-numbers/import', form);
      await Promise.all(
        [['parade-registrations'], ['parade-registration'], ['parade-composition']].map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      );
      setOpen(false);
      reset();
      onDone(
        result.changed
          ? `${result.changed} startnummer(s) ingelezen.`
          : 'Geen wijzigingen: alle startnummers waren al zo.',
      );
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
          reset();
          setOpen(true);
        }}
      >
        Startnummers importeren
      </button>
      <Dialog open={open} title="Startnummers importeren" onClose={() => setOpen(false)}>
        <p>
          Gebruik de export als basis: vul de kolom <strong>Startnummer</strong> in en upload het bestand. De koppeling
          gaat op de kolom <strong>Opgave</strong>; een lege cel maakt het startnummer leeg. Je ziet eerst wat er
          verandert.
        </p>
        <div className="field">
          <label htmlFor="import-bestand">Excel-bestand (.xlsx)</label>
          <input
            id="import-bestand"
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={(e) => {
              const selected = e.target.files?.[0];
              if (selected) void load(selected);
            }}
          />
        </div>
        <ProblemAlert error={error} />
        {busy && !preview ? <p className="muted">Bestand lezen…</p> : null}
        {preview ? (
          <>
            {preview.errors.length ? (
              <div className="alert alert-error" role="alert">
                <strong>
                  {preview.errors.length} fout{preview.errors.length === 1 ? '' : 'en'}: er wordt niets ingelezen.
                </strong>
                <ul>
                  {preview.errors.map((e, i) => (
                    <li key={i}>
                      {e.row ? `Regel ${e.row}: ` : ''}
                      {e.message}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {preview.changes.length === 0 && preview.errors.length === 0 ? (
              <p role="status">Geen wijzigingen: alle startnummers in het bestand zijn al zo.</p>
            ) : null}
            {preview.changes.length ? (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Wijzigingen">
                <table className="table compact">
                  <caption>
                    {preview.changes.length} wijziging{preview.changes.length === 1 ? '' : 'en'}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">Opgave</th>
                      <th scope="col">Groep</th>
                      <th scope="col">Oud</th>
                      <th scope="col">Nieuw</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.changes.map((c) => (
                      <tr key={c.registrationNumber}>
                        <td>{c.registrationNumber}</td>
                        <td>
                          {c.groupName ?? '–'} {c.published ? <span className="badge warn">gepubliceerd</span> : null}
                        </td>
                        <td>{c.oldStartNumber ?? '–'}</td>
                        <td>{c.newStartNumber ?? 'leeg'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
            {preview.changes.some((c) => c.published) ? (
              <p className="muted small-text">
                Groepen met een gepubliceerd startnummer krijgen een melding van hun nieuwe nummer.
              </p>
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
            {preview?.changes.length ? `${preview.changes.length} wijziging(en) inlezen` : 'Inlezen'}
          </button>
        </div>
      </Dialog>
    </>
  );
}
