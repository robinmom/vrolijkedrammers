import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { awardLabels, useAwards, WEBSITE_KEYS, type Award, type AwardRequest, type AwardType } from '../api/website';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

const types = Object.keys(awardLabels) as AwardType[];

/** Website → Onderscheidingen: 't Drammertje, De Verdienstelijke Didammer en Het Eikenloof van Boschslag, per jaar. */
export function AwardsPage() {
  const api = useApi();
  const [type, setType] = useState<AwardType | null>(null);
  const awards = useAwards(type);
  const [editing, setEditing] = useState<Award | 'nieuw' | null>(null);
  const [removing, setRemoving] = useState<Award | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const remove = useApiMutation(async (id: string) => api.DELETE('/api/v1/admin/website/awards/{id}', { params: { path: { id } } }), WEBSITE_KEYS);

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Onderscheidingen</h1>
          <p className="muted">Per jaar zichtbaar op de website bij Vereniging → Onderscheidingen.</p>
        </div>
        <button type="button" className="button" onClick={() => setEditing('nieuw')}>
          Onderscheiding toevoegen
        </button>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={awards.error ?? remove.error} />
      <div className="chips" role="group" aria-label="Soort onderscheiding">
        <button type="button" className={type === null ? 'chip active' : 'chip'} aria-pressed={type === null} onClick={() => setType(null)}>
          Alle
        </button>
        {types.map((t) => (
          <button key={t} type="button" className={type === t ? 'chip active' : 'chip'} aria-pressed={type === t} onClick={() => setType(t)}>
            {awardLabels[t]}
          </button>
        ))}
      </div>
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Onderscheidingen">
        <table className="table">
          <caption className="visually-hidden">Onderscheidingen</caption>
          <thead>
            <tr>
              <th scope="col">Jaar</th>
              <th scope="col">Foto</th>
              <th scope="col">Soort</th>
              <th scope="col">Ontvanger</th>
              <th scope="col">Status</th>
              <th scope="col">
                <span className="visually-hidden">Acties</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(awards.data ?? []).map((a) => (
              <tr key={a.id}>
                <td>
                  <strong>{a.year}</strong>
                </td>
                <td>{a.photoUrl ? <img src={a.photoUrl} alt="" className="thumb" /> : <span className="thumb thumb-empty" />}</td>
                <td>{awardLabels[a.type]}</td>
                <td>
                  <strong>{a.recipient}</strong>
                </td>
                <td>{a.isPublished ? <span className="badge ok">Online</span> : <span className="badge">Concept</span>}</td>
                <td className="actions-cell">
                  <button type="button" className="button ghost small" onClick={() => setEditing(a)}>
                    Bewerken <span className="visually-hidden">{a.recipient}</span>
                  </button>
                  <button type="button" className="button ghost small" onClick={() => setRemoving(a)}>
                    Verwijderen <span className="visually-hidden">{a.recipient}</span>
                  </button>
                </td>
              </tr>
            ))}
            {awards.data?.length === 0 ? (
              <tr>
                <td colSpan={6} className="muted">
                  Nog geen onderscheidingen.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
      {editing ? (
        <AwardDialog
          award={editing === 'nieuw' ? null : editing}
          defaultType={type ?? 'Drammertje'}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removing !== null}
        title="Onderscheiding verwijderen?"
        message={`${removing ? awardLabels[removing.type] : ''} ${removing?.year ?? ''} voor ${removing?.recipient ?? ''} verdwijnt van de website.`}
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id, { onSuccess: () => { setRemoving(null); setMessage('Verwijderd.'); } })}
      />
    </>
  );
}

function AwardDialog({ award, defaultType, onClose }: { award: Award | null; defaultType: AwardType; onClose: (saved?: string) => void }) {
  const api = useApi();
  const [form, setForm] = useState<AwardRequest>({
    type: award?.type ?? defaultType,
    year: award?.year ?? new Date().getFullYear(),
    recipient: award?.recipient ?? '',
    body: award?.body ?? null,
    photo: null,
    isPublished: award?.isPublished ?? true,
  });
  const save = useApiMutation(
    async (body: AwardRequest) =>
      award
        ? api.PUT('/api/v1/admin/website/awards/{id}', { params: { path: { id: award.id } }, body })
        : api.POST('/api/v1/admin/website/awards', { body }),
    WEBSITE_KEYS,
  );
  const set = (change: Partial<AwardRequest>) => setForm({ ...form, ...change });
  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, { onSuccess: () => onClose('Opgeslagen.') });
  }
  return (
    <Dialog open title={award ? 'Onderscheiding bewerken' : 'Onderscheiding toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Jaar" type="number" min={1958} max={2100} required value={form.year} onChange={(e) => set({ year: Number(e.target.value) })} />
          <div className="field">
            <label htmlFor="award-soort">Soort</label>
            <select id="award-soort" value={form.type} onChange={(e) => set({ type: e.target.value as AwardType })}>
              {types.map((t) => (
                <option key={t} value={t}>
                  {awardLabels[t]}
                </option>
              ))}
            </select>
          </div>
        </div>
        <Field label="Ontvanger" required maxLength={150} value={form.recipient} onChange={(e) => set({ recipient: e.target.value })} />
        <div className="field">
          <label htmlFor="award-tekst">Tekst op de website (Markdown)</label>
          <textarea id="award-tekst" rows={8} value={form.body ?? ''} onChange={(e) => set({ body: e.target.value || null })} />
        </div>
        <ImagePicker label="Foto" uploadPath="/api/v1/admin/website/images" currentUrl={award?.photoUrl} onChange={(photo) => set({ photo })} />
        <Checkbox label="Online (zichtbaar op de website)" checked={form.isPublished} onChange={(e) => set({ isPublished: e.target.checked })} />
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </Dialog>
  );
}
