import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type MemberDetail } from '../api/hooks';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

/**
 * Jubileum van een lid (fase 20): het jaar waarvanaf het jubileum telt aanpassen, bijvoorbeeld als iemand een tijd geen
 * lid was. Leeg = het inschrijfjaar volgen. De sync met e-Boekhouden raakt dit nooit aan.
 */
export function JubileeCard({ member, canEdit }: { member: MemberDetail; canEdit: boolean }) {
  const api = useApi();
  const [year, setYear] = useState(member.jubileeJoinYearOverride?.toString() ?? '');
  const [note, setNote] = useState(member.jubileeNote ?? '');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    setYear(member.jubileeJoinYearOverride?.toString() ?? '');
    setNote(member.jubileeNote ?? '');
  }, [member.jubileeJoinYearOverride, member.jubileeNote]);

  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/jubilees/members/{memberId}', {
        params: { path: { memberId: member.id } },
        body: { joinYearOverride: year ? Number(year) : null, note: year && note.trim() ? note.trim() : null },
      }),
    [['member', member.id], ['jubilees']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(undefined, { onSuccess: () => setMessage('Jubileum opgeslagen.') });
  }

  const base = member.jubileeJoinYearOverride ?? member.joinYear;
  return (
    <section className="card" aria-labelledby="jubileum">
      <h2 id="jubileum">Jubileum</h2>
      <p className="card-hint">
        {base ? `Het jubileum telt vanaf ${base}` : 'Er is nog geen inschrijfjaar bekend'}
        {member.jubileeJoinYearOverride ? ` (aangepast; inschrijfjaar ${member.joinYear ?? 'onbekend'}).` : '.'}
      </p>
      <form onSubmit={submit}>
        <fieldset disabled={!canEdit} className="plain-fieldset">
          <legend className="visually-hidden">Jubileum aanpassen</legend>
          <Field
            label="Jubileum telt vanaf (jaar)"
            hint="Leeg laten om het inschrijfjaar te volgen"
            type="number"
            min={1900}
            max={2100}
            value={year}
            onChange={(e) => setYear(e.target.value)}
          />
          <Field
            label="Reden"
            maxLength={200}
            disabled={!year}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
        </fieldset>
        <ProblemAlert error={save.error} />
        <SuccessMessage message={message} />
        {canEdit ? (
          <div className="actions">
            <button type="submit" className="button secondary" disabled={save.isPending}>
              Jubileum opslaan
            </button>
          </div>
        ) : null}
      </form>
    </section>
  );
}
