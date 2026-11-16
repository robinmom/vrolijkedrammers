import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useAdminEvents,
  useApiMutation,
  useMeeting,
  useMeetings,
  type MeetingAudience,
  type MeetingCreateRequest,
  type MeetingRequest,
} from '../api/hooks';
import { upload } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime, fromLocalInput, toLocalInput } from '../format';

/** Voor wie de vergadering is; bepaalt de doelgroep van het agenda-item en wie de agenda per mail krijgt. */
export const audienceLabels: Record<MeetingAudience, string> = {
  Board: 'Bestuur',
  CouncilOfEleven: 'Raad van Elf',
  Both: 'Bestuur en Raad van Elf',
};
const audiences = Object.keys(audienceLabels) as MeetingAudience[];
const senderLabels = { Secretary: 'Secretaris', Chairman: 'Voorzitter' } as const;

/**
 * Vergaderingen (2026-11-16): alle vergaderingen van het bestuur en de raad van elf bij elkaar. Elke vergadering hoort
 * bij een agenda-item; daar staan de datum, de tijd en de locatie, en daar hangen de agenda en de notulen als bijlage.
 */
export function MeetingsPage() {
  const [audience, setAudience] = useState<MeetingAudience | null>(null);
  const meetings = useMeetings(audience);
  const rows = meetings.data ?? [];
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Vergaderingen</h1>
          <p className="muted">
            Vergaderingen van het bestuur en de raad van elf. Je maakt hier de agenda, bewaart de notulen en stuurt de agenda in de huisstijl rond.
          </p>
        </div>
        <Link to="/vergaderingen/$id" params={{ id: 'nieuw' }} className="button">
          Nieuwe vergadering
        </Link>
      </div>
      <ProblemAlert error={meetings.error} />
      <div className="chips" role="group" aria-label="Soort vergadering">
        <button type="button" className={audience === null ? 'chip active' : 'chip'} aria-pressed={audience === null} onClick={() => setAudience(null)}>
          Alle
        </button>
        {audiences.map((a) => (
          <button key={a} type="button" className={audience === a ? 'chip active' : 'chip'} aria-pressed={audience === a} onClick={() => setAudience(a)}>
            {audienceLabels[a]}
          </button>
        ))}
      </div>
      <div className="card">
        <table className="table">
          <caption className="visually-hidden">Vergaderingen</caption>
          <thead>
            <tr>
              <th scope="col">Wanneer</th>
              <th scope="col">Vergadering</th>
              <th scope="col">Voor wie</th>
              <th scope="col">Agenda</th>
              <th scope="col">Documenten</th>
              <th scope="col">Agenda verstuurd</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((m) => (
              <tr key={m.id}>
                <td>{formatDateTime(m.startAt)}</td>
                <td>
                  <Link to="/vergaderingen/$id" params={{ id: m.id }}>
                    {m.title}
                  </Link>
                  {m.locationName ? <span className="muted"> · {m.locationName}</span> : null}
                </td>
                <td>{audienceLabels[m.audience ?? 'Board']}</td>
                <td>
                  {m.agendaItems} {m.agendaItems === 1 ? 'punt' : 'punten'}
                </td>
                <td>{m.documents}</td>
                <td>
                  {m.agendaSentAt ? (
                    <span className="badge ok">
                      {formatDateTime(m.agendaSentAt)} · {m.agendaRecipientCount}
                    </span>
                  ) : (
                    <span className="badge">Nog niet</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {meetings.data && rows.length === 0 ? <p className="muted">Nog geen vergaderingen{audience ? ' van deze soort' : ''}.</p> : null}
      </div>
    </>
  );
}

/** Een nieuwe vergadering: met een nieuw agenda-item, of bij een agenda-item dat al in de agenda staat. */
function NewMeeting() {
  const api = useApi();
  const navigate = useNavigate();
  const [link, setLink] = useState(false);
  // De agenda-items pas ophalen als je er echt een wilt koppelen: dat vraagt het recht event.manage.
  const events = useAdminEvents(true, link);
  const [form, setForm] = useState<MeetingCreateRequest>(() => {
    const start = new Date();
    start.setHours(20, 0, 0, 0);
    const end = new Date(start);
    end.setHours(22, 0, 0, 0);
    return { audience: 'Board', eventId: null, title: 'Bestuursvergadering', startAt: start.toISOString(), endAt: end.toISOString(), locationName: null };
  });
  const create = useApiMutation(async (body: MeetingCreateRequest) => (await api.POST('/api/v1/admin/meetings', { body })).data?.id, [['meetings']]);

  function submit(event: FormEvent) {
    event.preventDefault();
    const body: MeetingCreateRequest = link
      ? { audience: form.audience, eventId: form.eventId, title: null, startAt: null, endAt: null, locationName: null }
      : { ...form, eventId: null };
    create.mutate(body, {
      onSuccess: (id) => {
        if (typeof id === 'string') void navigate({ to: '/vergaderingen/$id', params: { id } });
      },
    });
  }

  return (
    <form className="card" onSubmit={submit}>
      <div className="form-grid">
        <div className="field">
          <label htmlFor="soort">Voor wie</label>
          <select
            id="soort"
            value={form.audience}
            onChange={(e) => {
              const audience = e.target.value as MeetingAudience;
              const titles: Record<MeetingAudience, string> = {
                Board: 'Bestuursvergadering',
                CouncilOfEleven: 'Vergadering Raad van Elf',
                Both: 'Gezamenlijke vergadering',
              };
              setForm({ ...form, audience, title: titles[audience] });
            }}
          >
            {audiences.map((a) => (
              <option key={a} value={a}>
                {audienceLabels[a]}
              </option>
            ))}
          </select>
        </div>
      </div>
      <Checkbox
        label="Koppel aan een agenda-item dat al in de agenda staat"
        checked={link}
        onChange={(e) => setLink(e.target.checked)}
      />
      {link ? (
        <div className="field">
          <label htmlFor="agenda-item">Agenda-item</label>
          <select id="agenda-item" required value={form.eventId ?? ''} onChange={(e) => setForm({ ...form, eventId: e.target.value || null })}>
            <option value="">Kies een agenda-item…</option>
            {(events.data ?? []).map((e) => (
              <option key={e.id} value={e.id}>
                {formatDateTime(e.startAt)} · {e.title}
              </option>
            ))}
          </select>
          <p className="muted">
            De datum, tijd en locatie komen uit het agenda-item. Hoort er al een vergadering bij, dan zegt het portal dat bij het opslaan.
          </p>
        </div>
      ) : (
        <>
          <div className="form-grid">
            <Field label="Titel" required maxLength={200} value={form.title ?? ''} onChange={(e) => setForm({ ...form, title: e.target.value })} />
            <Field
              label="Begint"
              type="datetime-local"
              required
              value={toLocalInput(form.startAt)}
              onChange={(e) => setForm({ ...form, startAt: fromLocalInput(e.target.value) })}
            />
            <Field
              label="Eindigt"
              type="datetime-local"
              value={toLocalInput(form.endAt)}
              onChange={(e) => setForm({ ...form, endAt: fromLocalInput(e.target.value) })}
            />
            <Field label="Locatie" maxLength={200} value={form.locationName ?? ''} onChange={(e) => setForm({ ...form, locationName: e.target.value || null })} />
          </div>
          <p className="muted">
            Er komt een agenda-item bij in de agenda, alleen zichtbaar voor {audienceLabels[form.audience ?? 'Board'].toLowerCase()} — dus niet op de website.
          </p>
        </>
      )}
      <ProblemAlert error={create.error} />
      <div className="actions">
        <button type="submit" className="button" disabled={create.isPending}>
          Vergadering maken
        </button>
      </div>
    </form>
  );
}

type AgendaItem = { title: string; note: string | null };

/** De agendapunten: toevoegen, verwijderen en verschuiven; de nummering in de mail volgt deze volgorde. */
function AgendaEditor({ id, items, onSaved }: { id: string; items: AgendaItem[]; onSaved: (message: string) => void }) {
  const api = useApi();
  const [rows, setRows] = useState<AgendaItem[]>(items);
  useEffect(() => setRows(items), [items]);
  const save = useApiMutation(
    (body: AgendaItem[]) => api.PUT('/api/v1/admin/meetings/{id}/agenda', { params: { path: { id } }, body: { items: body } }),
    [['meeting', id], ['meetings']],
  );
  const copy = useApiMutation(() => api.POST('/api/v1/admin/meetings/{id}/agenda/copy-previous', { params: { path: { id } } }), [['meeting', id]]);

  function move(index: number, direction: -1 | 1) {
    const next = [...rows];
    [next[index], next[index + direction]] = [next[index + direction]!, next[index]!];
    setRows(next);
  }

  return (
    <section className="card" aria-labelledby="agenda-kop">
      <div className="card-header">
        <h2 id="agenda-kop">Agenda ({rows.length})</h2>
        <button
          type="button"
          className="button ghost small"
          disabled={copy.isPending}
          onClick={() => copy.mutate(undefined, { onSuccess: () => onSaved('Agenda van de vorige vergadering overgenomen.') })}
        >
          Overnemen van vorige vergadering
        </button>
      </div>
      <ol className="agenda-items">
        {rows.map((row, index) => (
          <li key={index}>
            <div className="form-grid">
              <Field
                label={`Punt ${index + 1}`}
                required
                maxLength={200}
                value={row.title}
                onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, title: e.target.value } : r)))}
              />
              <Field
                label={`Toelichting ${index + 1}`}
                maxLength={1000}
                value={row.note ?? ''}
                onChange={(e) => setRows(rows.map((r, i) => (i === index ? { ...r, note: e.target.value || null } : r)))}
              />
            </div>
            <div className="actions">
              <button type="button" className="button secondary small" disabled={index === 0} onClick={() => move(index, -1)}>
                ↑ <span className="visually-hidden">Punt {index + 1} omhoog</span>
              </button>
              <button type="button" className="button secondary small" disabled={index === rows.length - 1} onClick={() => move(index, 1)}>
                ↓ <span className="visually-hidden">Punt {index + 1} omlaag</span>
              </button>
              <button type="button" className="button secondary small" onClick={() => setRows(rows.filter((_, i) => i !== index))}>
                Verwijderen <span className="visually-hidden">punt {index + 1}</span>
              </button>
            </div>
          </li>
        ))}
      </ol>
      {rows.length === 0 ? <p className="muted">Nog geen agendapunten.</p> : null}
      <ProblemAlert error={save.error ?? copy.error} />
      <div className="actions">
        <button type="button" className="button secondary" onClick={() => setRows([...rows, { title: '', note: null }])}>
          Punt toevoegen
        </button>
        <button
          type="button"
          className="button"
          disabled={save.isPending || rows.some((r) => r.title.trim().length === 0)}
          onClick={() => save.mutate(rows, { onSuccess: () => onSaved('Agenda opgeslagen.') })}
        >
          Agenda opslaan
        </button>
      </div>
    </section>
  );
}

