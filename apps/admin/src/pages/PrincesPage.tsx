import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  princeKindLabels,
  settingsRequest,
  usePrinces,
  useWebsiteSettings,
  WEBSITE_KEYS,
  type Prince,
  type PrinceKind,
  type PrinceRequest,
} from '../api/website';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

const kinds: PrinceKind[] = ['Prince', 'YouthPrince'];

/** Website → Prinsen: de prinsengalerie en (apart) de jeugdprinsen; niet gekoppeld aan de ledenlijst. */
export function PrincesPage() {
  const api = useApi();
  const [kind, setKind] = useState<PrinceKind>('Prince');
  const princes = usePrinces(kind);
  const settings = useWebsiteSettings();
  const [editing, setEditing] = useState<Prince | 'nieuw' | null>(null);
  const [removing, setRemoving] = useState<Prince | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showYouth, setShowYouth] = useState<boolean | null>(null);
  const remove = useApiMutation(async (id: string) => api.DELETE('/api/v1/admin/website/princes/{id}', { params: { path: { id } } }), WEBSITE_KEYS);
  const toggle = useApiMutation(
    async (show: boolean) =>
      api.PUT('/api/v1/admin/website/settings', { body: { ...settingsRequest(settings.data!), showYouthPrinces: show } }),
    WEBSITE_KEYS,
  );
  const youth = kind === 'YouthPrince';

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Prinsen</h1>
          <p className="muted">De prinsengalerie op de website. Niet gekoppeld aan de ledenlijst.</p>
        </div>
        <button type="button" className="button" onClick={() => setEditing('nieuw')}>
          {youth ? 'Jeugdprins(es) toevoegen' : 'Prins toevoegen'}
        </button>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={princes.error ?? remove.error ?? toggle.error} />
      <div className="tabs" role="tablist" aria-label="Soort">
        {kinds.map((k) => (
          <button key={k} type="button" role="tab" className="tab" aria-selected={k === kind} onClick={() => setKind(k)}>
            {princeKindLabels[k]}
          </button>
        ))}
      </div>
      {youth && settings.data ? (
        <div className="card compact">
          <Checkbox
            label="Pagina Jeugdprinsen tonen op de website"
            checked={showYouth ?? settings.data.showYouthPrinces}
            disabled={toggle.isPending}
            onChange={(e) => {
              const show = e.target.checked;
              setShowYouth(show);
              toggle.mutate(show, {
                onSuccess: () => setMessage(show ? 'De pagina Jeugdprinsen staat online.' : 'De pagina Jeugdprinsen is verborgen.'),
                onError: () => setShowYouth(null),
              });
            }}
          />
          <p className="muted">Zet de pagina pas aan als de jeugdprinsen zijn ingevuld.</p>
        </div>
      ) : null}
      <div className="table-scroll table-wrapper" tabIndex={0} role="tabpanel" aria-label={princeKindLabels[kind]}>
        <table className="table">
          <caption className="visually-hidden">{princeKindLabels[kind]}</caption>
          <thead>
            <tr>
              <th scope="col">Jaar</th>
              <th scope="col">Foto</th>
              <th scope="col">Prinsennaam</th>
              <th scope="col">Naam</th>
              <th scope="col">Motto</th>
              <th scope="col">
                <span className="visually-hidden">Acties</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(princes.data ?? []).map((p) => (
              <tr key={p.id}>
                <td>
                  <strong>{p.year}</strong>
                </td>
                <td>{p.photoUrl ? <img src={p.photoUrl} alt="" className="thumb" /> : <span className="thumb thumb-empty" />}</td>
                <td>
                  <strong>{p.princeName}</strong>
                </td>
                <td>{p.name}</td>
                <td className="muted truncate">{p.motto}</td>
                <td className="actions-cell">
                  <button type="button" className="button ghost small" onClick={() => setEditing(p)}>
                    Bewerken <span className="visually-hidden">{p.princeName}</span>
                  </button>
                  <button type="button" className="button ghost small" onClick={() => setRemoving(p)}>
                    Verwijderen <span className="visually-hidden">{p.princeName}</span>
                  </button>
                </td>
              </tr>
            ))}
            {princes.data?.length === 0 ? (
              <tr>
                <td colSpan={6} className="muted">
                  Nog geen {youth ? 'jeugdprinsen' : 'prinsen'}.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
      {editing ? (
        <PrinceDialog
          prince={editing === 'nieuw' ? null : editing}
          kind={kind}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removing !== null}
        title="Verwijderen?"
        message={`${removing?.princeName ?? ''} (${removing?.year ?? ''}) verdwijnt uit de prinsengalerie.`}
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id, { onSuccess: () => { setRemoving(null); setMessage('Verwijderd.'); } })}
      />
    </>
  );
}

function PrinceDialog({ prince, kind, onClose }: { prince: Prince | null; kind: PrinceKind; onClose: (saved?: string) => void }) {
  const api = useApi();
  const [form, setForm] = useState<PrinceRequest>({
    kind: prince?.kind ?? kind,
    year: prince?.year ?? new Date().getFullYear(),
    princeName: prince?.princeName ?? '',
    name: prince?.name ?? null,
    motto: prince?.motto ?? null,
    photo: null,
  });
  const save = useApiMutation(
    async (body: PrinceRequest) =>
      prince
        ? api.PUT('/api/v1/admin/website/princes/{id}', { params: { path: { id: prince.id } }, body })
        : api.POST('/api/v1/admin/website/princes', { body }),
    WEBSITE_KEYS,
  );
  const set = (change: Partial<PrinceRequest>) => setForm({ ...form, ...change });
  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, { onSuccess: () => onClose('Opgeslagen.') });
  }
  return (
    <Dialog open title={prince ? `${prince.princeName} bewerken` : kind === 'YouthPrince' ? 'Jeugdprins(es) toevoegen' : 'Prins toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Jaar (carnaval)" type="number" min={1958} max={2100} required value={form.year} onChange={(e) => set({ year: Number(e.target.value) })} />
          <Field label="Prinsennaam" required maxLength={100} placeholder="Prins Ferry I" value={form.princeName} onChange={(e) => set({ princeName: e.target.value })} />
        </div>
        <Field label="Naam" maxLength={150} value={form.name ?? ''} onChange={(e) => set({ name: e.target.value || null })} />
        <div className="field">
          <label htmlFor="prins-motto">Motto</label>
          <textarea id="prins-motto" rows={2} maxLength={500} value={form.motto ?? ''} onChange={(e) => set({ motto: e.target.value || null })} />
        </div>
        <ImagePicker label="Foto" uploadPath="/api/v1/admin/website/images" currentUrl={prince?.photoUrl} onChange={(photo) => set({ photo })} />
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
