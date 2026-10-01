import { Link } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe } from '../api/hooks';
import { useJubilees, type Jubilarian } from '../api/jubilees';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

function JubileeTable({ title, rows }: { title: string; rows: Jubilarian[] }) {
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
          {canExport ? (
            <button type="button" className="button secondary" onClick={() => void exportExcel()} disabled={!r}>
              Exporteren (Excel)
            </button>
          ) : null}
        </div>
      </div>
      <ProblemAlert error={exportError ?? report.error} />
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
                <JubileeTable title={`${g.years} jaar lid`} rows={g.rows} />
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

          {canConfigure ? <MilestonesCard current={r.milestones} /> : null}
        </>
      ) : report.error ? null : (
        <p>Laden…</p>
      )}
    </>
  );
}