/** Eén vergadering: de gegevens, de agenda, de documenten en het versturen. */
export function MeetingPage() {
  const { id } = useParams({ from: '/vergaderingen/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const auth = useAuth();
  const navigate = useNavigate();
  const meeting = useMeeting(isNew ? null : id);
  const [form, setForm] = useState<MeetingRequest | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [fileError, setFileError] = useState<unknown>(null);
  const [send, setSend] = useState<Set<string>>(new Set());
  const [extra, setExtra] = useState('');
  const [preview, setPreview] = useState<{ subject: string; previewUrl: string; recipients: number } | null>(null);
  const [confirm, setConfirm] = useState<'send' | 'delete' | null>(null);
  const data = meeting.data;

  useEffect(() => {
    if (!data) return;
    const m = data.meeting;
    setForm({
      audience: m.audience,
      title: m.title,
      startAt: m.startAt,
      endAt: m.endAt,
      locationName: m.locationName,
      intro: m.intro,
      sender: m.sender,
    });
    setSend(new Set((data.documents ?? []).map((d) => d.id)));
  }, [data]);

  const save = useApiMutation((body: MeetingRequest) => api.PUT('/api/v1/admin/meetings/{id}', { params: { path: { id } }, body }), [
    ['meeting', id],
    ['meetings'],
  ]);
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/meetings/{id}', { params: { path: { id } } }), [['meetings']]);
  const body = () => ({ documentIds: [...send], extraEmails: addresses(extra) });
  const makePreview = useApiMutation(
    async () => (await api.POST('/api/v1/admin/meetings/{id}/agenda/preview', { params: { path: { id } }, body: body() })).data,
    [],
  );
  const sendAgenda = useApiMutation(
    async () => (await api.POST('/api/v1/admin/meetings/{id}/agenda/send', { params: { path: { id } }, body: body() })).data,
    [['meeting', id], ['meetings']],
  );

  async function uploadDocument(file: File) {
    setFileError(null);
    const data = new FormData();
    data.append('file', file);
    try {
      await upload(auth, 'POST', `/api/v1/admin/meetings/${id}/documents`, data);
      await meeting.refetch();
      setMessage('Document toegevoegd.');
    } catch (error) {
      setFileError(error);
    }
  }

  if (isNew) {
    return (
      <>
        <p>
          <Link to="/vergaderingen">← Vergaderingen</Link>
        </p>
        <h1>Nieuwe vergadering</h1>
        <NewMeeting />
      </>
    );
  }

  const documents = data?.documents ?? [];
  const audience = data?.audience;
  return (
    <>
      <p>
        <Link to="/vergaderingen">← Vergaderingen</Link>
      </p>
      <h1>{form?.title || 'Vergadering'}</h1>
      <SuccessMessage message={message} />
      <ProblemAlert error={meeting.error} />
      {form && data ? (
        <>
          <form
            className="card"
            onSubmit={(event) => {
              event.preventDefault();
              save.mutate(form, { onSuccess: () => setMessage('Vergadering opgeslagen.') });
            }}
          >
            <div className="form-grid">
              <div className="field">
                <label htmlFor="soort">Voor wie</label>
                <select id="soort" value={form.audience} onChange={(e) => setForm({ ...form, audience: e.target.value as MeetingAudience })}>
                  {audiences.map((a) => (
                    <option key={a} value={a}>
                      {audienceLabels[a]}
                    </option>
                  ))}
                </select>
              </div>
              <Field label="Titel" required maxLength={200} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
              <Field
                label="Begint"
                type="datetime-local"
                required
                value={toLocalInput(form.startAt)}
                onChange={(e) => setForm({ ...form, startAt: fromLocalInput(e.target.value) ?? form.startAt })}
              />
              <Field
                label="Eindigt"
                type="datetime-local"
                value={toLocalInput(form.endAt)}
                onChange={(e) => setForm({ ...form, endAt: fromLocalInput(e.target.value) })}
              />
              <Field
                label="Locatie"
                maxLength={200}
                value={form.locationName ?? ''}
                onChange={(e) => setForm({ ...form, locationName: e.target.value || null })}
              />
              <div className="field">
                <label htmlFor="afzender">Namens</label>
                <select id="afzender" value={form.sender} onChange={(e) => setForm({ ...form, sender: e.target.value as MeetingRequest['sender'] })}>
                  <option value="Secretary">{senderLabels.Secretary}</option>
                  <option value="Chairman">{senderLabels.Chairman}</option>
                </select>
              </div>
            </div>
            <Field
              label="Begeleidende tekst in de mail"
              value={form.intro ?? ''}
              maxLength={2000}
              onChange={(e) => setForm({ ...form, intro: e.target.value || null })}
            />
            <p className="muted">
              Titel, tijd en locatie staan in het{' '}
              <Link to="/agenda/$id" params={{ id: data.meeting.eventId }}>
                agenda-item
              </Link>
              , dat alleen voor {audienceLabels[form.audience ?? 'Board'].toLowerCase()} zichtbaar is. Laat je de begeleidende tekst leeg, dan komt er een
              standaardregel in de mail.
            </p>
            <ProblemAlert error={save.error ?? remove.error} />
            <div className="actions">
              <button type="button" className="button danger" onClick={() => setConfirm('delete')}>
                Vergadering verwijderen
              </button>
              <button type="submit" className="button" disabled={save.isPending}>
                Opslaan
              </button>
            </div>
          </form>

          <AgendaEditor id={id} items={data.meeting.agendaItems.map((a) => ({ title: a.title, note: a.note }))} onSaved={setMessage} />

          <section className="card" aria-labelledby="documenten-kop">
            <h2 id="documenten-kop">Agenda en notulen</h2>
            <p className="muted">
              Deze documenten horen bij het agenda-item, dus {audienceLabels[form.audience ?? 'Board'].toLowerCase()} kan ze ook in de app bij de activiteit
              openen. Vink aan wat met de agendamail meegaat.
            </p>
            <ul className="plain">
              {documents.map((d) => (
                <li key={d.id}>
                  <Checkbox
                    label={
                      <>
                        <a href={d.url} target="_blank" rel="noreferrer">
                          {d.fileName}
                        </a>{' '}
                        <span className="muted">({Math.max(1, Math.round(d.sizeBytes / 1024))} kB)</span>
                      </>
                    }
                    checked={send.has(d.id)}
                    onChange={(e) =>
                      setSend((current) => {
                        const next = new Set(current);
                        if (e.target.checked) next.add(d.id);
                        else next.delete(d.id);
                        return next;
                      })
                    }
                  />
                  <button
                    type="button"
                    className="button secondary small"
                    onClick={async () => {
                      await api.DELETE('/api/v1/admin/meetings/{id}/documents/{documentId}', { params: { path: { id, documentId: d.id } } });
                      await meeting.refetch();
                      setMessage('Document verwijderd.');
                    }}
                  >
                    Verwijderen <span className="visually-hidden">{d.fileName}</span>
                  </button>
                </li>
              ))}
            </ul>
            {documents.length === 0 ? <p className="muted">Nog geen agenda of notulen toegevoegd.</p> : null}
            <div className="field">
              <label htmlFor="document">Document toevoegen (PDF, JPEG of PNG, max. 10 MB)</label>
              <input
                id="document"
                type="file"
                accept="application/pdf,image/jpeg,image/png"
                onChange={(e) => {
                  if (e.target.files?.[0]) void uploadDocument(e.target.files[0]);
                  e.target.value = '';
                }}
              />
            </div>
            <ProblemAlert error={fileError} />
          </section>

          <section className="card" aria-labelledby="versturen-kop">
            <h2 id="versturen-kop">Agenda versturen</h2>
            {data.meeting.agendaSentAt ? (
              <p>
                <span className="badge ok">
                  Verstuurd op {formatDateTime(data.meeting.agendaSentAt)} naar {data.meeting.agendaRecipientCount} ontvangers
                </span>
              </p>
            ) : null}
            <p>
              De agenda gaat in de huisstijl naar <strong>{audience?.recipients.length ?? 0} ontvangers</strong> met de rol{' '}
              {audienceLabels[form.audience ?? 'Board']}, namens de {senderLabels[form.sender ?? 'Secretary'].toLowerCase()}.
            </p>
            {audience && audience.recipients.length > 0 ? (
              <details>
                <summary>Wie krijgt de agenda?</summary>
                <ul className="plain">
                  {audience.recipients.map((r) => (
                    <li key={r.email}>
                      {r.name} <span className="muted">· {r.email}</span>
                    </li>
                  ))}
                </ul>
              </details>
            ) : null}
            {audience && audience.withoutEmail.length > 0 ? (
              <p className="alert alert-warning" role="status">
                Zonder e-mailadres, dus zonder agenda: {audience.withoutEmail.join(', ')}. Vul hun adres hieronder in, of zet het in hun account.
              </p>
            ) : null}
            <div className="field">
              <label htmlFor="extra">Extra adressen (één per regel, bijvoorbeeld iemand zonder account)</label>
              <textarea id="extra" rows={3} value={extra} onChange={(e) => setExtra(e.target.value)} />
            </div>
            <ProblemAlert error={makePreview.error ?? sendAgenda.error} />
            <div className="actions">
              <button
                type="button"
                className="button secondary"
                disabled={makePreview.isPending}
                onClick={() =>
                  makePreview.mutate(undefined, {
                    onSuccess: (result) => {
                      const p = result as { subject: string; previewUrl: string; recipients: number } | undefined;
                      if (p) setPreview(p);
                    },
                  })
                }
              >
                Voorbeeld
              </button>
              <button type="button" className="button" disabled={data.meeting.agendaItems.length === 0} onClick={() => setConfirm('send')}>
                Agenda versturen
              </button>
            </div>
            {data.meeting.agendaItems.length === 0 ? <p className="muted">Zet eerst agendapunten op de agenda.</p> : null}
            {preview ? (
              <>
                <p className="muted">
                  Onderwerp: <strong>{preview.subject}</strong>
                  <br />
                  {preview.recipients} ontvangers
                </p>
                {/* Eigen adres met een eigen CSP: met srcDoc zou de CSP van het portal de opmaak van de mail tegenhouden. */}
                <iframe title="Voorbeeld van de agendamail" className="mailing-frame" sandbox="" src={preview.previewUrl} />
              </>
            ) : null}
          </section>
        </>
      ) : null}
      <ConfirmDialog
        open={confirm === 'send'}
        title="Agenda versturen?"
        message={`De agenda gaat naar ${audience?.recipients.length ?? 0} ontvangers${send.size > 0 ? ` met ${send.size} bijlage(n)` : ''}. Dit kan niet ongedaan worden gemaakt.`}
        confirmLabel="Versturen"
        busy={sendAgenda.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() =>
          sendAgenda.mutate(undefined, {
            onSuccess: (result) => {
              const sent = (result as { recipients: number } | undefined)?.recipients ?? 0;
              setMessage(`Agenda verstuurd naar ${sent} ontvangers.`);
            },
            onSettled: () => setConfirm(null),
          })
        }
      />
      <ConfirmDialog
        open={confirm === 'delete'}
        title="Vergadering verwijderen?"
        message="De agendapunten verdwijnen. Het agenda-item met de agenda en de notulen blijft in de agenda staan."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/vergaderingen' }) })}
      />
    </>
  );
}

/** Adressen uit het tekstvak: per regel, komma of puntkomma. */
function addresses(value: string): string[] {
  return value
    .split(/[\n,;]/)
    .map((a) => a.trim())
    .filter((a) => a.length > 0);
}
