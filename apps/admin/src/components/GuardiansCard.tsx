import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useGuardianCandidates, useMemberGuardians } from '../api/hooks';
import { formatDate, formatDateTime } from '../format';
import { Dialog } from './Dialog';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

type Relationship = 'Parent' | 'Caregiver';

const relationshipLabels: Record<Relationship, string> = { Parent: 'ouder', Caregiver: 'verzorger' };

const invalidate = (memberId: string) => [
  ['member-guardians', memberId],
  ['member', memberId],
  ['guardian-requests'],
  ['guardian-suggestions'],
  ['dansgarde'],
];

/**
 * Kaarten "Ouders/verzorgers" en "Eigen account" bij een lid (fase 17, Figma 👨‍👧 Ouders & kinderen). Voor leden onder
 * 18: gekoppelde ouders (hooguit 2), een voorstel bij hetzelfde e-mailadres, openstaande koppelverzoeken en vanaf 15
 * "Eigen account geven".
 */
export function GuardiansCard({
  memberId,
  memberName,
  canEdit,
}: {
  memberId: string;
  memberName: string;
  canEdit: boolean;
}) {
  const api = useApi();
  const data = useMemberGuardians(memberId);
  const [message, setMessage] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [ownOpen, setOwnOpen] = useState(false);
  const firstName = memberName.split(' ')[0] ?? memberName;

  const unlink = useApiMutation(
    async (relationId: string) =>
      (
        await api.DELETE('/api/v1/admin/members/{id}/guardians/{relationId}', {
          params: { path: { id: memberId, relationId } },
        })
      ).data,
    invalidate(memberId),
  );
  const acceptSuggestion = useApiMutation(
    async (parentMemberId: string) =>
      (await api.POST('/api/v1/admin/guardian-suggestions/link', { body: { childMemberId: memberId, parentMemberId } }))
        .data,
    invalidate(memberId),
  );
  const dismissSuggestion = useApiMutation(
    async (parentMemberId: string) =>
      (
        await api.POST('/api/v1/admin/guardian-suggestions/dismiss', {
          body: { childMemberId: memberId, parentMemberId },
        })
      ).data,
    invalidate(memberId),
  );
  const approve = useApiMutation(
    async (requestId: string) =>
      (
        await api.POST('/api/v1/admin/guardian-requests/{id}/approve', {
          params: { path: { id: requestId } },
          body: { memberId },
        })
      ).data,
    invalidate(memberId),
  );
  const reject = useApiMutation(
    async (requestId: string) =>
      (
        await api.POST('/api/v1/admin/guardian-requests/{id}/reject', {
          params: { path: { id: requestId } },
          body: { reason: null },
        })
      ).data,
    invalidate(memberId),
  );

  const g = data.data;
  if (!g) {
    return <ProblemAlert error={data.error} />;
  }
  if (!g.applies) {
    return null;
  }
  const full = g.guardians.length >= g.max;
  const own = g.ownAccount;
  const busy =
    unlink.isPending ||
    acceptSuggestion.isPending ||
    dismissSuggestion.isPending ||
    approve.isPending ||
    reject.isPending;
  const error = unlink.error ?? acceptSuggestion.error ?? dismissSuggestion.error ?? approve.error ?? reject.error;

  return (
    <>
      <section className="card" aria-labelledby="ouders">
        <div className="card-header">
          <h2 id="ouders">Ouders/verzorgers</h2>
          <span className="badge">
            {g.guardians.length} van {g.max}
          </span>
        </div>
        <SuccessMessage message={message} />
        <ProblemAlert error={error} />
        {g.guardians.length === 0 ? <p className="muted">Nog geen ouder of verzorger gekoppeld.</p> : null}
        <ul className="list">
          {g.guardians.map((p) => (
            <li key={p.id} className="list-row">
              <div className="grow">
                <strong>{p.name}</strong>
                <div className="muted small-text">
                  {p.email} · {relationshipLabels[p.relationship as Relationship]}
                  {p.isMember ? ' · ook zelf lid' : ''}
                  {p.phone ? ` · ${p.phone}` : ''}
                </div>
              </div>
              {canEdit ? (
                <button
                  type="button"
                  className="button ghost small"
                  disabled={busy}
                  onClick={() => unlink.mutate(p.id, { onSuccess: () => setMessage(`${p.name} is ontkoppeld.`) })}
                >
                  Ontkoppelen
                </button>
              ) : null}
            </li>
          ))}
        </ul>

        {g.suggestions.map((s) => (
          <div key={s.parentMemberId} className="alert alert-info">
            <strong>Voorstel: zelfde e-mailadres</strong>
            <div>
              {s.email} hoort ook bij {s.parentName} ({s.parentNumber}
              {s.parentAge !== null && s.parentAge !== undefined ? `, ${s.parentAge} jaar` : ''}
              {s.parentUserId ? ', heeft een app-account' : ', nog geen app-account'}). Is {s.parentName} de ouder of
              verzorger van {firstName}?
            </div>
            {canEdit && !full ? (
              <div className="toolbar">
                <button
                  type="button"
                  className="button small"
                  disabled={busy}
                  onClick={() =>
                    acceptSuggestion.mutate(s.parentMemberId, {
                      onSuccess: () => setMessage(`${s.parentName} is gekoppeld.`),
                    })
                  }
                >
                  {s.parentUserId ? 'Koppelen' : 'Koppelen + uitnodigen'}
                </button>
                <button
                  type="button"
                  className="button ghost small"
                  disabled={busy}
                  onClick={() => dismissSuggestion.mutate(s.parentMemberId)}
                >
                  Geen relatie
                </button>
              </div>
            ) : null}
          </div>
        ))}

        {g.requests.map((r) => (
          <div key={r.id} className="alert alert-warning">
            <strong>{r.requestedByName} vraagt koppeling aan</strong>
            <div className="small-text">
              Via de app, {formatDateTime(r.createdAt)}. Opgegeven kind: "{r.childFirstName} {r.childLastName}".
              Relatie: {relationshipLabels[r.relationship as Relationship]}.{r.phone ? ` Telefoon ${r.phone}.` : ''}
            </div>
            {canEdit ? (
              <div className="toolbar">
                <button
                  type="button"
                  className="button small"
                  disabled={busy || full}
                  onClick={() =>
                    approve.mutate(r.id, {
                      onSuccess: () => setMessage(`${r.requestedByName} is gekoppeld aan ${firstName}.`),
                    })
                  }
                >
                  Goedkeuren
                </button>
                <button
                  type="button"
                  className="button secondary small"
                  disabled={busy}
                  onClick={() => reject.mutate(r.id)}
                >
                  Afwijzen
                </button>
              </div>
            ) : null}
          </div>
        ))}

        {canEdit && !full ? (
          <button type="button" className="button ghost" onClick={() => setAdding(true)}>
            + Ouder koppelen
          </button>
        ) : null}
        <p className="muted small-text">
          Maximaal {g.max} ouders/verzorgers. Zoek een bestaand account of nodig een ouder uit per e-mail. Ouders
          ontvangen meldingen namens {firstName} tot {firstName} 18 is
          {own.guardiansUntil ? ` (${formatDate(own.guardiansUntil)})` : ''}.
        </p>
      </section>

      {!own.hasAccount ? (
        <section className="card" aria-labelledby="eigen-account">
          <div className="card-header">
            <h2 id="eigen-account">Eigen account</h2>
            <span className="badge info">{own.pending ? 'Wordt aangemaakt' : 'Via ouder'}</span>
          </div>
          <p>
            {firstName} heeft geen eigen inlog: {firstName} staat onder "Mijn kinderen" bij de ouders, die de QR kunnen
            tonen.
          </p>
          <p className="muted small-text">
            {own.canGetOwnAccount
              ? `${firstName} is ${own.age}: ${firstName} kan nu een eigen account krijgen met een eigen e-mailadres.`
              : own.pending
                ? 'De uitnodiging voor het eigen account wordt verstuurd.'
                : own.availableFrom
                  ? `${firstName} wordt 15 op ${formatDate(own.availableFrom)}. Vanaf dan kun je een eigen account geven.`
                  : 'Vul de geboortedatum in; een eigen account kan vanaf 15 jaar.'}
          </p>
          {canEdit ? (
            <button
              type="button"
              className="button secondary"
              disabled={!own.canGetOwnAccount}
              onClick={() => setOwnOpen(true)}
            >
              Eigen account geven
            </button>
          ) : null}
        </section>
      ) : null}

      <AddGuardianDialog
        open={adding}
        memberId={memberId}
        firstName={firstName}
        onClose={() => setAdding(false)}
        onDone={(text) => {
          setAdding(false);
          setMessage(text);
        }}
      />
      <OwnAccountDialog
        open={ownOpen}
        memberId={memberId}
        firstName={firstName}
        guardianNames={g.guardians.map((p) => p.name)}
        guardiansUntil={own.guardiansUntil ?? null}
        onClose={() => setOwnOpen(false)}
        onDone={(text) => {
          setOwnOpen(false);
          setMessage(text);
        }}
      />
      <p className="muted small-text">
        Openstaande verzoeken en voorstellen voor alle leden staan onder{' '}
        <Link to="/koppelverzoeken">Koppelverzoeken</Link>.
      </p>
    </>
  );
}

