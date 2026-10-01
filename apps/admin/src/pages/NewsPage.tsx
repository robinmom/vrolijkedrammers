import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useAdminNews, useAdminNewsItem, useApiMutation, useMe, type NewsRequest } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { PublicationFields, defaultPublication } from '../components/PublicationFields';
import { formatDateTime, fromLocalInput, notificationStatusLabels, statusLabels, toLocalInput, visibilityLabels } from '../format';

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
              <th scope="col">Website</th>
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
                <td>{n.showOnWebsite ? <span className="badge info">Website</span> : null}</td>
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

const emptyNews: NewsRequest = {
  title: '',
  summary: null,
  body: '',
  category: null,
  expireAt: null,
  publication: defaultPublication,
  pushOnPublish: false,
  showOnWebsite: false,
  websiteBody: null,
  slug: null,
  image: null,
};

export function NewsEditorPage() {
  const { id } = useParams({ from: '/nieuws/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const existing = useAdminNewsItem(isNew ? null : id);
  const [form, setForm] = useState<NewsRequest>(emptyNews);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canPush =
    permissions.includes('notification.send') && (form.publication.visibility !== 'Public' || permissions.includes('notification.send.urgent'));

  useEffect(() => {
    const n = existing.data;
    if (n) {
      setForm({
        title: n.title,
        summary: n.summary,
        body: n.body,
        category: n.category,
        expireAt: n.expireAt,
        publication: n.publication,
        pushOnPublish: n.pushOnPublish,
        showOnWebsite: n.showOnWebsite ?? false,
        websiteBody: n.websiteBody ?? null,
        slug: n.slug ?? null,
        image: null,
      });
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
        <PublicationFields value={form.publication} onChange={(publication) => set({ publication })} seasonWarning />
        <fieldset>
          <legend>Afbeelding</legend>
          <ImagePicker
            label="Afbeelding"
            uploadPath="/api/v1/admin/news/images"
            currentUrl={existing.data?.imageUrl}
            onChange={(image) => set({ image })}
          />
        </fieldset>
        <fieldset className="website-fieldset">
          <legend>Website</legend>
          <Checkbox
            label="Ook tonen op de website (bij Nieuws en op de homepage)"
            checked={form.showOnWebsite ?? false}
            disabled={form.publication.visibility !== 'Public'}
            onChange={(e) => set({ showOnWebsite: e.target.checked })}
          />
          {form.publication.visibility !== 'Public' ? (
            <p className="muted">Alleen openbaar nieuws kan op de website. Zet de zichtbaarheid op Openbaar.</p>
          ) : null}
          {form.showOnWebsite ? (
            <>
              <div className="field">
                <label htmlFor="websitetekst">Langere tekst voor de website (optioneel, Markdown)</label>
                <textarea
                  id="websitetekst"
                  rows={10}
                  aria-describedby="websitetekst-hint"
                  value={form.websiteBody ?? ''}
                  onChange={(e) => set({ websiteBody: e.target.value || null })}
                />
                <small id="websitetekst-hint" className="muted">
                  Staat op de website onder de tekst uit de app. Laat leeg als de tekst uit de app genoeg is.
                </small>
              </div>
              <Field
                label="Webadres"
                hint="Wordt uit de titel gemaakt als je het leeg laat. Alleen kleine letters, cijfers en streepjes."
                value={form.slug ?? ''}
                onChange={(e) => set({ slug: e.target.value || null })}
              />
            </>
          ) : null}
        </fieldset>
        <fieldset>
          <legend>Pushmelding</legend>
          {existing.data?.pushStatus ? (
            <p>Pushmelding: {notificationStatusLabels[existing.data.pushStatus] ?? existing.data.pushStatus}. Er gaat per bericht maar één melding uit.</p>
          ) : (
            <>
              <Checkbox
                label="Pushmelding bij publicatie (aan dezelfde doelgroep)"
                checked={form.pushOnPublish ?? false}
                disabled={!canPush}
                onChange={(e) => set({ pushOnPublish: e.target.checked })}
              />
              {!canPush ? (
                <p className="muted">
                  {form.publication.visibility === 'Public'
                    ? 'Een push aan iedereen (openbaar bericht) mag alleen met het recht voor dringende meldingen.'
                    : 'Push bij publicatie vraagt het recht om meldingen te versturen.'}
                </p>
              ) : null}
            </>
          )}
        </fieldset>
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
