import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { Dialog } from './Dialog';
import { Checkbox } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

const WELCOME_KEY = ['account-welcomes'];

type Selection = Schemas['WelcomeSelection'];
type PickedMember = { id: string; name: string };

/**
 * Welkomstmails (2026-10-12/13, inloggen met wachtwoord): elk lid krijgt een account en een mail met de inloguitleg. Eerst
 * een selectie (bijv. het kader en de gesplitste leden), op 11-11 iedereen. Wie al een welkomstmail kreeg, wordt
 * overgeslagen; opnieuw sturen naar één lid kan op de pagina van het lid.
 */
export function WelcomeMailsCard() {
  const api = useApi();
  const [everyone, setEveryone] = useState(false);
  const [roles, setRoles] = useState<string[]>([]);
  const [groups, setGroups] = useState<string[]>([]);
  const [committee, setCommittee] = useState(false);
  const [split, setSplit] = useState(false);
  const [members, setMembers] = useState<PickedMember[]>([]);
  const [search, setSearch] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const selection: Selection = everyone
    ? { everyone: true, committee: false, split: false }
    : { everyone: false, roleCodes: roles, groupIds: groups, committee, split, memberIds: members.map((m) => m.id) };
  const empty = !everyone && !roles.length && !groups.length && !committee && !split && !members.length;

  const options = useQuery({
    queryKey: [...WELCOME_KEY, 'options'],
    queryFn: async () => (await api.GET('/api/v1/admin/account-welcomes/options')).data ?? null,
  });
  const preview = useQuery({
    queryKey: [...WELCOME_KEY, 'preview', selection],
    enabled: !empty,
    queryFn: async () => (await api.POST('/api/v1/admin/account-welcomes/preview', { body: selection })).data ?? null,
  });
  const found = useQuery({
    queryKey: ['members', 'welcome-search', search],
    enabled: search.trim().length >= 2,
    queryFn: async () =>
      (
        await api.GET('/api/v1/admin/members', {
          params: { query: { search: search.trim(), status: 'Active', pageSize: 8 } },
        })
      ).data?.items ?? [],
  });
  const send = useApiMutation(
    async () => (await api.POST('/api/v1/admin/account-welcomes/send', { body: selection })).data,
    [WELCOME_KEY, ['members']],
  );

  const toggle = (list: string[], set: (v: string[]) => void, key: string) =>
    set(list.includes(key) ? list.filter((k) => k !== key) : [...list, key]);
  const p = empty ? null : preview.data;
  const total = p ? p.members + p.otherAccounts : 0;

  return (
    <section className="card" aria-labelledby="welkomstmails">
      <h2 id="welkomstmails">Welkomstmails</h2>
      <p className="muted">
        Elk lid krijgt een account met een welkomstmail. Daarin staat hoe je de eerste keer inlogt: e-mailadres invullen
        en via &quot;Wachtwoord vergeten?&quot; zelf een wachtwoord kiezen. Kinderen loggen in via het account van hun
        ouder. Stuur eerst een selectie (bijvoorbeeld het kader); wie al een welkomstmail kreeg, wordt later
        overgeslagen.
      </p>
      {p && !p.passwordSignIn ? (
        <p className="muted">Inloggen met wachtwoord staat nog niet aan; zie het runbook accounts-wachtwoord.</p>
      ) : null}

      <div className="chips" role="group" aria-label="Ontvangers">
        <button
          type="button"
          className={`chip${!everyone ? ' active' : ''}`}
          aria-pressed={!everyone}
          onClick={() => setEveryone(false)}
        >
          Selectie
        </button>
        <button
          type="button"
          className={`chip${everyone ? ' active' : ''}`}
          aria-pressed={everyone}
          onClick={() => setEveryone(true)}
        >
          Iedereen
        </button>
      </div>

      {!everyone && options.data ? (
        <div className="welcome-selection">
          <fieldset>
            <legend>Snel kiezen</legend>
            <Checkbox
              label={`Kader op de website (${options.data.committeeMembers})`}
              checked={committee}
              onChange={(e) => setCommittee(e.target.checked)}
            />
            <Checkbox
              label={`Gesplitste leden: hoofdlid en tweede lid (${options.data.splitMembers})`}
              checked={split}
              onChange={(e) => setSplit(e.target.checked)}
            />
          </fieldset>
          <fieldset>
            <legend>Rollen</legend>
            <div className="check-columns">
              {options.data.roles.map((r) => (
                <Checkbox
                  key={r.key}
                  label={r.name}
                  checked={roles.includes(r.key)}
                  onChange={() => toggle(roles, setRoles, r.key)}
                />
              ))}
            </div>
          </fieldset>
          {options.data.groups.length ? (
            <fieldset>
              <legend>Groepen</legend>
              <div className="check-columns">
                {options.data.groups.map((g) => (
                  <Checkbox
                    key={g.key}
                    label={g.name}
                    checked={groups.includes(g.key)}
                    onChange={() => toggle(groups, setGroups, g.key)}
                  />
                ))}
              </div>
            </fieldset>
          ) : null}
          <fieldset>
            <legend>Losse leden</legend>
            <div className="field">
              <label htmlFor="welcome-search">Zoek op naam of lidnummer</label>
              <input
                id="welcome-search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                autoComplete="off"
              />
            </div>
            {found.data?.length ? (
              <ul className="pick-list">
                {found.data
                  .filter((m) => !members.some((picked) => picked.id === m.id))
                  .map((m) => (
                    <li key={m.id}>
                      <button
                        type="button"
                        className="link-button"
                        onClick={() => setMembers([...members, { id: m.id, name: m.fullName }])}
                      >
                        + {m.fullName} ({m.memberNumber})
                      </button>
                    </li>
                  ))}
              </ul>
            ) : null}
            {members.length ? (
              <div className="chips">
                {members.map((m) => (
                  <button
                    key={m.id}
                    type="button"
                    className="chip active"
                    aria-label={`${m.name} verwijderen`}
                    onClick={() => setMembers(members.filter((x) => x.id !== m.id))}
                  >
                    {m.name} ×
                  </button>
                ))}
              </div>
            ) : null}
          </fieldset>
        </div>
      ) : null}
      <ProblemAlert error={options.error ?? preview.error} />

      {empty ? (
        <p className="muted">Kies wie de welkomstmail krijgt.</p>
      ) : p ? (
        <>
          <dl className="details compact-details">
            <dt>Leden die een mail krijgen</dt>
            <dd>{p.members}</dd>
            <dt>Ouders en beheerders zonder lid</dt>
            <dd>{p.otherAccounts}</dd>
            <dt>Al een welkomstmail gehad</dt>
            <dd>{p.alreadySent}</dd>
            <dt>Leden zonder e-mailadres</dt>
            <dd>{p.withoutEmail}</dd>
            <dt>Kinderen (via de ouder)</dt>
            <dd>{p.children}</dd>
            <dt>Zelfde e-mailadres als een ander lid</dt>
            <dd>{p.sharedEmail}</dd>
          </dl>
          {p.recipients.length ? (
            <details>
              <summary>Ontvangers bekijken ({total})</summary>
              <ul className="recipient-list">
                {p.recipients.map((r) => (
                  <li key={r.email + r.name}>
                    {r.name} <span className="muted">· {r.email}</span>
                  </li>
                ))}
              </ul>
            </details>
          ) : null}
        </>
      ) : (
        <p>Laden…</p>
      )}
      <SuccessMessage message={message} />
      <ProblemAlert error={send.error} />
      <button
        type="button"
        className="button"
        disabled={!p?.passwordSignIn || total === 0 || send.isPending}
        onClick={() => setConfirming(true)}
      >
        {everyone ? 'Welkomstmail naar iedereen sturen…' : `Welkomstmail naar deze selectie sturen (${total})…`}
      </button>
      <Dialog
        open={confirming}
        title={everyone ? 'Welkomstmail naar iedereen sturen' : 'Welkomstmail naar de selectie sturen'}
        onClose={() => setConfirming(false)}
      >
        <p>
          {total} mensen krijgen een account en een welkomstmail. De mails gaan één voor één; dat duurt ongeveer{' '}
          {p?.estimatedMinutes ?? 0} minuten. Dit kan niet worden teruggedraaid.
        </p>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setConfirming(false)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button"
            disabled={send.isPending}
            onClick={() =>
              send.mutate(undefined, {
                onSuccess: (data) => {
                  setConfirming(false);
                  setMessage(
                    `${(data as { queued?: number } | undefined)?.queued ?? total} welkomstmails staan in de wachtrij.`,
                  );
                },
              })
            }
          >
            Versturen
          </button>
        </div>
      </Dialog>
    </section>
  );
}