function AddGuardianDialog({
  open,
  memberId,
  firstName,
  onClose,
  onDone,
}: {
  open: boolean;
  memberId: string;
  firstName: string;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const api = useApi();
  const [mode, setMode] = useState<'search' | 'invite'>('search');
  const [search, setSearch] = useState('');
  const [relationship, setRelationship] = useState<Relationship>('Parent');
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const candidates = useGuardianCandidates(search);
  const link = useApiMutation(
    async (userId: string) =>
      (
        await api.POST('/api/v1/admin/members/{id}/guardians', {
          params: { path: { id: memberId } },
          body: { userId, relationship },
        })
      ).data,
    invalidate(memberId),
  );
  const invite = useApiMutation(
    async () =>
      (
        await api.POST('/api/v1/admin/members/{id}/guardians/invite', {
          params: { path: { id: memberId } },
          body: { email, name, relationship },
        })
      ).data,
    invalidate(memberId),
  );

  function submitInvite(e: FormEvent) {
    e.preventDefault();
    invite.mutate(undefined, {
      onSuccess: () => onDone(`${name} is gekoppeld; een nieuwe ouder krijgt een uitnodiging op ${email}.`),
    });
  }

  return (
    <Dialog open={open} title={`Ouder koppelen aan ${firstName}`} onClose={onClose}>
      <div className="field">
        <label htmlFor="relatie">Relatie</label>
        <select id="relatie" value={relationship} onChange={(e) => setRelationship(e.target.value as Relationship)}>
          <option value="Parent">Ouder</option>
          <option value="Caregiver">Verzorger</option>
        </select>
      </div>
      <div className="toolbar" role="group" aria-label="Manier van koppelen">
        <button
          type="button"
          className={mode === 'search' ? 'button small' : 'button ghost small'}
          onClick={() => setMode('search')}
        >
          Bestaand account
        </button>
        <button
          type="button"
          className={mode === 'invite' ? 'button small' : 'button ghost small'}
          onClick={() => setMode('invite')}
        >
          Nieuwe ouder uitnodigen
        </button>
      </div>
      <ProblemAlert error={link.error ?? invite.error} />
      {mode === 'search' ? (
        <>
          <Field label="Zoek op naam of e-mail" value={search} onChange={(e) => setSearch(e.target.value)} autoFocus />
          <ul className="list">
            {(candidates.data ?? []).map((c) => (
              <li key={c.userId} className="list-row">
                <div className="grow">
                  <strong>{c.name}</strong>
                  <div className="muted small-text">
                    {c.email}
                    {c.isMember ? ' · lid' : ''}
                  </div>
                </div>
                <button
                  type="button"
                  className="button small"
                  disabled={link.isPending}
                  onClick={() =>
                    link.mutate(c.userId, { onSuccess: () => onDone(`${c.name} is gekoppeld aan ${firstName}.`) })
                  }
                >
                  Koppelen
                </button>
              </li>
            ))}
          </ul>
          {search.trim().length >= 2 && candidates.data?.length === 0 ? (
            <p className="muted">Geen account gevonden.</p>
          ) : null}
        </>
      ) : (
        <form onSubmit={submitInvite}>
          <Field
            label="Naam van de ouder"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            maxLength={100}
          />
          <Field
            label="E-mailadres"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            maxLength={254}
          />
          <p className="muted small-text">
            Een nieuwe ouder krijgt een account met alleen de rol Ouder/verzorger en een uitnodiging per e-mail.
          </p>
          <div className="actions">
            <button type="button" className="button secondary" onClick={onClose}>
              Annuleren
            </button>
            <button type="submit" className="button" disabled={invite.isPending}>
              Uitnodigen en koppelen
            </button>
          </div>
        </form>
      )}
    </Dialog>
  );
}

function OwnAccountDialog({
  open,
  memberId,
  firstName,
  guardianNames,
  guardiansUntil,
  onClose,
  onDone,
}: {
  open: boolean;
  memberId: string;
  firstName: string;
  guardianNames: string[];
  guardiansUntil: string | null;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const api = useApi();
  const [email, setEmail] = useState('');
  const give = useApiMutation(
    async () =>
      (
        await api.POST('/api/v1/admin/members/{id}/own-account', {
          params: { path: { id: memberId } },
          body: { email },
        })
      ).data,
    invalidate(memberId),
  );

  function submit(e: FormEvent) {
    e.preventDefault();
    give.mutate(undefined, { onSuccess: () => onDone(`${firstName} krijgt een uitnodiging op ${email}.`) });
  }

  return (
    <Dialog open={open} title={`Eigen account geven aan ${firstName}`} onClose={onClose}>
      <form onSubmit={submit}>
        <p>
          {firstName} krijgt een uitnodiging op het eigen e-mailadres en logt daarna zelf in. Lidmaatschap, groepen en
          inschrijvingen blijven gewoon staan.
        </p>
        <Field
          label={`E-mailadres van ${firstName}`}
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          maxLength={254}
        />
        <ul className="alert alert-info">
          <li>Mijn QR verhuist naar de eigen telefoon van {firstName} (telt niet mee voor de 3 keer overzetten).</li>
          {guardianNames.length ? (
            <li>
              {guardianNames.join(' en ')} {guardianNames.length > 1 ? 'blijven' : 'blijft'} gekoppeld tot {firstName}{' '}
              18 wordt
              {guardiansUntil ? ` (${formatDate(guardiansUntil)})` : ''}: meldingen "Namens {firstName}", maar de QR
              niet meer.
            </li>
          ) : null}
          <li>Het e-mailadres in e-Boekhouden verandert niet vanzelf; pas het daar aan als {firstName} dat wil.</li>
        </ul>
        <ProblemAlert error={give.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={give.isPending}>
            Uitnodiging versturen
          </button>
        </div>
      </form>
    </Dialog>
  );
}
