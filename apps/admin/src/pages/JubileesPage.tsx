import { Link } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe } from '../api/hooks';
import { useJubilees, type Jubilarian, type JubileeInvitationResult } from '../api/jubilees';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { JubileeInvitationCard } from '../components/JubileeInvitationCard';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

function JubileeTable({
  title,
  rows,
  onInvite,
  busy,
}: {
  title: string;
  rows: Jubilarian[];
  onInvite: ((j: Jubilarian) => void) | null;
  busy: boolean;
}) {
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label={title}>
      <table className="table compact">
        <caption className="visually-hidden">{title}</caption>
        <thead>
          <tr>
            <th scope="col">Lidnummer</th>
            <th scope="col">Naam</th>
            <th scope="col">Inschrijfjaar</th>
            <th scope="col">Opmerking</th>
            <th scope="col">Uitnodiging</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((j) => (
            <tr key={j.memberId}>
              <td>{j.memberNumber}</td>
              <td>
                <Link to="/leden/$id" params={{ id: j.memberId }}>
                  {j.fullName}
                </Link>
              </td>
              <td>
                {j.joinYear ?? '—'}
                {j.joinYearOverride !== null && j.joinYearOverride !== undefined ? (
                  <span className="badge" title="Het jaar waarvanaf het jubileum telt is aangepast">
                    telt vanaf {j.joinYearOverride}
                  </span>
                ) : null}
              </td>
              <td>{j.note ?? ''}</td>
              <td>
                {j.invitedAt ? (
                  <span className="badge">uitgenodigd {formatDate(j.invitedAt.slice(0, 10))}</span>
                ) : !j.hasEmail ? (
                  <span className="muted">geen e-mailadres</span>
                ) : onInvite ? (
                  <button
                    type="button"
                    className="button ghost small"
                    disabled={busy}
                    aria-label={`${j.fullName} uitnodigen`}
                    onClick={() => onInvite(j)}
                  >
                    Uitnodigen
                  </button>
                ) : (
                  '—'
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MilestonesCard({ current }: { current: number[] }) {
  const api = useApi();
  const [value, setValue] = useState(current.join(', '));
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => setValue(current.join(', ')), [current]);

  const save = useApiMutation(
    (milestones: number[]) => api.PUT('/api/v1/admin/jubilees/settings', { body: { milestones } }),
    [['jubilees']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    const milestones = value
      .split(/[\s,;]+/)
      .filter(Boolean)
      .map(Number);
    save.mutate(milestones, { onSuccess: () => setMessage('Jubilea opgeslagen.') });
  }

  return (
    <section className="card" aria-labelledby="jubilea-instellen">
      <h2 id="jubilea-instellen">Jubilea instellen</h2>
      <form onSubmit={submit}>
        <Field
          label="Jubilea (aantal jaren lid)"
          hint="Gescheiden door komma's, bijvoorbeeld 11, 22, 33, 44, 55, 66, 77"
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        <ProblemAlert error={save.error} />
        <SuccessMessage message={message} />
        <div className="actions">
          <button type="submit" className="button secondary" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </section>
  );
}

/**
 * Jubilarissen per carnavalsjaar (fase 20). Het jubileum telt in het jaar waarin carnaval valt; alleen actieve leden.
 * Het jaar waarvanaf iemands jubileum telt, pas je aan op de pagina van het lid.
 */
export function JubileesPage() {
  const api = useApi();
  const me = useMe();
  const [yearId, setYearId] = useState<number | null>(null);
  const report = useJubilees(yearId);
  const [exportError, setExportError] = useState<unknown>(null);
  const permissions = me.data?.permissions ?? [];
  const canExport = permissions.includes('member.export');
  const canConfigure = permissions.includes('config.manage');
  const canEdit = permissions.includes('member.update');
  const [confirmAll, setConfirmAll] = useState(false);
  const [inviteMessage, setInviteMessage] = useState<string | null>(null);

  const invite = useApiMutation(
    async (memberIds: string[] | null) => {
      const { data } = await api.POST('/api/v1/admin/jubilees/invitations', {
        body: { carnivalYearId: report.data?.carnivalYearId ?? null, memberIds },
      });
      return data;
    },
    [['jubilees']],
  );

  function runInvite(memberIds: string[] | null) {
    setInviteMessage(null);
    invite.mutate(memberIds, {
      onSuccess: (data) => {
        setConfirmAll(false);
        const result = data as JubileeInvitationResult | undefined;
        if (!result) return;
        const parts = [`${result.invited} ${result.invited === 1 ? 'uitnodiging' : 'uitnodigingen'} verstuurd`];
        if (result.alreadyInvited > 0) parts.push(`${result.alreadyInvited} al eerder uitgenodigd`);
        if (result.withoutEmail > 0) parts.push(`${result.withoutEmail} zonder e-mailadres overgeslagen`);
        setInviteMessage(`${parts.join(', ')}.`);
      },
    });
  }

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/jubilees/export', {
        params: { query: { carnivalYearId: report.data?.carnivalYearId } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `jubilarissen-${(report.data?.carnivalYearName ?? '').replace('/', '-')}.xlsx`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  const r = report.data;
  const groups = r
    ? [...r.milestones]
        .sort((a, b) => b - a)
        .map((years) => ({ years, rows: r.jubilarians.filter((j) => j.years === years) }))
        .filter((g) => g.rows.length > 0)
    : [];
  const pending = r ? r.jubilarians.filter((j) => !j.invitedAt && j.hasEmail).length : 0;
  const first = r?.jubilarians[0];
  const example = {
    voornaam: first ? first.fullName.split(' ')[0]! : 'Piet',
    naam: first?.fullName ?? 'Piet van der Berg',
    jaren: first?.years ?? 11,
    carnavalsjaar: r?.carnivalYearName ?? '',
  };

  return (
    <>
      <div className="page-header">
        <h1>Jubilarissen</h1>
        <div className="actions">
          {r ? (
            <select
              aria-label="Carnavalsjaar"
              value={r.carnivalYearId}
              onChange={(e) => setYearId(Number(e.target.value))}
            >
              {r.carnivalYears.map((y) => (
                <option key={y.id} value={y.id}>
                  {y.name}
                  {y.active ? ' (actief)' : ''}
                </option>
              ))}
            </select>
          ) : null}
          {canEdit && r ? (
            <button
              type="button"
              className="button"
              disabled={pending === 0 || invite.isPending}
              onClick={() => setConfirmAll(true)}
            >
              Alle jubilarissen uitnodigen ({pending})
            </button>
          ) : null}
          {canExport ? (
            <button type="button" className="button secondary" onClick={() => void exportExcel()} disabled={!r}>
              Exporteren (Excel)
            </button>
          ) : null}
        </div>
      </div>
      <ProblemAlert error={exportError ?? report.error ?? invite.error} />
      <SuccessMessage message={inviteMessage} />
      {r ? (
        <>
          <p>
            Carnaval {r.carnivalYearName} valt in {r.referenceYear}. Jubilaris zijn de actieve leden met inschrijfjaar{' '}
            {[...r.milestones]
              .sort((a, b) => a - b)
              .map((m) => `${r.referenceYear - m} (${m} jaar)`)
              .join(', ')}
            .
          </p>
          {groups.length === 0 ? (
            <section className="card">
              <p className="muted">Geen jubilarissen in dit carnavalsjaar.</p>
            </section>
          ) : (
            groups.map((g) => (
              <section key={g.years} className="card" aria-labelledby={`jubileum-${g.years}`}>
                <h2 id={`jubileum-${g.years}`}>
                  {g.years} jaar lid <span className="muted">({g.rows.length})</span>
                </h2>
                <JubileeTable
                  title={`${g.years} jaar lid`}
                  rows={g.rows}
                  busy={invite.isPending}
                  onInvite={canEdit ? (j) => runInvite([j.memberId]) : null}
                />
              </section>
            ))
          )}

          <section className="card" aria-labelledby="zonder-inschrijfjaar">
            <h2 id="zonder-inschrijfjaar">
              Zonder inschrijfjaar <span className="muted">({r.withoutJoinYear.length})</span>
            </h2>
            <p className="card-hint">
              Actieve leden zonder inschrijfjaar tellen niet mee. Vul het inschrijfjaar aan in e-Boekhouden of bij het
              lid.
            </p>
            {r.withoutJoinYear.length === 0 ? (
              <p className="muted">Van alle actieve leden is het inschrijfjaar bekend.</p>
            ) : (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Leden zonder inschrijfjaar">
                <table className="table compact">
                  <caption className="visually-hidden">Leden zonder inschrijfjaar</caption>
                  <thead>
                    <tr>
                      <th scope="col">Lidnummer</th>
                      <th scope="col">Naam</th>
                      <th scope="col">Plaats</th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.withoutJoinYear.map((m) => (
                      <tr key={m.memberId}>
                        <td>{m.memberNumber}</td>
                        <td>
                          <Link to="/leden/$id" params={{ id: m.memberId }}>
                            {m.fullName}
                          </Link>
                        </td>
                        <td>{m.city ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <JubileeInvitationCard canEdit={canEdit} example={example} />

          {canConfigure ? <MilestonesCard current={r.milestones} /> : null}

          <ConfirmDialog
            open={confirmAll}
            title="Alle jubilarissen uitnodigen?"
            message={`Er gaat een uitnodiging per e-mail naar ${pending} ${pending === 1 ? 'jubilaris' : 'jubilarissen'} van ${r.carnivalYearName}. Wie al is uitgenodigd of geen e-mailadres heeft, wordt overgeslagen.`}
            confirmLabel="Uitnodigingen versturen"
            busy={invite.isPending}
            onConfirm={() => runInvite(null)}
            onCancel={() => setConfirmAll(false)}
          />
        </>
      ) : report.error ? null : (
        <p>Laden…</p>
      )}
    </>
  );
}
