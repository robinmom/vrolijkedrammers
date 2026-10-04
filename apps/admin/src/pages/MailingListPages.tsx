import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useGroups, useMembers } from '../api/hooks';
import { MAILING_KEYS, useMailingList, useMailingLists, useMailingUnsubscribes } from '../api/mailing';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/** Mailing → Mailinggroepen (fase 27a), met onderaan wie zich heeft afgemeld. */
export function MailingListsPage() {
  const lists = useMailingLists();
  const unsubscribes = useMailingUnsubscribes();
  const api = useApi();
  const [resubscribe, setResubscribe] = useState<string | null>(null);
  const restore = useApiMutation(
    (email: string) => api.DELETE('/api/v1/admin/mailing/unsubscribes', { params: { query: { email } } }),
    MAILING_KEYS,
  );
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Mailinggroepen</h1>
          <p className="muted">
            Groepen voor de nieuwsbrief en uitnodigingen: leden, ledengroepen en losse e-mailadressen.
          </p>
        </div>
        <Link to="/mailing/groepen/$id" params={{ id: 'nieuw' }} className="button">
          Groep toevoegen
        </Link>
      </div>
      <ProblemAlert error={lists.error} />
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Mailinggroepen">
        <table className="table">
          <caption className="visually-hidden">Mailinggroepen</caption>
          <thead>
            <tr>
              <th scope="col">Naam</th>
              <th scope="col">Bestaat uit</th>
            </tr>
          </thead>
          <tbody>
            {(lists.data ?? []).map((l) => (
              <tr key={l.id}>
                <td>
                  <Link to="/mailing/groepen/$id" params={{ id: l.id }}>
                    {l.name}
                  </Link>
                  {l.description ? <div className="muted">{l.description}</div> : null}
                </td>
                <td>
                  {[
                    l.allMembers ? 'alle leden' : null,
                    l.allAdvertisers ? 'alle adverteerders' : null,
                    l.memberCount ? `${l.memberCount} leden` : null,
                    l.groupCount ? `${l.groupCount} ledengroepen` : null,
                    l.addressCount ? `${l.addressCount} losse adressen` : null,
                  ]
                    .filter(Boolean)
                    .join(', ') || '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <section className="card" aria-labelledby="afgemeld-kop">
        <h2 id="afgemeld-kop">Afgemeld</h2>
        <p className="muted">
          Deze adressen krijgen geen nieuwsbrieven en uitnodigingen meer. Zet iemand alleen terug als hij of zij daar
          zelf om vraagt.
        </p>
        <ProblemAlert error={unsubscribes.error ?? restore.error} />
        {unsubscribes.data?.length === 0 ? <p className="muted">Niemand heeft zich afgemeld.</p> : null}
        <ul className="plain-list">
          {(unsubscribes.data ?? []).map((u) => (
            <li key={u.email} className="list-row">
              <span>
                {u.email} <span className="muted">· {formatDateTime(u.unsubscribedAt)}</span>
              </span>
              <button type="button" className="button secondary small" onClick={() => setResubscribe(u.email)}>
                Weer aanmelden
              </button>
            </li>
          ))}
        </ul>
      </section>
      <ConfirmDialog
        open={resubscribe !== null}
        title="Weer aanmelden?"
        message={`${resubscribe} krijgt weer nieuwsbrieven en uitnodigingen. Doe dit alleen op verzoek.`}
        confirmLabel="Weer aanmelden"
        busy={restore.isPending}
        onCancel={() => setResubscribe(null)}
        onConfirm={() => restore.mutate(resubscribe!, { onSettled: () => setResubscribe(null) })}
      />
    </>
  );
}

interface ChosenMember {
  id: string;
  fullName: string;
  memberNumber: string | null;
}

/** Losse adressen: één per regel, "e-mail", "naam <e-mail>" of "e-mail; naam" (zoals uit Excel geplakt). */
export function parseAddresses(text: string): { email: string; name: string | null }[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const angle = line.match(/^(.*)<([^>]+)>$/);
      if (angle) return { email: angle[2]!.trim(), name: angle[1]!.trim().replace(/^"|"$/g, '') || null };
      const [first, ...rest] = line.split(/[;,\t]/).map((p) => p.trim());
      const name = rest.join(' ').trim();
      return first!.includes('@') ? { email: first!, name: name || null } : { email: name, name: first || null };
    });
}

const formatAddresses = (addresses: { email: string; name: string | null }[]) =>
  addresses.map((a) => (a.name ? `${a.name} <${a.email}>` : a.email)).join('\n');

export function MailingListEditorPage() {
  const { id } = useParams({ from: '/mailing/groepen/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const existing = useMailingList(isNew ? null : id);
  const groups = useGroups();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [allMembers, setAllMembers] = useState(false);
  const [allAdvertisers, setAllAdvertisers] = useState(false);
  const [groupIds, setGroupIds] = useState<string[]>([]);
  const [members, setMembers] = useState<ChosenMember[]>([]);
  const [addresses, setAddresses] = useState('');
  const [search, setSearch] = useState('');
  const found = useMembers({ search, status: 'Active', syncState: '' }, 1);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    const l = existing.data;
    if (l) {
      setName(l.name);
      setDescription(l.description ?? '');
      setAllMembers(l.allMembers);
      setAllAdvertisers(l.allAdvertisers);
      setGroupIds(l.groups.map((g) => g.id));
      setMembers(l.members.map((m) => ({ id: m.id, fullName: m.fullName, memberNumber: m.memberNumber })));
      setAddresses(formatAddresses(l.addresses));
    }
  }, [existing.data]);

  const save = useApiMutation(async () => {
    const body = {
      name,
      description: description || null,
      allMembers,
      allAdvertisers,
      groupIds,
      memberIds: members.map((m) => m.id),
      addresses: parseAddresses(addresses),
    };
    return isNew
      ? (await api.POST('/api/v1/admin/mailing/lists', { body })).data?.id
      : (await api.PUT('/api/v1/admin/mailing/lists/{id}', { params: { path: { id } }, body }), id);
  }, MAILING_KEYS);
  const remove = useApiMutation(
    () => api.DELETE('/api/v1/admin/mailing/lists/{id}', { params: { path: { id } } }),
    MAILING_KEYS,
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(undefined, {
      onSuccess: (newId) => {
        setMessage('Groep opgeslagen.');
        if (isNew && typeof newId === 'string') void navigate({ to: '/mailing/groepen/$id', params: { id: newId } });
      },
    });
  }

  const audience = existing.data?.audience;
  const addressCount = parseAddresses(addresses).length;

  return (
    <>
      <p>
        <Link to="/mailing/groepen">← Mailinggroepen</Link>
      </p>
      <h1>{isNew ? 'Groep toevoegen' : name || 'Mailinggroep'}</h1>
      <SuccessMessage message={message} />
      {audience ? (
        <p className="muted">
          Nu {audience.recipients} ontvangers
          {audience.unsubscribed > 0 ? `, ${audience.unsubscribed} afgemeld` : ''}
          {audience.withoutEmail > 0 ? `, ${audience.withoutEmail} zonder (geldig) e-mailadres` : ''}. Bij het versturen
          wordt de groep opnieuw uitgerekend.
        </p>
      ) : null}
      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          <Field label="Naam" required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
          <Field
            label="Omschrijving"
            maxLength={500}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <Checkbox
          label="Alle actieve leden met een e-mailadres"
          checked={allMembers}
          onChange={(e) => setAllMembers(e.target.checked)}
        />
        <Checkbox
          label="Alle actieve adverteerders met een e-mailadres"
          checked={allAdvertisers}
          onChange={(e) => setAllAdvertisers(e.target.checked)}
        />

        <fieldset className="checkbox-group" disabled={allMembers}>
          <legend>Ledengroepen</legend>
          {(groups.data ?? [])
            .filter((g) => g.active)
            .map((g) => (
              <label key={g.id} className="checkbox">
                <input
                  type="checkbox"
                  checked={groupIds.includes(g.id)}
                  onChange={(e) =>
                    setGroupIds(e.target.checked ? [...groupIds, g.id] : groupIds.filter((x) => x !== g.id))
                  }
                />
                {g.name}
              </label>
            ))}
        </fieldset>

        <fieldset className="checkbox-group" disabled={allMembers}>
          <legend>Losse leden ({members.length})</legend>
          <ul className="plain-list">
            {members.map((m) => (
              <li key={m.id} className="list-row">
                <span>
                  {m.fullName} {m.memberNumber ? <span className="muted">· {m.memberNumber}</span> : null}
                </span>
                <button
                  type="button"
                  className="button secondary small"
                  onClick={() => setMembers(members.filter((x) => x.id !== m.id))}
                >
                  Weghalen
                </button>
              </li>
            ))}
          </ul>
          <Field
            label="Lid zoeken"
            type="search"
            placeholder="Naam, lidnummer of e-mailadres"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {search.length >= 2 ? (
            <ul className="plain-list" aria-label="Zoekresultaten">
              {(found.data?.items ?? [])
                .filter((f) => !members.some((m) => m.id === f.id))
                .slice(0, 8)
                .map((f) => (
                  <li key={f.id} className="list-row">
                    <span>
                      {f.fullName} <span className="muted">· {f.memberNumber}</span>{' '}
                      {f.email ? null : <span className="badge warn">geen e-mail</span>}
                    </span>
                    <button
                      type="button"
                      className="button secondary small"
                      onClick={() =>
                        setMembers([...members, { id: f.id, fullName: f.fullName, memberNumber: f.memberNumber }])
                      }
                    >
                      Toevoegen
                    </button>
                  </li>
                ))}
            </ul>
          ) : null}
        </fieldset>

        <div className="field">
          <label htmlFor="losse-adressen">Losse e-mailadressen ({addressCount})</label>
          <textarea
            id="losse-adressen"
            rows={8}
            aria-describedby="losse-adressen-hint"
            value={addresses}
            onChange={(e) => setAddresses(e.target.value)}
            placeholder={'Jan Jansen <jan@example.com>\npiet@example.com'}
          />
          <small id="losse-adressen-hint" className="muted">
            Eén per regel: "naam &lt;e-mail&gt;", "e-mail; naam" of alleen het e-mailadres. Plakken uit Excel kan ook.
          </small>
        </div>

        <ProblemAlert error={save.error ?? remove.error} />
        <div className="actions">
          {!isNew ? (
            <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
              Verwijderen
            </button>
          ) : null}
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
      <ConfirmDialog
        open={confirmDelete}
        title="Groep verwijderen?"
        message="De groep verdwijnt. Een groep die al in een mailing is gebruikt, kan niet weg."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate({ to: '/mailing/groepen' }) })}
      />
    </>
  );
}
