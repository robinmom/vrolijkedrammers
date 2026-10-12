import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { Dialog } from './Dialog';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

const WELCOME_KEY = ['account-welcomes'];

/**
 * Welkomstmail naar iedereen (2026-10-12, inloggen met wachtwoord): elk huidig lid krijgt een account en een mail met de
 * inloguitleg; ouders en beheerders zonder lid ook. Het bestuur kiest het moment. Wie al een welkomstmail kreeg, wordt
 * overgeslagen; opnieuw sturen naar één lid kan op de pagina van het lid.
 */
export function WelcomeMailsCard() {
  const api = useApi();
  const preview = useQuery({
    queryKey: WELCOME_KEY,
    queryFn: async () => (await api.GET('/api/v1/admin/account-welcomes/preview')).data ?? null,
  });
  const [confirming, setConfirming] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const send = useApiMutation(
    async () => (await api.POST('/api/v1/admin/account-welcomes/send-all')).data,
    [WELCOME_KEY, ['members']],
  );

  const p = preview.data;
  if (!p) return preview.error ? <ProblemAlert error={preview.error} /> : null;
  const total = p.members + p.otherAccounts;

  return (
    <section className="card" aria-labelledby="welkomstmails">
      <h2 id="welkomstmails">Welkomstmails</h2>
      <p className="muted">
        Elk lid krijgt een account met een welkomstmail. Daarin staat hoe je de eerste keer inlogt: e-mailadres invullen
        en via &quot;Wachtwoord vergeten?&quot; zelf een wachtwoord kiezen. Kinderen loggen in via het account van hun
        ouder.
      </p>
      {!p.passwordSignIn ? (
        <p className="muted">Inloggen met wachtwoord staat nog niet aan; zie het runbook accounts-wachtwoord.</p>
      ) : null}
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
      <SuccessMessage message={message} />
      <ProblemAlert error={send.error} />
      <button
        type="button"
        className="button"
        disabled={!p.passwordSignIn || total === 0 || send.isPending}
        onClick={() => setConfirming(true)}
      >
        Welkomstmail naar iedereen sturen…
      </button>
      <Dialog open={confirming} title="Welkomstmail naar iedereen sturen" onClose={() => setConfirming(false)}>
        <p>
          {total} mensen krijgen een account en een welkomstmail. De mails gaan één voor één; dat duurt ongeveer{' '}
          {p.estimatedMinutes} minuten. Dit kan niet worden teruggedraaid.
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
