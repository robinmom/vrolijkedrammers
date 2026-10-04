import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import {
  KIND_LABELS,
  MAILING_KEYS,
  STATUS_LABELS,
  useMailing,
  useMailingLists,
  useMailings,
  type MailingBlock,
  type MailingKind,
  type MailingPreview,
  type MailingRequest,
  type MailingSender,
} from '../api/mailing';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/** Mailing → Mailings (fase 27a): nieuwsbrieven en uitnodigingen, nieuwste eerst. */
export function MailingsPage() {
  const mailings = useMailings();
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Mailings</h1>
          <p className="muted">
            Nieuwsbrieven en uitnodigingen in de huisstijl. Ze gaan vanaf secretaris@vrolijkedrammers.nl.
          </p>
        </div>
        <Link to="/mailing/$id" params={{ id: 'nieuw' }} className="button">
          Nieuwe mailing
        </Link>
      </div>
      <ProblemAlert error={mailings.error} />
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Mailings">
        <table className="table">
          <caption className="visually-hidden">Mailings</caption>
          <thead>
            <tr>
              <th scope="col">Onderwerp</th>
              <th scope="col">Soort</th>
              <th scope="col">Status</th>
              <th scope="col">Verstuurd</th>
              <th scope="col">Laatst gewijzigd</th>
            </tr>
          </thead>
          <tbody>
            {(mailings.data ?? []).map((m) => (
              <tr key={m.id}>
                <td>
                  <Link to="/mailing/$id" params={{ id: m.id }}>
                    {m.subject}
                  </Link>
                </td>
                <td>{KIND_LABELS[m.kind]}</td>
                <td>
                  <span className={m.status === 'Sent' ? 'badge ok' : m.status === 'Sending' ? 'badge warn' : 'badge'}>
                    {STATUS_LABELS[m.status]}
                  </span>
                </td>
                <td>{m.status === 'Draft' ? '—' : `${m.sentCount} van ${m.recipientCount}`}</td>
                <td>{formatDateTime(m.sentAt ?? m.updatedAt)}</td>
              </tr>
            ))}
            {mailings.data?.length === 0 ? (
              <tr>
                <td colSpan={5} className="muted">
                  Nog geen mailings.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </>
  );
}

const BLOCK_LABELS: Record<string, string> = {
  heading: 'Kop',
  text: 'Tekst',
  image: 'Foto',
  button: 'Knop',
  highlight: 'Uitgelicht',
  divider: 'Lijn',
  closing: 'Afsluiting',
};

const block = (type: string, fields: Partial<MailingBlock> = {}): MailingBlock => ({
  type,
  text: null,
  label: null,
  url: null,
  image: null,
  note: null,
  ...fields,
});

const newBlock = (type: string): MailingBlock => {
  switch (type) {
    case 'heading':
    case 'text':
      return block(type, { text: '' });
    case 'closing':
      return block(type, { text: 'Groeten,\nDe Vrolijke Drammers' });
    case 'button':
      return block(type, { label: '', url: 'https://' });
    case 'highlight':
      return block(type, { label: '', text: '' });
    default:
      return block(type);
  }
};

const starter: MailingRequest = {
  kind: 'Newsletter',
  sender: 'Secretary',
  subject: '',
  preheader: null,
  listIds: [],
  blocks: [
    block('heading', { text: 'Nieuws van De Vrolijke Drammers' }),
    block('text', { text: 'Beste {voornaam},\n\n…' }),
    block('closing', { text: 'Groeten,\nDe Vrolijke Drammers' }),
  ],
};

/** De API stuurt ook imageUrl mee (om de foto te tonen); die hoort niet in het verzoek. */
const toRequestBlock = (b: MailingBlock): MailingBlock =>
  block(b.type, { text: b.text, label: b.label, url: b.url, image: b.image, note: b.note });

/**
 * Een mailing opstellen (fase 27a): onderwerp, groepen en blokken links, het voorbeeld zoals een lid hem ziet rechts.
 * Een verstuurde mailing is alleen nog te bekijken (en te kopiëren).
 */
export function MailingEditorPage() {
  const { id } = useParams({ from: '/mailing/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const existing = useMailing(isNew ? null : id);
  const lists = useMailingLists();
  const [form, setForm] = useState<MailingRequest>(starter);
  const [imageUrls, setImageUrls] = useState<Record<number, string | null | undefined>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<'send' | 'delete' | null>(null);
  const [preview, setPreview] = useState<MailingPreview | null>(null);
  const [previewError, setPreviewError] = useState<unknown>(null);
  const [showText, setShowText] = useState(false);
  const readOnly = existing.data !== undefined && existing.data.status !== 'Draft';

  useEffect(() => {
    const m = existing.data;
    if (m) {
      setForm({
        kind: m.kind,
        sender: m.sender,
        subject: m.subject,
        preheader: m.preheader,
        listIds: m.listIds,
        blocks: m.blocks.map(toRequestBlock),
      });
      setImageUrls(Object.fromEntries(m.blocks.map((b, i) => [i, b.imageUrl])));
    }
  }, [existing.data]);

  // Voorbeeld een halve seconde na de laatste wijziging.
  const latest = useRef(0);
  useEffect(() => {
    if (form.blocks.length === 0) return;
    const call = ++latest.current;
    const timer = window.setTimeout(async () => {
      try {
        const result = await api.POST('/api/v1/admin/mailing/preview', {
          body: { ...form, subject: form.subject || 'Onderwerp' },
        });
        if (call === latest.current) {
          setPreview(result.data ?? null);
          setPreviewError(null);
        }
      } catch (e) {
        if (call === latest.current) setPreviewError(e);
      }
    }, 500);
    return () => window.clearTimeout(timer);
  }, [api, form]);

  const save = useApiMutation(
    async (body: MailingRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/mailing/mailings', { body })).data?.id
        : (await api.PUT('/api/v1/admin/mailing/mailings/{id}', { params: { path: { id } }, body }), id),
    MAILING_KEYS,
  );
  type TestResult = Schemas['MailingTestResponse'];
  type SendResult = Schemas['MailingSendResponse'];
  const test = useApiMutation(
    async () => (await api.POST('/api/v1/admin/mailing/mailings/{id}/test', { params: { path: { id } } })).data,
    MAILING_KEYS,
  );
  const send = useApiMutation(
    async () => (await api.POST('/api/v1/admin/mailing/mailings/{id}/send', { params: { path: { id } } })).data,
    MAILING_KEYS,
  );
  const remove = useApiMutation(
    () => api.DELETE('/api/v1/admin/mailing/mailings/{id}', { params: { path: { id } } }),
    MAILING_KEYS,
  );
  const duplicate = useApiMutation(
    async () =>
      (await api.POST('/api/v1/admin/mailing/mailings/{id}/duplicate', { params: { path: { id } } })).data?.id,
    MAILING_KEYS,
  );

  const set = (change: Partial<MailingRequest>) => setForm({ ...form, ...change });
  const setBlock = (index: number, change: Partial<MailingBlock>) =>
    set({ blocks: form.blocks.map((b, i) => (i === index ? { ...b, ...change } : b)) });
  const move = (index: number, by: number) => {
    const blocks = [...form.blocks];
    const [block] = blocks.splice(index, 1);
    blocks.splice(index + by, 0, block!);
    const urls = form.blocks.map((_, i) => imageUrls[i]);
    const [url] = urls.splice(index, 1);
    urls.splice(index + by, 0, url);
    setImageUrls(Object.fromEntries(urls.map((u, i) => [i, u])));
    set({ blocks });
  };
  const removeBlock = (index: number) => {
    setImageUrls(
      Object.fromEntries(
        form.blocks
          .map((_, i) => imageUrls[i])
          .filter((_, i) => i !== index)
          .map((u, i) => [i, u]),
      ),
    );
    set({ blocks: form.blocks.filter((_, i) => i !== index) });
  };
  const toggleList = (listId: string, on: boolean) =>
    set({ listIds: on ? [...(form.listIds ?? []), listId] : (form.listIds ?? []).filter((l) => l !== listId) });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Mailing opgeslagen.');
        if (isNew && typeof newId === 'string') void navigate({ to: '/mailing/$id', params: { id: newId } });
      },
    });
  }

  const recipients = preview?.audience.recipients ?? 0;
  const progress = existing.data?.progress;

  return (
    <>
      <p>
        <Link to="/mailing">← Mailings</Link>
      </p>
      <div className="page-header">
        <div>
          <h1>{isNew ? 'Nieuwe mailing' : form.subject || 'Mailing'}</h1>
          {existing.data ? (
            <p className="muted">
              {KIND_LABELS[existing.data.kind]} · {STATUS_LABELS[existing.data.status]}
              {existing.data.sentAt ? ` · verstuurd ${formatDateTime(existing.data.sentAt)}` : ''}
            </p>
          ) : null}
        </div>
        {readOnly ? (
          <button
            type="button"
            className="button secondary"
            disabled={duplicate.isPending}
            onClick={() =>
              duplicate.mutate(undefined, {
                onSuccess: (copy) => {
                  if (typeof copy === 'string') void navigate({ to: '/mailing/$id', params: { id: copy } });
                },
              })
            }
          >
            Kopie maken
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      {readOnly && progress ? (
        <section className="card" aria-labelledby="voortgang-kop">
          <h2 id="voortgang-kop">Versturen</h2>
          <div
            className="progress"
            role="progressbar"
            aria-label="Verwerkt"
            aria-valuemin={0}
            aria-valuemax={existing.data!.recipientCount}
            aria-valuenow={progress.sent + progress.failed}
          >
            <span
              style={{
                width: `${(100 * (progress.sent + progress.failed)) / Math.max(1, existing.data!.recipientCount)}%`,
              }}
            />
          </div>
          <p>
            {progress.sent} verstuurd, {progress.pending} in de wachtrij
            {progress.failed > 0 ? `, ${progress.failed} mislukt` : ''}. Grote mailings gaan in delen weg (ongeveer 90
            per uur).
          </p>
        </section>
      ) : null}

      <div className="mailing-layout">
        <form className="card" onSubmit={submit} aria-label="Mailing opstellen">
          <fieldset disabled={readOnly} className="plain-fieldset">
            <div className="form-grid">
              <div className="field">
                <label htmlFor="mailing-soort">Soort</label>
                <select
                  id="mailing-soort"
                  value={form.kind}
                  onChange={(e) => set({ kind: e.target.value as MailingKind })}
                >
                  <option value="Newsletter">Nieuwsbrief</option>
                  <option value="Invitation">Uitnodiging</option>
                </select>
              </div>
              <div className="field">
                <label htmlFor="mailing-afzender">Afzender</label>
                <select
                  id="mailing-afzender"
                  value={form.sender ?? 'Secretary'}
                  onChange={(e) => set({ sender: e.target.value as MailingSender })}
                >
                  <option value="Secretary">Secretaris (secretaris@)</option>
                  <option value="Chairman">Voorzitter (voorzitter@)</option>
                </select>
              </div>
              <Field
                label="Onderwerp"
                required
                maxLength={200}
                value={form.subject}
                onChange={(e) => set({ subject: e.target.value })}
              />
            </div>
            <Field
              label="Voorbeeldregel"
              hint="De korte tekst die mailprogramma's naast het onderwerp tonen."
              maxLength={200}
              value={form.preheader ?? ''}
              onChange={(e) => set({ preheader: e.target.value || null })}
            />
            <fieldset className="checkbox-group">
              <legend>Naar</legend>
              {(lists.data ?? []).map((l) => (
                <label key={l.id} className="checkbox">
                  <input
                    type="checkbox"
                    checked={(form.listIds ?? []).includes(l.id)}
                    onChange={(e) => toggleList(l.id, e.target.checked)}
                  />
                  {l.name}
                </label>
              ))}
              {lists.data?.length === 0 ? <p className="muted">Maak eerst een mailinggroep.</p> : null}
              <p className="muted">
                <Link to="/mailing/groepen">Mailinggroepen beheren</Link>
              </p>
            </fieldset>

            <h2>Inhoud</h2>
            <p className="muted">
              Gebruik {'{voornaam}'}, {'{naam}'} en (bij adverteerders) {'{bedrijf}'} voor een persoonlijke aanhef.
              Tekst mag **vet**, *cursief*, lijstjes en [links](https://…) bevatten.
            </p>
            <ol className="block-list">
              {form.blocks.map((block, index) => (
                <li key={index} className="card nested">
                  <div className="block-header">
                    <strong>{BLOCK_LABELS[block.type] ?? block.type}</strong>
                    <span className="block-actions">
                      <button
                        type="button"
                        className="button secondary small"
                        disabled={index === 0}
                        onClick={() => move(index, -1)}
                        aria-label={`Blok ${index + 1} omhoog`}
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        className="button secondary small"
                        disabled={index === form.blocks.length - 1}
                        onClick={() => move(index, 1)}
                        aria-label={`Blok ${index + 1} omlaag`}
                      >
                        ↓
                      </button>
                      <button
                        type="button"
                        className="button danger small"
                        onClick={() => removeBlock(index)}
                        aria-label={`Blok ${index + 1} verwijderen`}
                      >
                        ✕
                      </button>
                    </span>
                  </div>
                  <BlockFields
                    block={block}
                    index={index}
                    imageUrl={imageUrls[index]}
                    onChange={(change) => setBlock(index, change)}
                  />
                </li>
              ))}
            </ol>
            <div className="add-blocks" role="group" aria-label="Blok toevoegen">
              {/* De afsluiting staat altijd onderaan en komt hooguit één keer voor. */}
              {Object.entries(BLOCK_LABELS)
                .filter(([type]) => type !== 'closing' || !form.blocks.some((b) => b.type === 'closing'))
                .map(([type, label]) => (
                  <button
                    key={type}
                    type="button"
                    className="button secondary small"
                    onClick={() => set({ blocks: [...form.blocks, newBlock(type)] })}
                  >
                    + {label}
                  </button>
                ))}
            </div>
          </fieldset>
          <ProblemAlert error={save.error ?? test.error ?? send.error ?? remove.error ?? duplicate.error} />
          {test.data ? (
            <SuccessMessage message={`Testmail verstuurd naar ${(test.data as TestResult).sentTo}.`} />
          ) : null}
          {send.data ? (
            <SuccessMessage
              message={`De mailing gaat naar ${(send.data as SendResult).recipients} ontvangers; de laatste mail gaat rond ${formatDateTime((send.data as SendResult).lastAt)} weg.`}
            />
          ) : null}
          {!readOnly ? (
            <div className="actions">
              {!isNew ? (
                <button type="button" className="button danger" onClick={() => setConfirm('delete')}>
                  Verwijderen
                </button>
              ) : null}
              <button type="submit" className="button secondary" disabled={save.isPending}>
                Opslaan
              </button>
              {!isNew ? (
                <>
                  <button
                    type="button"
                    className="button secondary"
                    disabled={test.isPending}
                    onClick={() => test.mutate(undefined)}
                  >
                    Testmail naar mij
                  </button>
                  <button
                    type="button"
                    className="button"
                    disabled={send.isPending || recipients === 0}
                    onClick={() => setConfirm('send')}
                  >
                    Versturen…
                  </button>
                </>
              ) : null}
            </div>
          ) : null}
          {!isNew && !readOnly ? (
            <p className="muted">Sla eerst op; de testmail en het versturen gebruiken de opgeslagen versie.</p>
          ) : null}
        </form>

        <section className="card mailing-preview" aria-labelledby="voorbeeld-kop">
          <div className="card-header">
            <h2 id="voorbeeld-kop">Voorbeeld</h2>
            <label className="checkbox">
              <input type="checkbox" checked={showText} onChange={(e) => setShowText(e.target.checked)} />
              Platte tekst
            </label>
          </div>
          {preview ? (
            <>
              <p className="muted">
                Onderwerp: <strong>{preview.subject}</strong>
                <br />
                {preview.audience.recipients} ontvangers
                {preview.audience.unsubscribed > 0 ? `, ${preview.audience.unsubscribed} afgemeld` : ''}
                {preview.audience.withoutEmail > 0 ? `, ${preview.audience.withoutEmail} zonder e-mailadres` : ''}
              </p>
              {showText ? (
                <pre className="mailing-text">{preview.plainText}</pre>
              ) : (
                // Eigen adres met een eigen CSP: met srcDoc zou de CSP van het portal de opmaak van de mail tegenhouden.
                <iframe
                  title="Voorbeeld van de mailing"
                  className="mailing-frame"
                  sandbox=""
                  src={preview.previewUrl}
                />
              )}
            </>
          ) : (
            <p className="muted">Het voorbeeld verschijnt zodra de blokken zijn ingevuld.</p>
          )}
          {previewError ? <ProblemAlert error={previewError} /> : null}
        </section>
      </div>

      <ConfirmDialog
        open={confirm === 'send'}
        title="Mailing versturen?"
        message={`De mailing gaat naar ${recipients} ontvangers. Dit kan niet ongedaan worden gemaakt. Grote mailings gaan in delen weg (ongeveer 90 per uur).`}
        confirmLabel="Versturen"
        busy={send.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => send.mutate(undefined, { onSettled: () => setConfirm(null) })}
      />
      <ConfirmDialog
        open={confirm === 'delete'}
        title="Mailing verwijderen?"
        message="Het concept verdwijnt."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirm(null)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/mailing' }) })}
      />
    </>
  );
}

function BlockFields({
  block,
  index,
  imageUrl,
  onChange,
}: {
  block: MailingBlock;
  index: number;
  imageUrl: string | null | undefined;
  onChange: (change: Partial<MailingBlock>) => void;
}) {
  const id = `blok-${index}`;
  switch (block.type) {
    case 'heading':
      return (
        <Field
          label="Kop"
          maxLength={200}
          value={block.text ?? ''}
          onChange={(e) => onChange({ text: e.target.value })}
        />
      );
    case 'text':
      return (
        <div className="field">
          <label htmlFor={`${id}-tekst`}>Tekst</label>
          <textarea
            id={`${id}-tekst`}
            rows={8}
            maxLength={10000}
            value={block.text ?? ''}
            onChange={(e) => onChange({ text: e.target.value })}
          />
        </div>
      );
    case 'image':
      return (
        <>
          <ImagePicker
            label="Foto"
            uploadPath="/api/v1/admin/mailing/images"
            currentUrl={imageUrl}
            onChange={(path) => onChange({ image: path || null })}
          />
          <div className="form-grid">
            <Field
              label="Omschrijving (voor schermlezers)"
              maxLength={200}
              value={block.text ?? ''}
              onChange={(e) => onChange({ text: e.target.value || null })}
            />
            <Field
              label="Link bij de foto (optioneel)"
              type="url"
              maxLength={500}
              value={block.url ?? ''}
              onChange={(e) => onChange({ url: e.target.value || null })}
            />
          </div>
        </>
      );
    case 'button':
      return (
        <div className="form-grid">
          <Field
            label="Knoptekst"
            maxLength={60}
            value={block.label ?? ''}
            onChange={(e) => onChange({ label: e.target.value })}
          />
          <Field
            label="Link"
            type="url"
            maxLength={500}
            value={block.url ?? ''}
            onChange={(e) => onChange({ url: e.target.value })}
          />
        </div>
      );
    case 'highlight':
      return (
        <>
          <div className="form-grid">
            <Field
              label="Label"
              hint="Bijvoorbeeld: Datum"
              maxLength={100}
              value={block.label ?? ''}
              onChange={(e) => onChange({ label: e.target.value || null })}
            />
            <Field
              label="Waarde"
              hint="Bijvoorbeeld: Zaterdag 6 februari 2027"
              maxLength={100}
              value={block.text ?? ''}
              onChange={(e) => onChange({ text: e.target.value })}
            />
          </div>
          <Field
            label="Toelichting (optioneel)"
            maxLength={300}
            value={block.note ?? ''}
            onChange={(e) => onChange({ note: e.target.value || null })}
          />
        </>
      );
    case 'closing':
      return (
        <div className="field">
          <label htmlFor={`${id}-afsluiting`}>Groet</label>
          <textarea
            id={`${id}-afsluiting`}
            rows={4}
            maxLength={2000}
            aria-describedby={`${id}-afsluiting-hint`}
            value={block.text ?? ''}
            onChange={(e) => onChange({ text: e.target.value })}
          />
          <small id={`${id}-afsluiting-hint`} className="muted">
            Staat altijd onderaan de mail, in één blok met de gegevens van de vereniging en de afmeldlink.
          </small>
        </div>
      );
    default:
      return <p className="muted">Een dunne lijn tussen twee onderdelen.</p>;
  }
}
