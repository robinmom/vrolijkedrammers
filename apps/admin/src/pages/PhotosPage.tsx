import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useAdminAlbum, useAdminAlbums, useApiMutation, type AlbumRequest } from '../api/hooks';
import { upload } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { PublicationFields, defaultPublication } from '../components/PublicationFields';
import { formatDate, statusLabels, visibilityLabels } from '../format';

export function PhotosPage() {
  const albums = useAdminAlbums();
  return (
    <>
      <div className="page-header">
        <h1>Foto's</h1>
        <Link to="/fotos/$id" params={{ id: 'nieuw' }} className="button">
          Album toevoegen
        </Link>
      </div>
      <ProblemAlert error={albums.error} />
      <div className="table-scroll table-wrapper">
        <table className="table">
          <caption className="visually-hidden">Fotoalbums</caption>
          <thead>
            <tr>
              <th scope="col">Album</th>
              <th scope="col">Datum</th>
              <th scope="col">Foto's</th>
              <th scope="col">Zichtbaar voor</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {(albums.data ?? []).map((a) => (
              <tr key={a.id}>
                <td>
                  <Link to="/fotos/$id" params={{ id: a.id }}>
                    {a.title}
                  </Link>
                </td>
                <td>{formatDate(a.albumDate)}</td>
                <td>{a.photoCount}</td>
                <td>{visibilityLabels[a.visibility]}</td>
                <td>{statusLabels[a.status]}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}

const emptyAlbum: AlbumRequest = { title: '', albumDate: null, description: null, eventId: null, publication: defaultPublication };
const processingLabels: Record<string, string> = { Pending: 'In verwerking', Ready: 'Klaar', Rejected: 'Geweigerd' };

export function AlbumEditorPage() {
  const { id } = useParams({ from: '/fotos/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const auth = useAuth();
  const navigate = useNavigate();
  const album = useAdminAlbum(isNew ? null : id, true);
  const [form, setForm] = useState<AlbumRequest>(emptyAlbum);
  const [message, setMessage] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<unknown>(null);
  const [uploading, setUploading] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    const a = album.data;
    if (a) {
      setForm({ title: a.title, albumDate: a.albumDate, description: a.description, eventId: a.eventId, publication: a.publication });
    }
  }, [album.data]);

  const save = useApiMutation(
    async (body: AlbumRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/photo-albums', { body })).data?.id
        : (await api.PUT('/api/v1/admin/photo-albums/{id}', { params: { path: { id } }, body }), id),
    [['admin-albums'], ['admin-album', id]],
  );
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/photo-albums/{id}', { params: { path: { id } } }), [['admin-albums']]);
  const photos = album.data?.photos ?? [];

  async function refresh(text: string) {
    await album.refetch();
    setMessage(text);
  }

  async function uploadPhotos(files: FileList) {
    setUploadError(null);
    setUploading(true);
    const data = new FormData();
    Array.from(files).forEach((file) => data.append('files', file));
    try {
      await upload(auth, 'POST', `/api/v1/admin/photo-albums/${id}/photos`, data);
      await refresh(`${files.length} foto('s) geüpload; ze verschijnen zodra de verwerking klaar is.`);
    } catch (error) {
      setUploadError(error);
    } finally {
      setUploading(false);
    }
  }

  async function move(index: number, direction: -1 | 1) {
    const order = photos.map((p) => p.id);
    const target = index + direction;
    [order[index], order[target]] = [order[target]!, order[index]!];
    await api.PUT('/api/v1/admin/photo-albums/{id}/photo-order', { params: { path: { id } }, body: { photoIds: order } });
    await album.refetch();
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Album opgeslagen.');
        if (isNew && newId) {
          void navigate({ to: '/fotos/$id', params: { id: newId } });
        }
      },
    });
  }

  return (
    <>
      <p>
        <Link to="/fotos">← Foto's</Link>
      </p>
      <h1>{isNew ? 'Album toevoegen' : form.title || 'Album'}</h1>
      <SuccessMessage message={message} />
      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          <Field label="Titel" required value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
          <Field label="Datum" type="date" value={form.albumDate ?? ''} onChange={(e) => setForm({ ...form, albumDate: e.target.value || null })} />
        </div>
        <Field label="Omschrijving" value={form.description ?? ''} onChange={(e) => setForm({ ...form, description: e.target.value || null })} />
        <PublicationFields value={form.publication} onChange={(publication) => setForm({ ...form, publication })} />
        <ProblemAlert error={save.error ?? remove.error} />
        <div className="actions">
          {!isNew ? (
            <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
              Album verwijderen
            </button>
          ) : null}
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
      {!isNew ? (
        <section className="card" aria-labelledby="fotos-titel">
          <h2 id="fotos-titel">Foto's ({photos.length})</h2>
          <div className="field">
            <label htmlFor="foto-upload">Foto's uploaden (JPEG, PNG of WebP; max. 20 per keer, 25 MB per foto)</label>
            <input
              id="foto-upload"
              type="file"
              multiple
              accept="image/jpeg,image/png,image/webp"
              disabled={uploading}
              onChange={(e) => e.target.files?.length && void uploadPhotos(e.target.files)}
            />
          </div>
          <ProblemAlert error={uploadError} />
          <ul className="photo-grid">
            {photos.map((photo, index) => (
              <li key={photo.id} className={photo.hidden ? 'hidden-photo' : undefined}>
                {photo.thumbnailUrl ? <img src={photo.thumbnailUrl} alt={photo.caption ?? `Foto ${index + 1}`} /> : <div className="placeholder" />}
                <p>
                  <span className="badge">{processingLabels[photo.processingStatus]}</span>
                  {photo.hidden ? <span className="badge warn">Verborgen</span> : null}
                  {album.data?.coverPhotoId === photo.id ? <span className="badge ok">Omslag</span> : null}
                </p>
                <div className="actions">
                  <button type="button" className="button secondary small" disabled={index === 0} onClick={() => void move(index, -1)}>
                    ← <span className="visually-hidden">Foto {index + 1} naar voren</span>
                  </button>
                  <button type="button" className="button secondary small" disabled={index === photos.length - 1} onClick={() => void move(index, 1)}>
                    → <span className="visually-hidden">Foto {index + 1} naar achteren</span>
                  </button>
                  <button
                    type="button"
                    className="button secondary small"
                    onClick={async () => {
                      await api.PUT('/api/v1/admin/photos/{photoId}', {
                        params: { path: { photoId: photo.id } },
                        body: { hidden: !photo.hidden, caption: photo.caption, photographer: photo.photographer },
                      });
                      await refresh(photo.hidden ? 'Foto weer zichtbaar.' : 'Foto verborgen.');
                    }}
                  >
                    {photo.hidden ? 'Tonen' : 'Verbergen'} <span className="visually-hidden">foto {index + 1}</span>
                  </button>
                  <button
                    type="button"
                    className="button secondary small"
                    disabled={photo.processingStatus !== 'Ready'}
                    onClick={async () => {
                      await api.PUT('/api/v1/admin/photo-albums/{id}/cover/{photoId}', { params: { path: { id, photoId: photo.id } } });
                      await refresh('Omslagfoto ingesteld.');
                    }}
                  >
                    Omslag <span className="visually-hidden">foto {index + 1}</span>
                  </button>
                  <button
                    type="button"
                    className="button danger small"
                    onClick={async () => {
                      await api.DELETE('/api/v1/admin/photos/{photoId}', { params: { path: { photoId: photo.id } } });
                      await refresh('Foto verwijderd.');
                    }}
                  >
                    Verwijderen <span className="visually-hidden">foto {index + 1}</span>
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      <ConfirmDialog
        open={confirmDelete}
        title="Album verwijderen?"
        message="Het album en alle foto's worden definitief verwijderd."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/fotos' }) })}
      />
    </>
  );
}
