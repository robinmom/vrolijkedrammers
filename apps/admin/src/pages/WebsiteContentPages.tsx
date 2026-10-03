import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  useWebsiteAlbums,
  useWebsitePage,
  useWebsitePages,
  WEBSITE_KEYS,
  type WebsiteMenu,
  type WebsitePageRequest,
} from '../api/website';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

const MENUS: { value: WebsiteMenu; label: string }[] = [
  { value: 'None', label: 'Geen menu' },
  { value: 'Association', label: 'Vereniging' },
  { value: 'Carnival', label: 'Carnaval' },
];

const menuLabel = (menu: WebsiteMenu) => MENUS.find((m) => m.value === menu)?.label ?? menu;

/** Website → Pagina's: vaste tekstpagina's (Over ons, Ontstaan, Loillands, Volkslied …). */
export function WebsiteContentPagesPage() {
  const pages = useWebsitePages();
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Pagina's</h1>
          <p className="muted">Vaste tekstpagina's van de website, zoals Over ons, Ontstaan en Loillands. Per pagina kies je in welk menu hij staat.</p>
        </div>
        <Link to="/website/paginas/$id" params={{ id: 'nieuw' }} className="button">
          Pagina toevoegen
        </Link>
      </div>
      <ProblemAlert error={pages.error} />
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Pagina's">
        <table className="table">
          <caption className="visually-hidden">Pagina's van de website</caption>
          <thead>
            <tr>
              <th scope="col">Titel</th>
              <th scope="col">Webadres</th>
              <th scope="col">Menu</th>
              <th scope="col">Status</th>
              <th scope="col">Laatst gewijzigd</th>
            </tr>
          </thead>
          <tbody>
            {(pages.data ?? []).map((p) => (
              <tr key={p.id}>
                <td>
                  <Link to="/website/paginas/$id" params={{ id: p.id }}>
                    {p.title}
                  </Link>
                </td>
                <td>
                  <code>/{p.slug}</code>
                </td>
                <td>{p.menu === 'None' ? <span className="muted">Geen</span> : `${menuLabel(p.menu)} · ${p.sortOrder}`}</td>
                <td>{p.isPublished ? <span className="badge ok">Online</span> : <span className="badge">Concept</span>}</td>
                <td>{formatDateTime(p.updatedAt)}</td>
              </tr>
            ))}
            {pages.data?.length === 0 ? (
              <tr>
                <td colSpan={5} className="muted">
                  Nog geen pagina's.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </>
  );
}

const emptyPage: WebsitePageRequest = {
  slug: null,
  title: '',
  intro: null,
  body: '',
  image: null,
  isPublished: false,
  sortOrder: 0,
  menu: 'None',
  photoAlbumId: null,
};

export function WebsitePageEditorPage() {
  const { id } = useParams({ from: '/website/paginas/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const existing = useWebsitePage(isNew ? null : id);
  const albums = useWebsiteAlbums();
  const [form, setForm] = useState<WebsitePageRequest>(emptyPage);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    const p = existing.data;
    if (p)
      setForm({
        slug: p.slug,
        title: p.title,
        intro: p.intro,
        body: p.body,
        image: null,
        isPublished: p.isPublished,
        sortOrder: p.sortOrder,
        menu: p.menu,
        photoAlbumId: p.photoAlbumId,
      });
  }, [existing.data]);

  const save = useApiMutation(
    async (body: WebsitePageRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/website/pages', { body })).data?.id
        : (await api.PUT('/api/v1/admin/website/pages/{id}', { params: { path: { id } }, body }), id),
    WEBSITE_KEYS,
  );
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/website/pages/{id}', { params: { path: { id } } }), WEBSITE_KEYS);
  const set = (change: Partial<WebsitePageRequest>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Pagina opgeslagen.');
        if (isNew && typeof newId === 'string') void navigate({ to: '/website/paginas/$id', params: { id: newId } });
      },
    });
  }

  return (
    <>
      <p>
        <Link to="/website/paginas">← Pagina's</Link>
      </p>
      <h1>{isNew ? 'Pagina toevoegen' : form.title || 'Pagina'}</h1>
      <SuccessMessage message={message} />
      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          <Field label="Titel" required maxLength={200} value={form.title} onChange={(e) => set({ title: e.target.value })} />
          <Field
            label="Webadres"
            hint="Bijvoorbeeld over-ons. Leeg laten: wordt uit de titel gemaakt."
            maxLength={100}
            value={form.slug ?? ''}
            onChange={(e) => set({ slug: e.target.value || null })}
          />
        </div>
        <Field label="Inleiding" maxLength={500} value={form.intro ?? ''} onChange={(e) => set({ intro: e.target.value || null })} />
        <div className="field">
          <label htmlFor="pagina-tekst">Tekst (Markdown)</label>
          <textarea id="pagina-tekst" rows={16} value={form.body} onChange={(e) => set({ body: e.target.value })} />
        </div>
        <div className="form-grid">
          <div className="field">
            <label htmlFor="pagina-menu">Menu</label>
            <select id="pagina-menu" value={form.menu} onChange={(e) => set({ menu: e.target.value as WebsiteMenu })}>
              {MENUS.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </select>
          </div>
          <Field
            label="Volgorde in het menu"
            type="number"
            hint={
              form.menu === 'Association'
                ? 'Laag eerst. Vaste onderdelen: Kader 10, Prinsengalerie 20, Jeugdprinsen 30, Onderscheidingen 40.'
                : 'Laag eerst.'
            }
            value={form.sortOrder}
            onChange={(e) => set({ sortOrder: Number(e.target.value) || 0 })}
          />
        </div>
        <div className="field">
          <label htmlFor="pagina-album">Fotoalbum onder de tekst</label>
          <select id="pagina-album" aria-describedby="pagina-album-hint" value={form.photoAlbumId ?? ''} onChange={(e) => set({ photoAlbumId: e.target.value || null })}>
            <option value="">Geen album</option>
            {(albums.data ?? []).map((a) => (
              <option key={a.id} value={a.id}>
                {a.title} ({a.photoCount === 1 ? '1 foto' : `${a.photoCount} foto's`})
              </option>
            ))}
          </select>
          <small id="pagina-album-hint" className="muted">
            Alleen een openbaar, gepubliceerd album is op de website te zien.
          </small>
        </div>
        <ImagePicker label="Afbeelding bovenaan" uploadPath="/api/v1/admin/website/images" currentUrl={existing.data?.imageUrl} onChange={(image) => set({ image })} />
        <Checkbox label="Online (zichtbaar op de website)" checked={form.isPublished} onChange={(e) => set({ isPublished: e.target.checked })} />
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
        title="Pagina verwijderen?"
        message="De pagina verdwijnt van de website. Wil je hem tijdelijk verbergen, zet dan 'Online' uit."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/website/paginas' }) })}
      />
    </>
  );
}
