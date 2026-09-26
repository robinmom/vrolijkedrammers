import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useAdminEvent, useAdminEvents, useApiMutation, useEventCategories, type EventRequest } from '../api/hooks';
import { upload } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { PublicationFields, defaultPublication } from '../components/PublicationFields';
import { formatDateTime, fromLocalInput, statusLabels, toLocalInput, visibilityLabels } from '../format';

export function EventsPage() {
  const [includePast, setIncludePast] = useState(false);
  const events = useAdminEvents(includePast);
  return (
    <>
      <div className="page-header">
        <h1>Agenda</h1>
        <Link to="/agenda/$id" params={{ id: 'nieuw' }} className="button">
          Event toevoegen
        </Link>
      </div>
      <Checkbox label="Ook afgelopen events tonen" checked={includePast} onChange={(e) => setIncludePast(e.target.checked)} />
      <ProblemAlert error={events.error} />
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Activiteiten">
        <table className="table">
          <caption className="visually-hidden">Events</caption>
          <thead>
            <tr>
              <th scope="col">Wanneer</th>
              <th scope="col">Titel</th>
              <th scope="col">Zichtbaar voor</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {(events.data ?? []).map((e) => (
              <tr key={e.id}>
                <td>{formatDateTime(e.startAt)}</td>
                <td>
                  <Link to="/agenda/$id" params={{ id: e.id }}>
                    {e.title}
                  </Link>
                  {e.isHighlight ? <span className="badge">Hoogtepunt</span> : null}
                </td>
                <td>{visibilityLabels[e.visibility]}</td>
                <td>{statusLabels[e.status]}</td>
              </tr>
            ))}
            {events.data?.length === 0 ? (
              <tr>
                <td colSpan={4} className="muted">
                  Geen events.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </>
  );
}

const emptyEvent: EventRequest = {
  categoryId: 1, title: '', summary: null, description: null, startAt: '', endAt: null, allDay: false, locationName: null,
  locationAddress: null, latitude: null, longitude: null, isHighlight: false, badgeText: null, publication: defaultPublication,
};

export function EventEditorPage() {
  const { id } = useParams({ from: '/agenda/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const auth = useAuth();
  const navigate = useNavigate();
  const categories = useEventCategories();
  const existing = useAdminEvent(isNew ? null : id);
  const [form, setForm] = useState<EventRequest>(emptyEvent);
  const [message, setMessage] = useState<string | null>(null);
  const [fileError, setFileError] = useState<unknown>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    const e = existing.data;
    if (e) {
      setForm({
        categoryId: e.categoryId, title: e.title, summary: e.summary, description: e.description, startAt: e.startAt, endAt: e.endAt,
        allDay: e.allDay, locationName: e.locationName, locationAddress: e.locationAddress, latitude: e.latitude, longitude: e.longitude,
        isHighlight: e.isHighlight, badgeText: e.badgeText, publication: e.publication,
      });
    }
  }, [existing.data]);

  const save = useApiMutation(
    async (body: EventRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/events', { body })).data?.id
        : (await api.PUT('/api/v1/admin/events/{id}', { params: { path: { id } }, body }), id),
    [['admin-events'], ['admin-event', id]],
  );
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/events/{id}', { params: { path: { id } } }), [['admin-events']]);
  const set = (change: Partial<EventRequest>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Event opgeslagen.');
        if (isNew && newId) {
          void navigate({ to: '/agenda/$id', params: { id: newId } });
        }
      },
    });
  }

  async function uploadFile(kind: 'image' | 'attachments', file: File) {
    setFileError(null);
    const form = new FormData();
    form.append('file', file);
    try {
      await upload(auth, kind === 'image' ? 'PUT' : 'POST', `/api/v1/admin/events/${id}/${kind}`, form);
      await existing.refetch();
      setMessage(kind === 'image' ? 'Afbeelding opgeslagen.' : 'Bijlage toegevoegd.');
    } catch (error) {
      setFileError(error);
    }
  }

  return (
    <>
      <p>
        <Link to="/agenda">← Agenda</Link>
      </p>
      <h1>{isNew ? 'Event toevoegen' : form.title || 'Event'}</h1>
      <SuccessMessage message={message} />
      <form className="card" onSubmit={submit}>
        <Field label="Titel" required value={form.title} onChange={(e) => set({ title: e.target.value })} />
        <div className="form-grid">
          <div className="field">
            <label htmlFor="categorie">Categorie</label>
            <select id="categorie" value={form.categoryId} onChange={(e) => set({ categoryId: Number(e.target.value) })}>
              {(categories.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <Field label="Begint" type="datetime-local" required value={toLocalInput(form.startAt)} onChange={(e) => set({ startAt: fromLocalInput(e.target.value) ?? '' })} />
          <Field label="Eindigt" type="datetime-local" value={toLocalInput(form.endAt)} onChange={(e) => set({ endAt: fromLocalInput(e.target.value) })} />
          <Field label="Locatie" value={form.locationName ?? ''} onChange={(e) => set({ locationName: e.target.value || null })} />
          <Field label="Adres" value={form.locationAddress ?? ''} onChange={(e) => set({ locationAddress: e.target.value || null })} />
          <Field label="Badge (bijv. Uitverkocht)" maxLength={40} value={form.badgeText ?? ''} onChange={(e) => set({ badgeText: e.target.value || null })} />
        </div>
        <Checkbox label="Hele dag" checked={form.allDay} onChange={(e) => set({ allDay: e.target.checked })} />
        <Checkbox label="Hoogtepunt" checked={form.isHighlight} onChange={(e) => set({ isHighlight: e.target.checked })} />
        <Field label="Korte samenvatting" maxLength={500} value={form.summary ?? ''} onChange={(e) => set({ summary: e.target.value || null })} />
        <div className="field">
          <label htmlFor="omschrijving">Omschrijving (Markdown)</label>
          <textarea id="omschrijving" rows={8} value={form.description ?? ''} onChange={(e) => set({ description: e.target.value || null })} />
        </div>
        <PublicationFields value={form.publication} onChange={(publication) => set({ publication })} />
        <ProblemAlert error={save.error ?? remove.error} />
        <div className="actions">
          {!isNew ? (
            <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
              Verwijderen
            </button>
          ) : null}
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
      {!isNew && existing.data ? (
        <section className="card" aria-labelledby="bestanden">
          <h2 id="bestanden">Afbeelding en bijlagen</h2>
          {existing.data.imageUrl ? <img src={existing.data.imageUrl} alt="" className="preview" /> : <p className="muted">Nog geen afbeelding.</p>}
          <div className="field">
            <label htmlFor="afbeelding">Afbeelding (JPEG, PNG of WebP, max. 10 MB)</label>
            <input id="afbeelding" type="file" accept="image/jpeg,image/png,image/webp" onChange={(e) => e.target.files?.[0] && void uploadFile('image', e.target.files[0])} />
          </div>
          <ul className="plain">
            {existing.data.attachments.map((a) => (
              <li key={a.id}>
                <a href={a.url} target="_blank" rel="noreferrer">
                  {a.fileName}
                </a>{' '}
                <button
                  type="button"
                  className="button secondary small"
                  onClick={async () => {
                    await api.DELETE('/api/v1/admin/events/{id}/attachments/{attachmentId}', { params: { path: { id, attachmentId: a.id } } });
                    await existing.refetch();
                  }}
                >
                  Verwijderen <span className="visually-hidden">{a.fileName}</span>
                </button>
              </li>
            ))}
          </ul>
          <div className="field">
            <label htmlFor="bijlage">Bijlage toevoegen (PDF, JPEG of PNG, max. 10 MB)</label>
            <input id="bijlage" type="file" accept="application/pdf,image/jpeg,image/png" onChange={(e) => e.target.files?.[0] && void uploadFile('attachments', e.target.files[0])} />
          </div>
          <ProblemAlert error={fileError} />
        </section>
      ) : null}
      <ConfirmDialog
        open={confirmDelete}
        title="Event verwijderen?"
        message="Het event en de bijlagen worden definitief verwijderd."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/agenda' }) })}
      />
    </>
  );
}
