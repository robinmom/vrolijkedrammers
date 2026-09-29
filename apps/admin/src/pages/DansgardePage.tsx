import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useDansgarde, useMe } from '../api/hooks';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate } from '../format';

const NO_GROUP = 'geen';

/**
 * Dansgarde → Overzicht (fase 17, Figma 👨‍👧 Ouders & kinderen): alle leden met groep "Dansgarde" in e-Boekhouden (vrij
 * veld 3), met dansgroep, ouders in de app en of de QR klaar is. Deze leden staan ook gewoon onder Leden.
 */
export function DansgardePage() {
  const api = useApi();
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canEdit = permissions.includes('member.update');
  const canNotify = permissions.includes('notification.send');
  const overview = useDansgarde();
  const [filter, setFilter] = useState<string | null>(null);
  const assign = useApiMutation(
    async ({ memberId, groupId }: { memberId: string; groupId: string | null }) =>
      (await api.PUT('/api/v1/admin/dansgarde/{memberId}/group', { params: { path: { memberId } }, body: { groupId } }))
        .data,
    [['dansgarde'], ['dance-groups']],
  );

  const d = overview.data;
  const rows = (d?.members ?? []).filter((m) =>
    filter === null ? true : filter === NO_GROUP ? !m.danceGroup : m.danceGroup?.id === filter,
  );
  const count = (groupId: string) => (d?.members ?? []).filter((m) => m.danceGroup?.id === groupId).length;

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Dansgarde</h1>
          <p className="page-subtitle">
            Alle leden met groep "Dansgarde" in e-Boekhouden (vrij veld 3). Ze staan ook gewoon onder Leden. Hier deel
            je ze in bij een dansgroep en zie je of de ouders gekoppeld zijn.
          </p>
        </div>
      </div>
      <ProblemAlert error={overview.error ?? assign.error} />
      {d ? (
        <>
          <section className="kpis" aria-label="Kerncijfers">
            <div className="kpi">
              <span className="kpi-label">Dansgarde</span>
              <span className="kpi-value">{d.total}</span>
              <span className="kpi-hint">groep Dansgarde in e-Boekhouden</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Zonder dansgroep</span>
              <span className="kpi-value">{d.withoutGroup}</span>
              <span className="kpi-hint">nog indelen</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Zonder ouder in de app</span>
              <span className="kpi-value">{d.withoutGuardian}</span>
              <span className="kpi-hint">
                zie <Link to="/koppelverzoeken">Koppelverzoeken</Link>
              </span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Wordt binnenkort 15</span>
              <span className="kpi-value">{d.turningFifteenSoon}</span>
              <span className="kpi-hint">eigen account mogelijk</span>
            </div>
          </section>

          <section className="card" aria-label="Dansgarde-leden">
            <div className="toolbar">
              <div className="chips" role="group" aria-label="Filter op dansgroep">
                <button
                  type="button"
                  className={`chip${filter === null ? ' active' : ''}`}
                  aria-pressed={filter === null}
                  onClick={() => setFilter(null)}
                >
                  Alle ({d.total})
                </button>
                {d.groups.map((g) => (
                  <button
                    key={g.id}
                    type="button"
                    className={`chip${filter === g.id ? ' active' : ''}`}
                    aria-pressed={filter === g.id}
                    onClick={() => setFilter(g.id)}
                  >
                    {g.name} ({count(g.id)})
                  </button>
                ))}
                <button
                  type="button"
                  className={`chip${filter === NO_GROUP ? ' active' : ''}`}
                  aria-pressed={filter === NO_GROUP}
                  onClick={() => setFilter(NO_GROUP)}
                >
                  Zonder groep ({d.withoutGroup})
                </button>
              </div>
              <span className="grow" />
              {canNotify ? (
                <Link className="button secondary" to="/meldingen/nieuw" search={{ doelgroep: 'dansgarde' }}>
                  Melding aan dansgarde
                </Link>
              ) : null}
            </div>
            {d.members.length === 0 ? (
              <p className="muted">Nog geen leden met groep "Dansgarde" in e-Boekhouden.</p>
            ) : (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Dansgarde">
                <table className="table">
                  <caption className="visually-hidden">Dansgarde-leden</caption>
                  <thead>
                    <tr>
                      <th scope="col">Naam</th>
                      <th scope="col">Leeftijd</th>
                      <th scope="col">Dansgroep</th>
                      <th scope="col">Ouders in de app</th>
                      <th scope="col">Mijn QR</th>
                      <th scope="col">
                        <span className="visually-hidden">Opmerking</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((m) => (
                      <tr key={m.memberId}>
                        <td>
                          <Link to="/leden/$id" params={{ id: m.memberId }}>
                            {m.fullName}
                          </Link>
                          <div className="muted small-text">Lidnummer {m.memberNumber}</div>
                        </td>
                        <td>{m.age ?? '–'}</td>
                        <td>
                          <label className="visually-hidden" htmlFor={`groep-${m.memberId}`}>
                            Dansgroep van {m.fullName}
                          </label>
                          <select
                            id={`groep-${m.memberId}`}
                            value={m.danceGroup?.id ?? ''}
                            disabled={!canEdit || assign.isPending}
                            onChange={(e) => assign.mutate({ memberId: m.memberId, groupId: e.target.value || null })}
                          >
                            <option value="">— kies dansgroep</option>
                            {d.groups.map((g) => (
                              <option key={g.id} value={g.id}>
                                {g.name}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          {m.ownAccount ? (
                            <span className="badge">Eigen account</span>
                          ) : m.guardians.length ? (
                            <span className="badge ok">{m.guardians.join(', ')}</span>
                          ) : m.hasSuggestion ? (
                            <span className="badge info">Voorstel</span>
                          ) : (
                            <span className="badge error">Geen</span>
                          )}
                        </td>
                        <td>
                          {m.ownAccount || m.guardians.length ? (
                            <span className="badge ok">Klaar</span>
                          ) : (
                            <span className="badge warn">Geen ouder</span>
                          )}
                        </td>
                        <td>
                          {m.turnsFifteenOn ? (
                            <span className="badge info">Wordt 15 op {formatDate(m.turnsFifteenOn)}</span>
                          ) : null}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            {d.groups.length === 0 ? (
              <p className="muted">
                Nog geen dansgroepen. Maak ze aan onder <Link to="/dansgarde/groepen">Dansgroepen</Link>.
              </p>
            ) : null}
          </section>
        </>
      ) : null}
    </>
  );
}
