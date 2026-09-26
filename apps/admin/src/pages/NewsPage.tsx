import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useAdminNews, useAdminNewsItem, useApiMutation, type NewsRequest } from '../api/hooks';
import { upload } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { PublicationFields, defaultPublication } from '../components/PublicationFields';
import { formatDateTime, fromLocalInput, statusLabels, toLocalInput, visibilityLabels } from '../format';

export function NewsPage() {
  const news = useAdminNews();
  return (
    <>
      <div className="page-header">
        <h1>Nieuws</h1>
        <Link to="/nieuws/$id" params={{ id: 'nieuw' }} className="button">
          Bericht toevoegen
        </Link>
      </div>
      <ProblemAlert error={news.error} />
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Nieuwsberichten">
        <table className="table">
          <caption className="visually-hidden">Nieuwsberichten</caption>
          <thead>
            <tr>
              <th scope="col">Titel</th>
              <th scope="col">Zichtbaar voor</th>
              <th scope="col">Status</th>
              <th scope="col">Publicatie</th>
            </tr>
          </thead>
          <tbody>
            {(news.data ?? []).map((n) => (
              <tr key={n.id}>
                <td>
                  <Link to="/nieuws/$id" params={{ id: n.id }}>
                    {n.title}
                  </Link>
                </td>
                <td>{visibilityLabels[n.visibility]}</td>
                <td>{statusLabels[n.status]}</td>
                <td>{formatDateTime(n.publishAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

const emptyNews: NewsRequest = { title: '', summary: null, body: '', category: null, expireAt: null, publication: defaultPublication };

export function NewsEditorPage() {
  const { id } = useParams({ from: '/nieuws/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const auth = useAuth();
  const navigate = useNavigate();
  const existing = useAdminNewsItem(isNew ? null : id);
  const [form, setForm] = useState<NewsRequest>(emptyNews);
  const [message, setMessage] = useState<string | null>(null);
  const [fileError, setFileError] = useState<unknown>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    const n = existing.data;
    if (n) {
      setForm({ title: n.title, summary: n.summary, body: n.body, category: n.category, expireAt: n.expireAt, publication: n.publication });
    }
  }, [existing.data]);

  const save = useApiMutation(
    async (body: NewsRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/news', { body })).data?.id
        : (await api.PUT('/api/v1/admin/news/{id}', { params: { path: { id } }, body }), id),
    [['admin-news'], ['admin-news-item', id]],
  );
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/news/{id}', { params: { path: { id } } }), [['admin-news']]);
  const set = (change: Partial<NewsRequest>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Bericht opgeslagen.');
        if (isNew && newId) {
          void navigate({ to: '/nieuws/$id', params: { id: newId } });
        }
      },
    });
  }

  async function uploadImage(file: File) {
    setFileError(null);
    const data = new FormData();
    data.append('file', file);
    try {
      await upload(auth, 'PUT', `/api/v1/admin/news/${id}/image`, data);
      await existing.refetch();
      setMessage('Afbeelding opgeslagen.');
    } catch (error) {
      setFileError(error);
    }
  }

  return (
    <>
      <p>
        <Link to="/nieuws">← Nieuws</Link>
      </p>
      <h1>{isNew ? 'Bericht toevoegen' : form.title || 'Bericht'}</h1>
      <SuccessMessage message={message} />
      <form className="card" onSubmit={submit}>
        <Field label="Titel" required value={form.title} onChange={(e) => set({ title: e.target.value })} />
        <Field label="Korte samenvatting" maxLength={500} value={form.summary ?? ''} onChange={(e) => set({ summary: e.target.value || null })} />
        <div className="field">
          <label htmlFor="bericht">Bericht (Markdown)</label>
          <textarea id="bericht" rows={10} required value={form.body} onChange={(e) => set({ body: e.target.value })} />
        </div>
        <div className="form-grid">
          <Field label="Categorie" value={form.category ?? ''} onChange={(e) => set({ category: e.target.value || null })} />
          <Field label="Zichtbaar tot" type="datetime-local" value={toLocalInput(form.expireAt)} onChange={(e) => set({ expireAt: fromLocalInput(e.target.value) })} />
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
        <section className="card" aria-labelledby="nieuwsafbeelding">
          <h2 id="nieuwsafbeelding">Afbeelding</h2>
          {existing.data.imageUrl ? <img src={existing.data.imageUrl} alt="" className="preview" /> : <p className="muted">Nog geen afbeelding.</p>}
          <div className="field">
            <label htmlFor="nieuws-afbeelding">Afbeelding (JPEG, PNG of WebP, max. 10 MB)</label>
            <input id="nieuws-afbeelding" type="file" accept="image/jpeg,image/png,image/webp" onChange={(e) => e.target.files?.[0] && void uploadImage(e.target.files[0])} />
          </div>
          <ProblemAlert error={fileError} />
        </section>
      ) : null}
      <ConfirmDialog
        open={confirmDelete}
        title="Bericht verwijderen?"
        message="Het bericht wordt definitief verwijderd. Wil je het alleen verbergen, kies dan de status Gearchiveerd."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/nieuws' }) })}
      />
    </>
  );
}
