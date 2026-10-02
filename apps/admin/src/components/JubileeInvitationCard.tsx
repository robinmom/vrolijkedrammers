import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

/** Vult de invulvelden zoals de server dat doet, voor het voorbeeld. */
export function fillInvitation(
  text: string,
  values: { voornaam: string; naam: string; jaren: number; carnavalsjaar: string },
) {
  return text
    .replace(/\{voornaam\}/gi, values.voornaam)
    .replace(/\{naam\}/gi, values.naam)
    .replace(/\{jaren\}/gi, String(values.jaren))
    .replace(/\{carnavalsjaar\}/gi, values.carnavalsjaar);
}

/**
 * Tekst van de uitnodiging voor jubilarissen (fase 20b): onderwerp, brief met invulvelden en het adres waar antwoorden
 * heen gaan. Het voorbeeld toont de brief voor de eerste jubilaris.
 */
export function JubileeInvitationCard({
  canEdit,
  example,
}: {
  canEdit: boolean;
  example: { voornaam: string; naam: string; jaren: number; carnavalsjaar: string };
}) {
  const api = useApi();
  const template = useQuery({
    queryKey: ['jubilees', 'invitation-template'],
    queryFn: async () => (await api.GET('/api/v1/admin/jubilees/invitation-template')).data!,
  });
  const [form, setForm] = useState({ subject: '', body: '', replyTo: '' });
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (template.data) {
      setForm({ subject: template.data.subject, body: template.data.body, replyTo: template.data.replyTo });
    }
  }, [template.data]);

  const save = useApiMutation(
    () => api.PUT('/api/v1/admin/jubilees/invitation-template', { body: form }),
    [['jubilees', 'invitation-template']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(undefined, { onSuccess: () => setMessage('Tekst van de uitnodiging opgeslagen.') });
  }

  return (
    <section className="card" aria-labelledby="uitnodiging-tekst">
      <h2 id="uitnodiging-tekst">Tekst van de uitnodiging</h2>
      <p className="card-hint">
        Invulvelden: {(template.data?.placeholders ?? []).join(', ')}. De e-mail komt van het systeemadres; antwoorden
        gaan naar het adres hieronder.
      </p>
      <ProblemAlert error={template.error} />
      <form onSubmit={submit}>
        <fieldset disabled={!canEdit} className="plain-fieldset">
          <legend className="visually-hidden">Uitnodiging</legend>
          <Field
            label="Onderwerp"
            required
            maxLength={200}
            value={form.subject}
            onChange={(e) => setForm({ ...form, subject: e.target.value })}
          />
          <div className="field">
            <label htmlFor="uitnodiging-brief">Brief</label>
            <textarea
              id="uitnodiging-brief"
              required
              maxLength={4000}
              rows={12}
              value={form.body}
              onChange={(e) => setForm({ ...form, body: e.target.value })}
            />
          </div>
          <Field
            label="Antwoorden naar"
            type="email"
            required
            maxLength={254}
            value={form.replyTo}
            onChange={(e) => setForm({ ...form, replyTo: e.target.value })}
          />
        </fieldset>
        <ProblemAlert error={save.error} />
        <SuccessMessage message={message} />
        {canEdit ? (
          <div className="actions">
            <button type="submit" className="button secondary" disabled={save.isPending || !template.data}>
              Tekst opslaan
            </button>
          </div>
        ) : null}
      </form>
      <h3>Voorbeeld voor {example.naam}</h3>
      <div className="letter-preview" aria-label="Voorbeeld van de uitnodiging">
        <p>
          <strong>{fillInvitation(form.subject, example)}</strong>
        </p>
        {fillInvitation(form.body, example)
          .split(/\n\s*\n/)
          .map((paragraph, i) => (
            <p key={i} style={{ whiteSpace: 'pre-line' }}>
              {paragraph.trim()}
            </p>
          ))}
      </div>
    </section>
  );
}
