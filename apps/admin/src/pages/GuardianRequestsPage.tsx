import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useGuardianRequests,
  useGuardianSuggestions,
  useMe,
  type GuardianRequestView,
} from '../api/hooks';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

type Tab = 'requests' | 'suggestions' | 'done';

const relationshipLabels: Record<string, string> = { Parent: 'ouder', Caregiver: 'verzorger' };

const invalidate = [['guardian-requests'], ['guardian-suggestions'], ['dansgarde']];

/**
 * Leden → Koppelverzoeken (fase 17): verzoeken van ouders uit de app (op naam van het kind; het bestuur kiest het lid)
 * en voorstellen op basis van hetzelfde e-mailadres. Er wordt niets vanzelf gekoppeld.
 */
export function GuardianRequestsPage() {
  const me = useMe();
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  const [tab, setTab] = useState<Tab>('requests');
  const pending = useGuardianRequests('Pending');
  const suggestions = useGuardianSuggestions();
  const [message, setMessage] = useState<string | null>(null);

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Koppelverzoeken</h1>
          <p className="page-subtitle">
            Ouders vragen in de app een koppeling aan met hun kind (op voor- en achternaam). Voorstellen komen van
            hetzelfde e-mailadres bij twee leden. Pas na jouw goedkeuring zien ouders het kind in "Mijn kinderen".
          </p>
        </div>
      </div>
      <div className="tabs" role="tablist" aria-label="Koppelverzoeken">
        <button
          type="button"
          role="tab"
          className="tab"
          aria-selected={tab === 'requests'}
          onClick={() => setTab('requests')}
        >
          Verzoeken uit de app ({pending.data?.length ?? 0})
        </button>
        <button
          type="button"
          role="tab"
          className="tab"
          aria-selected={tab === 'suggestions'}
          onClick={() => setTab('suggestions')}
        >
          Voorstellen: zelfde e-mail ({suggestions.data?.length ?? 0})
        </button>
        <button type="button" role="tab" className="tab" aria-selected={tab === 'done'} onClick={() => setTab('done')}>
          Afgehandeld
        </button>
      </div>
      <SuccessMessage message={message} />
      {tab === 'requests' ? <Requests canEdit={canEdit} onMessage={setMessage} /> : null}
      {tab === 'suggestions' ? <Suggestions canEdit={canEdit} onMessage={setMessage} /> : null}
      {tab === 'done' ? <Done /> : null}
    </>
  );
}

