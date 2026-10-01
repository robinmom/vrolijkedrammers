import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useCallback, useEffect, useRef, useState, type DragEvent, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { describeProblem } from '../api/errors';
import { useAdminAlbum, useAdminAlbums, useApiMutation, type AlbumRequest, type Schemas } from '../api/hooks';
import { uploadWithProgress } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { PublicationFields, defaultPublication } from '../components/PublicationFields';
import { formatDate, statusLabels, visibilityLabels } from '../format';

type PhotoCategory = NonNullable<Schemas['PhotoCategory']>;
type PhotoBulkAction = NonNullable<Schemas['PhotoBulkAction']>;

/** Soorten galerij (fase 21b): voor de filters hier en op de website. */
export const categoryLabels: Record<PhotoCategory, string> = {
  Pronkzitting: 'Pronkzitting',
  Carnival: 'Carnaval',
  Parade: 'Optocht',
  Dansgarde: 'Dansgarde',
  Youth: 'Jeugd',
  Events: 'Evenementen',
  Other: 'Overig',
};
const categories = Object.keys(categoryLabels) as PhotoCategory[];

/** Foto's: alle galerijen als kaarten, te filteren op soort. */
export function PhotosPage() {
  const albums = useAdminAlbums();
  const [category, setCategory] = useState<PhotoCategory | null>(null);
  const list = (albums.data ?? []).filter((a) => category === null || a.category === category);
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Foto's</h1>
          <p className="muted">Galerijen voor de app en de website, bijvoorbeeld van de pronkzitting, de optocht en carnaval.</p>
        </div>
        <Link to="/fotos/$id" params={{ id: 'nieuw' }} className="button">
          Nieuwe galerij
        </Link>
      </div>
      <ProblemAlert error={albums.error} />
      <div className="chips" role="group" aria-label="Soort galerij">
        <button type="button" className={category === null ? 'chip active' : 'chip'} aria-pressed={category === null} onClick={() => setCategory(null)}>
          Alle
        </button>
        {categories.map((c) => (
          <button key={c} type="button" className={category === c ? 'chip active' : 'chip'} aria-pressed={category === c} onClick={() => setCategory(c)}>
            {categoryLabels[c]}
          </button>
        ))}
      </div>
      <ul className="album-grid" aria-label="Galerijen">
        {list.map((a) => (
          <li key={a.id}>
            <Link to="/fotos/$id" params={{ id: a.id }} className="album-card">
              {a.coverUrl ? <img src={a.coverUrl} alt="" /> : <span className="album-cover-empty" />}
              <span className="album-card-body">
                <strong>{a.title}</strong>
                <span className="muted">
                  {formatDate(a.albumDate)} · {a.photoCount} foto{a.photoCount === 1 ? '' : "'s"}
                </span>
                <span className="album-badges">
                  <span className="badge info">{categoryLabels[a.category ?? 'Other']}</span>
                  <span className={a.status === 'Published' ? 'badge ok' : 'badge'}>{statusLabels[a.status]}</span>
                  <span className="badge">{visibilityLabels[a.visibility]}</span>
                </span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {albums.data && list.length === 0 ? <p className="muted">Nog geen galerijen{category ? ' van deze soort' : ''}.</p> : null}
    </>
  );
}

const emptyAlbum: AlbumRequest = {
  title: '',
  albumDate: null,
  description: null,
  eventId: null,
  publication: defaultPublication,
  category: 'Other',
};
const processingLabels: Record<string, string> = { Pending: 'In verwerking', Ready: 'Klaar', Rejected: 'Geweigerd' };

type QueueItem = { key: string; file: File; progress: number; status: 'waiting' | 'uploading' | 'done' | 'failed'; error?: string };
const PARALLEL_UPLOADS = 3;
const accepted = ['image/jpeg', 'image/png', 'image/webp'];

/**
 * Bulk-upload (fase 21b): onbeperkt veel foto's slepen of kiezen; ze gaan één voor één (drie tegelijk) naar de server, met
 * voortgang per foto en "opnieuw proberen" bij een fout. Verwerken (verkleinen, zonder GPS) gebeurt daarna op de server.
 */
function useUploadQueue(albumId: string, onUploaded: () => void) {
  const auth = useAuth();
  const [queue, setQueue] = useState<QueueItem[]>([]);
  const running = useRef(new Set<string>());

  const update = useCallback((key: string, change: Partial<QueueItem>) => {
    setQueue((q) => q.map((item) => (item.key === key ? { ...item, ...change } : item)));
  }, []);

  useEffect(() => {
    const free = PARALLEL_UPLOADS - running.current.size;
    const next = queue.filter((i) => i.status === 'waiting' && !running.current.has(i.key)).slice(0, Math.max(0, free));
    for (const item of next) {
      running.current.add(item.key);
      update(item.key, { status: 'uploading', progress: 0 });
      const form = new FormData();
      form.append('files', item.file);
      uploadWithProgress(auth, `/api/v1/admin/photo-albums/${albumId}/photos`, form, (progress) => update(item.key, { progress }))
        .then(() => {
          update(item.key, { status: 'done', progress: 1 });
          onUploaded();
        })
        .catch((error: unknown) => update(item.key, { status: 'failed', error: describeProblem(error) }))
        .finally(() => running.current.delete(item.key));
    }
  }, [queue, albumId, auth, update, onUploaded]);

  const add = (files: FileList | File[]) => {
    const all = Array.from(files);
    const items = all
      .filter((f) => accepted.includes(f.type))
      .map((file, i) => ({ key: `${Date.now()}-${i}-${file.name}`, file, progress: 0, status: 'waiting' as const }));
    setQueue((q) => [...q.filter((i) => i.status !== 'done'), ...items]);
    return all.length - items.length;
  };
  const retry = (key: string) => update(key, { status: 'waiting', progress: 0, error: undefined });
  const retryAll = () => setQueue((q) => q.map((i) => (i.status === 'failed' ? { ...i, status: 'waiting', progress: 0, error: undefined } : i)));
  const clearDone = () => setQueue((q) => q.filter((i) => i.status !== 'done'));
  return { queue, add, retry, retryAll, clearDone };
}

function queueStatus(item: QueueItem) {
  switch (item.status) {
    case 'waiting':
      return 'Wacht';
    case 'uploading':
      return `${Math.round(item.progress * 100)}%`;
    case 'done':
      return 'Geüpload';
    default:
      return 'Mislukt';
  }
}

export function AlbumEditorPage() {
  const { id } = useParams({ from: '/fotos/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const client = useQueryClient();
  const album = useAdminAlbum(isNew ? null : id, true);
  const albums = useAdminAlbums();
  const [form, setForm] = useState<AlbumRequest>(emptyAlbum);
  const [message, setMessage] = useState<string | null>(null);
  const [skipped, setSkipped] = useState(0);
  const [dragging, setDragging] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [target, setTarget] = useState('');
  const [photographer, setPhotographer] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);
  const refetch = album.refetch;
  const onUploaded = useCallback(() => void refetch(), [refetch]);
  const uploads = useUploadQueue(id, onUploaded);

  useEffect(() => {
    const a = album.data;
    if (a) {
      setForm({
        title: a.title,
        albumDate: a.albumDate,
        description: a.description,
        eventId: a.eventId,
        publication: a.publication,
        category: a.category ?? 'Other',
      });
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
  const bulk = useApiMutation(
    async (body: { action: PhotoBulkAction; targetAlbumId?: string; photographer?: string }) =>
      api.POST('/api/v1/admin/photo-albums/{id}/photos/bulk', {
        params: { path: { id } },
        body: { photoIds: [...selected], action: body.action, targetAlbumId: body.targetAlbumId ?? null, photographer: body.photographer ?? null },
      }),
    [['admin-albums'], ['admin-album', id]],
  );
  const photos = album.data?.photos ?? [];
  const pending = photos.filter((p) => p.processingStatus === 'Pending').length;
  const uploading = uploads.queue.filter((i) => i.status === 'waiting' || i.status === 'uploading').length;
  const failed = uploads.queue.filter((i) => i.status === 'failed').length;
  const done = uploads.queue.filter((i) => i.status === 'done').length;

  // Zolang er geüpload wordt, het album blijven verversen (de verwerking zelf ververst useAdminAlbum al).
  useEffect(() => {
    if (uploading === 0) return;
    const timer = setInterval(() => void refetch(), 3000);
    return () => clearInterval(timer);
  }, [uploading, refetch]);

  function runBulk(action: PhotoBulkAction, text: string, extra: { targetAlbumId?: string; photographer?: string } = {}) {
    bulk.mutate(
      { action, ...extra },
      {
        onSuccess: () => {
          setMessage(text);
          if (action === 'Move' || action === 'Delete') setSelected(new Set());
          if (extra.targetAlbumId) void client.invalidateQueries({ queryKey: ['admin-album', extra.targetAlbumId] });
        },
      },
    );
  }

  async function move(index: number, direction: -1 | 1) {
    const order = photos.map((p) => p.id);
    const to = index + direction;
    [order[index], order[to]] = [order[to]!, order[index]!];
    await api.PUT('/api/v1/admin/photo-albums/{id}/photo-order', { params: { path: { id } }, body: { photoIds: order } });
    await album.refetch();
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Galerij opgeslagen.');
        if (isNew && typeof newId === 'string') void navigate({ to: '/fotos/$id', params: { id: newId } });
      },
    });
  }

  function addFiles(files: FileList | File[]) {
    setSkipped(uploads.add(files));
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    if (event.dataTransfer.files.length) addFiles(event.dataTransfer.files);
  }

  const toggle = (photoId: string) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(photoId)) next.delete(photoId);
      else next.add(photoId);
      return next;
    });

  const settingsForm = (
    <form onSubmit={submit}>
      <div className="form-grid">
        <Field label="Titel" required value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        <Field label="Datum" type="date" value={form.albumDate ?? ''} onChange={(e) => setForm({ ...form, albumDate: e.target.value || null })} />
        <div className="field">
          <label htmlFor="galerij-soort">Soort</label>
          <select id="galerij-soort" value={form.category ?? 'Other'} onChange={(e) => setForm({ ...form, category: e.target.value as PhotoCategory })}>
            {categories.map((c) => (
              <option key={c} value={c}>
                {categoryLabels[c]}
              </option>
            ))}
          </select>
        </div>
      </div>
      <Field label="Omschrijving" value={form.description ?? ''} onChange={(e) => setForm({ ...form, description: e.target.value || null })} />
      <PublicationFields value={form.publication} onChange={(publication) => setForm({ ...form, publication })} />
      <p className="muted">Openbare galerijen staan ook op de website.</p>
      <ProblemAlert error={save.error ?? remove.error} />
      <div className="actions">
        {!isNew ? (
          <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
            Galerij verwijderen
          </button>
        ) : null}
        <button type="submit" className="button" disabled={save.isPending}>
          {isNew ? 'Galerij maken' : 'Opslaan'}
        </button>
      </div>
    </form>
  );

  return (
    <>
      <p>
        <Link to="/fotos">← Foto's</Link>
      </p>
      <h1>{isNew ? 'Nieuwe galerij' : form.title || 'Galerij'}</h1>
      <SuccessMessage message={message} />
      {isNew ? (
        <>
          <p className="muted">Maak eerst de galerij; daarna sleep je er zoveel foto's in als je wilt.</p>
          <div className="card">{settingsForm}</div>
        </>
      ) : (
        <>
          <details className="card settings-details">
            <summary>
              Galerij-instellingen ({categoryLabels[form.category ?? 'Other']}, {visibilityLabels[form.publication.visibility]})
            </summary>
            {settingsForm}
          </details>

          <section className="card" aria-labelledby="upload-titel">
            <h2 id="upload-titel">Foto's toevoegen</h2>
            <div
              className={dragging ? 'dropzone dragging' : 'dropzone'}
              onDragOver={(e) => {
                e.preventDefault();
                setDragging(true);
              }}
              onDragLeave={() => setDragging(false)}
              onDrop={onDrop}
            >
              <p>
                <strong>Sleep foto's hierheen</strong> of
              </p>
              <label htmlFor="foto-upload" className="button secondary">
                Kies foto's
              </label>
              <input
                id="foto-upload"
                className="visually-hidden"
                type="file"
                multiple
                accept="image/jpeg,image/png,image/webp"
                onChange={(e) => {
                  if (e.target.files?.length) addFiles(e.target.files);
                  e.target.value = '';
                }}
              />
              <p className="muted">JPEG, PNG of WebP, max. 25 MB per foto. Zoveel als je wilt: ze gaan drie tegelijk omhoog.</p>
            </div>
            {skipped > 0 ? <p className="muted">{skipped} bestand(en) overgeslagen: geen JPEG, PNG of WebP.</p> : null}
            {uploads.queue.length > 0 ? (
              <div className="upload-queue">
                <p role="status">
                  {uploading > 0 ? `Bezig: nog ${uploading} foto('s). ` : ''}
                  {done} geüpload{failed > 0 ? `, ${failed} mislukt` : ''}.{pending > 0 ? ` ${pending} foto('s) worden nog verwerkt.` : ''}
                </p>
                <div className="actions">
                  {failed > 0 ? (
                    <button type="button" className="button secondary small" onClick={uploads.retryAll}>
                      Mislukte opnieuw proberen
                    </button>
                  ) : null}
                  {done > 0 ? (
                    <button type="button" className="button ghost small" onClick={uploads.clearDone}>
                      Klaar-lijst wissen
                    </button>
                  ) : null}
                </div>
                <ul className="queue-list" tabIndex={0} aria-label="Uploadwachtrij">
                  {uploads.queue.map((item) => (
                    <li key={item.key} className={`queue-item ${item.status}`}>
                      <span className="queue-name">{item.file.name}</span>
                      <progress value={Math.round(item.progress * 100)} max={100} aria-label={`Voortgang ${item.file.name}`} />
                      <span className="queue-status">{queueStatus(item)}</span>
                      {item.status === 'failed' ? (
                        <button type="button" className="button ghost small" onClick={() => uploads.retry(item.key)}>
                          Opnieuw <span className="visually-hidden">{item.file.name}</span>
                        </button>
                      ) : null}
                      {item.error ? <span className="queue-error">{item.error}</span> : null}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </section>

          <section className="card" aria-labelledby="fotos-titel">
            <div className="card-header">
              <h2 id="fotos-titel">Foto's ({photos.length})</h2>
              <div className="actions">
                <button type="button" className="button ghost small" disabled={photos.length === 0} onClick={() => setSelected(new Set(photos.map((p) => p.id)))}>
                  Alles selecteren
                </button>
                {selected.size > 0 ? (
                  <button type="button" className="button ghost small" onClick={() => setSelected(new Set())}>
                    Niets selecteren
                  </button>
                ) : null}
              </div>
            </div>
            {selected.size > 0 ? (
              <div className="bulk-bar" role="region" aria-label="Acties voor de geselecteerde foto's">
                <strong>{selected.size} geselecteerd</strong>
                <button type="button" className="button secondary small" onClick={() => runBulk('Hide', `${selected.size} foto('s) verborgen.`)}>
                  Verbergen
                </button>
                <button type="button" className="button secondary small" onClick={() => runBulk('Show', `${selected.size} foto('s) weer zichtbaar.`)}>
                  Tonen
                </button>
                <span className="bulk-group">
                  <label htmlFor="bulk-doel" className="visually-hidden">
                    Verplaatsen naar galerij
                  </label>
                  <select id="bulk-doel" value={target} onChange={(e) => setTarget(e.target.value)}>
                    <option value="">Verplaatsen naar…</option>
                    {(albums.data ?? [])
                      .filter((a) => a.id !== id)
                      .map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.title}
                        </option>
                      ))}
                  </select>
                  <button
                    type="button"
                    className="button secondary small"
                    disabled={!target}
                    onClick={() => runBulk('Move', `${selected.size} foto('s) verplaatst.`, { targetAlbumId: target })}
                  >
                    Verplaatsen
                  </button>
                </span>
                <span className="bulk-group">
                  <label htmlFor="bulk-fotograaf" className="visually-hidden">
                    Fotograaf
                  </label>
                  <input id="bulk-fotograaf" placeholder="Fotograaf" value={photographer} onChange={(e) => setPhotographer(e.target.value)} />
                  <button type="button" className="button secondary small" onClick={() => runBulk('SetPhotographer', 'Fotograaf ingesteld.', { photographer })}>
                    Fotograaf instellen
                  </button>
                </span>
                <button type="button" className="button danger small" onClick={() => setConfirmBulkDelete(true)}>
                  Verwijderen
                </button>
              </div>
            ) : null}
            <ProblemAlert error={bulk.error} />
            <ul className="photo-grid">
              {photos.map((photo, index) => {
                const checked = selected.has(photo.id);
                const classes = [photo.hidden ? 'hidden-photo' : '', checked ? 'selected' : ''].filter(Boolean).join(' ');
                return (
                  <li key={photo.id} className={classes || undefined}>
                    <label className="photo-select">
                      <input type="checkbox" checked={checked} onChange={() => toggle(photo.id)} />
                      <span className="visually-hidden">Foto {index + 1} selecteren</span>
                      {photo.thumbnailUrl ? <img src={photo.thumbnailUrl} alt={photo.caption ?? `Foto ${index + 1}`} /> : <div className="placeholder" />}
                    </label>
                    <p>
                      {photo.processingStatus !== 'Ready' ? <span className="badge">{processingLabels[photo.processingStatus]}</span> : null}
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
                        disabled={photo.processingStatus !== 'Ready'}
                        onClick={async () => {
                          await api.PUT('/api/v1/admin/photo-albums/{id}/cover/{photoId}', { params: { path: { id, photoId: photo.id } } });
                          await album.refetch();
                          setMessage('Omslagfoto ingesteld.');
                        }}
                      >
                        Omslag <span className="visually-hidden">foto {index + 1}</span>
                      </button>
                    </div>
                  </li>
                );
              })}
            </ul>
            {photos.length === 0 ? <p className="muted">Nog geen foto's in deze galerij.</p> : null}
          </section>
        </>
      )}
      <ConfirmDialog
        open={confirmDelete}
        title="Galerij verwijderen?"
        message="De galerij en alle foto's worden definitief verwijderd."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/fotos' }) })}
      />
      <ConfirmDialog
        open={confirmBulkDelete}
        title={`${selected.size} foto('s) verwijderen?`}
        message="De geselecteerde foto's worden definitief verwijderd. Wil je ze alleen van de site halen, kies dan Verbergen."
        confirmLabel="Verwijderen"
        busy={bulk.isPending}
        onCancel={() => setConfirmBulkDelete(false)}
        onConfirm={() => {
          setConfirmBulkDelete(false);
          runBulk('Delete', `${selected.size} foto('s) verwijderd.`);
        }}
      />
    </>
  );
}
