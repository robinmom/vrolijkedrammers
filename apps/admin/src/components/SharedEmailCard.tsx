import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

export const SHARED_EMAIL_KEY = ['shared-email-accounts'];

/** Accounts met hetzelfde e-mailadres (2026-10-11); ook op het dashboard bij "Aandacht nodig". */
export function useSharedEmailConflicts(enabled = true) {
  const api = useApi();
  return useQuery({
    queryKey: SHARED_EMAIL_KEY,
    enabled,
    queryFn: async () => (await api.GET('/api/v1/admin/shared-email-accounts')).data ?? [],
  });
}

/**
 * Eén e-mailadres = één account. Inloggen gaat per e-mailadres; hebben ouder en kind hetzelfde adres, dan houdt de
 * volwassene het account en komt het kind eronder (Mijn kinderen). Dat gebeurt ook vanzelf bij de eerste keer inloggen.
 */
export function SharedEmailCard() {
  const api = useApi();
  const conflicts = useSharedEmailConflicts();
  const [message, setMessage] = useState<string | null>(null);
  const resolve = useApiMutation(
    (email: string) => api.POST('/api/v1/admin/shared-email-accounts/resolve', { body: { email } }),
    [SHARED_EMAIL_KEY, ['account-requests']],
  );
  const items = conflicts.data ?? [];
  // Na het laatste samenvoegen blijft de bevestiging nog staan.
  if (items.length === 0 && !message) return null;

  return (
    <section className="card" aria-labelledby="gedeeld-adres">
      <h2 id="gedeeld-adres">Hetzelfde e-mailadres bij meerdere accounts</h2>
      <p className="muted">
        Iemand kan maar met één account per e-mailadres inloggen. Bij een ouder en een kind houdt de volwassene het
        account en komt het kind onder Mijn kinderen; dat gebeurt ook vanzelf bij de eerste keer inloggen.
      </p>
      <ProblemAlert error={conflicts.error ?? resolve.error} />
      <SuccessMessage message={message} />
      <ul className="list">
        {items.map((c) => (
          <li key={c.email} className="list-row">
            <div className="grow">
              <strong>{c.email}</strong>
              <p className="muted small-text">
                {c.resolvable
                  ? `${c.keepName} houdt het account${c.children.length ? `; ${c.children.join(', ')} komt eronder als kind` : ''}${c.others.length ? `; samengevoegd met ${c.others.join(', ')}` : ''}.`
                  : c.reason}
              </p>
            </div>
            {c.resolvable ? (
              <button
                type="button"
                className="button secondary small"
                disabled={resolve.isPending}
                aria-label={`Samenvoegen: ${c.email}`}
                onClick={() =>
                  resolve.mutate(c.email, { onSuccess: () => setMessage(`${c.email}: samengevoegd tot één account.`) })
                }
              >
                Samenvoegen
              </button>
            ) : (
              <span className="badge warn">Eigen e-mailadres nodig</span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