function Requests({ canEdit, onMessage }: { canEdit: boolean; onMessage: (m: string) => void }) {
  const api = useApi();
  const requests = useGuardianRequests('Pending');
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const approve = useApiMutation(
    async ({ id, memberId }: { id: string; memberId: string }) =>
      (await api.POST('/api/v1/admin/guardian-requests/{id}/approve', { params: { path: { id } }, body: { memberId } }))
        .data,
    invalidate,
  );
  const reject = useApiMutation(
    async (id: string) =>
      (
        await api.POST('/api/v1/admin/guardian-requests/{id}/reject', {
          params: { path: { id } },
          body: { reason: null },
        })
      ).data,
    invalidate,
  );

  const memberFor = (r: GuardianRequestView) =>
    chosen[r.id] ?? (r.candidates.length === 1 ? r.candidates[0]!.memberId : '');

  return (
    <section className="card" aria-labelledby="verzoeken">
      <h2 id="verzoeken">Wachten op beoordeling</h2>
      <ProblemAlert error={requests.error ?? approve.error ?? reject.error} />
      {requests.data?.length === 0 ? <p className="muted">Geen openstaande verzoeken.</p> : null}
      {requests.data?.length ? (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Verzoeken uit de app">
          <table className="table">
            <caption className="visually-hidden">Verzoeken uit de app</caption>
            <thead>
              <tr>
                <th scope="col">Aangevraagd door</th>
                <th scope="col">Opgegeven kind</th>
                <th scope="col">Gevonden lid</th>
                <th scope="col">Aangevraagd</th>
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {requests.data.map((r) => {
                const memberId = memberFor(r);
                const candidate = r.candidates.find((c) => c.memberId === memberId);
                return (
                  <tr key={r.id}>
                    <td>
                      <strong>{r.requestedByName}</strong>
                      <div className="muted small-text">
                        {r.requestedByEmail} · {relationshipLabels[r.relationship]}
                        {r.phone ? ` · ${r.phone}` : ''}
                      </div>
                    </td>
                    <td>
                      {r.childFirstName} {r.childLastName}
                    </td>
                    <td>
                      {r.candidates.length === 0 ? (
                        <span className="badge error">Geen lid gevonden</span>
                      ) : r.candidates.length === 1 ? (
                        <>
                          {r.candidates[0]!.name} · {r.candidates[0]!.memberNumber}
                          {r.candidates[0]!.age !== null ? ` · ${r.candidates[0]!.age} jaar` : ''}
                          {r.candidates[0]!.guardians ? (
                            <span className="badge info"> {r.candidates[0]!.guardians} ouder(s) gekoppeld</span>
                          ) : null}
                        </>
                      ) : (
                        <>
                          <label className="visually-hidden" htmlFor={`kies-${r.id}`}>
                            Kies het lid voor {r.childFirstName} {r.childLastName}
                          </label>
                          <select
                            id={`kies-${r.id}`}
                            value={memberId}
                            onChange={(e) => setChosen({ ...chosen, [r.id]: e.target.value })}
                          >
                            <option value="">{r.candidates.length} leden gevonden — kies</option>
                            {r.candidates.map((c) => (
                              <option key={c.memberId} value={c.memberId}>
                                {c.name} ({c.memberNumber}
                                {c.age !== null ? `, ${c.age} jaar` : ''})
                              </option>
                            ))}
                          </select>
                        </>
                      )}
                      {r.candidates.length === 0 ? (
                        <div className="muted small-text">
                          Zoek het lid onder <Link to="/leden">Leden</Link> en koppel daar de ouder, of wijs af.
                        </div>
                      ) : null}
                    </td>
                    <td>{formatDateTime(r.createdAt)}</td>
                    <td>
                      {canEdit ? (
                        <div className="toolbar">
                          <button
                            type="button"
                            className="button secondary small"
                            disabled={reject.isPending}
                            onClick={() => reject.mutate(r.id)}
                          >
                            Afwijzen
                          </button>
                          <button
                            type="button"
                            className="button small"
                            disabled={!memberId || approve.isPending}
                            onClick={() =>
                              approve.mutate(
                                { id: r.id, memberId },
                                {
                                  onSuccess: () =>
                                    onMessage(
                                      `${r.requestedByName} is gekoppeld aan ${candidate?.name ?? 'het kind'}.`,
                                    ),
                                },
                              )
                            }
                          >
                            Goedkeuren
                          </button>
                        </div>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}
      <p className="muted small-text">
        Goedkeuren kan alleen hier of bij het lid, door het bestuur. Een kind heeft maximaal 2 ouders/verzorgers. De
        ouder krijgt een melding als het verzoek is goedgekeurd of afgewezen.
      </p>
    </section>
  );
}

function Suggestions({ canEdit, onMessage }: { canEdit: boolean; onMessage: (m: string) => void }) {
  const api = useApi();
  const suggestions = useGuardianSuggestions();
  const link = useApiMutation(
    async (body: { childMemberId: string; parentMemberId: string }) =>
      (await api.POST('/api/v1/admin/guardian-suggestions/link', { body })).data,
    invalidate,
  );
  const dismiss = useApiMutation(
    async (body: { childMemberId: string; parentMemberId: string }) =>
      (await api.POST('/api/v1/admin/guardian-suggestions/dismiss', { body })).data,
    invalidate,
  );
  const busy = link.isPending || dismiss.isPending;

  return (
    <section className="card" aria-labelledby="voorstellen">
      <h2 id="voorstellen">Mogelijke ouder-kindrelaties</h2>
      <ProblemAlert error={suggestions.error ?? link.error ?? dismiss.error} />
      {suggestions.data?.length === 0 ? <p className="muted">Geen voorstellen.</p> : null}
      {suggestions.data?.length ? (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Voorstellen">
          <table className="table">
            <caption className="visually-hidden">Voorstellen op basis van hetzelfde e-mailadres</caption>
            <thead>
              <tr>
                <th scope="col">Kind (jonger dan 15)</th>
                <th scope="col">Mogelijke ouder</th>
                <th scope="col">Waarom</th>
                <th scope="col">App-account ouder</th>
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {suggestions.data.map((s) => {
                const key = { childMemberId: s.childMemberId, parentMemberId: s.parentMemberId };
                const youngParent = s.parentAge !== null && s.parentAge !== undefined && s.parentAge < 18;
                return (
                  <tr key={`${s.childMemberId}-${s.parentMemberId}`}>
                    <td>
                      <Link to="/leden/$id" params={{ id: s.childMemberId }}>
                        {s.childName}
                      </Link>
                      <div className="muted small-text">
                        {s.childNumber}
                        {s.childAge !== null ? ` · ${s.childAge} jaar` : ''}
                        {s.childDansgarde ? ' · Dansgarde' : ''}
                      </div>
                    </td>
                    <td>
                      <strong>{s.parentName}</strong>
                      <div className="muted small-text">
                        {s.parentNumber}
                        {s.parentAge !== null ? ` · ${s.parentAge} jaar` : ''}
                      </div>
                    </td>
                    <td>
                      Zelfde e-mail: {s.email}
                      {youngParent ? (
                        <div className="small-text">Let op: {s.parentName} is zelf jonger dan 18.</div>
                      ) : null}
                    </td>
                    <td>
                      {s.parentUserId ? (
                        <span className="badge ok">Heeft account</span>
                      ) : (
                        <span className="badge warn">Nog geen account</span>
                      )}
                    </td>
                    <td>
                      {canEdit ? (
                        <div className="toolbar">
                          <button
                            type="button"
                            className="button secondary small"
                            disabled={busy}
                            onClick={() => dismiss.mutate(key)}
                          >
                            Geen relatie
                          </button>
                          <button
                            type="button"
                            className="button small"
                            disabled={busy}
                            onClick={() =>
                              link.mutate(key, {
                                onSuccess: () => onMessage(`${s.parentName} is gekoppeld aan ${s.childName}.`),
                              })
                            }
                          >
                            {s.parentUserId ? 'Koppelen' : 'Koppelen + uitnodigen'}
                          </button>
                        </div>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}
      <p className="muted small-text">
        Alleen voorstellen: pas na "Koppelen" ziet de ouder het kind in Mijn kinderen. "Geen relatie" onthoudt je keuze,
        zodat het voorstel niet terugkomt. Kinderen zonder voorstel koppel je via de ledenpagina (kaart
        Ouders/verzorgers).
      </p>
    </section>
  );
}

function Done() {
  const approved = useGuardianRequests('Approved');
  const rejected = useGuardianRequests('Rejected');
  const rows = [...(approved.data ?? []), ...(rejected.data ?? [])].sort((a, b) =>
    (b.decidedAt ?? '').localeCompare(a.decidedAt ?? ''),
  );
  return (
    <section className="card" aria-labelledby="afgehandeld">
      <h2 id="afgehandeld">Afgehandeld</h2>
      <ProblemAlert error={approved.error ?? rejected.error} />
      {rows.length === 0 ? <p className="muted">Nog niets afgehandeld.</p> : null}
      <ul className="list">
        {rows.map((r) => (
          <li key={r.id} className="list-row">
            <span className={`badge ${r.status === 'Approved' ? 'ok' : 'error'}`}>
              {r.status === 'Approved' ? 'Goedgekeurd' : 'Afgewezen'}
            </span>
            <p className="grow">
              {r.requestedByName} → {r.memberName ?? `${r.childFirstName} ${r.childLastName}`}
            </p>
            <span className="muted small-text">{formatDateTime(r.decidedAt)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
