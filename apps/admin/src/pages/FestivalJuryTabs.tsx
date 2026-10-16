import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

type Category = Schemas['ResultCategory'];

const JURY_KEY = ['dance-festival', 'jury'];
const RESULTS_KEY = ['dance-festival', 'results'];

/** Juryleden van het Dansgardefestival (2026-10-15): vast nummer (anoniem in het rapport), hoofdjury, uitnodigen. */
export function FestivalJuryTab() {
  const api = useApi();
  const jurors = useQuery({
    queryKey: JURY_KEY,
    queryFn: async () => (await api.GET('/api/v1/admin/dance-festival/jury')).data ?? [],
  });
  const [inviting, setInviting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const head = useApiMutation(
    ({ id, headJury }: { id: string; headJury: boolean }) =>
      api.PUT('/api/v1/admin/dance-festival/jury/{userId}/head', {
        params: { path: { userId: id } },
        body: { headJury },
      }),
    [JURY_KEY],
  );
  const resend = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/dance-festival/jury/{userId}/resend', { params: { path: { userId: id } } }),
    [JURY_KEY],
  );
  const remove = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/dance-festival/jury/{userId}', { params: { path: { userId: id } } }),
    [JURY_KEY],
  );

  return (
    <>
      <div className="page-header">
        <h2>Jury</h2>
        <button type="button" className="button" onClick={() => setInviting(true)}>
          + Jurylid uitnodigen
        </button>
      </div>
      <p className="muted small-text">
        Juryleden jureren in de app en beoordelen alle dansen. In het rapport voor de verenigingen staan ze alleen met
        hun nummer (Jurylid 1, 2, 3). De hoofdjury ziet na het publiceren de uitslag in de app en rondt de
        prijsuitreiking af.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={jurors.error ?? head.error ?? resend.error ?? remove.error} />
      <section className="card table-wrapper" aria-label="Juryleden">
        {!jurors.data?.length ? (
          <p className="muted">Nog geen juryleden.</p>
        ) : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Juryleden (tabel)">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Nr.</th>
                  <th scope="col">Naam</th>
                  <th scope="col">E-mail</th>
                  <th scope="col">Hoofdjury</th>
                  <th scope="col">Ingediend</th>
                  <th scope="col">
                    <span className="visually-hidden">Acties</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {jurors.data.map((j) => (
                  <tr key={j.userId} className={j.active ? undefined : 'cancelled'}>
                    <td>Jurylid {j.number}</td>
                    <td>{j.name}</td>
                    <td>{j.email}</td>
                    <td>
                      {j.active ? (
                        <input
                          type="checkbox"
                          aria-label={`Hoofdjury ${j.name}`}
                          checked={j.headJury}
                          onChange={(e) => head.mutate({ id: j.userId, headJury: e.target.checked })}
                        />
                      ) : (
                        <span className="muted">verwijderd</span>
                      )}
                    </td>
                    <td>{j.submittedCategories} categorieën</td>
                    <td className="actions-cell">
                      {j.active ? (
                        <>
                          <button
                            type="button"
                            className="link-button"
                            onClick={() =>
                              resend.mutate(j.userId, {
                                onSuccess: () => setMessage(`Uitnodiging opnieuw naar ${j.email}.`),
                              })
                            }
                          >
                            Opnieuw uitnodigen
                          </button>
                          <button type="button" className="link-button" onClick={() => remove.mutate(j.userId)}>
                            Verwijderen
                          </button>
                        </>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      {inviting ? (
        <InviteDialog
          onClose={(text) => {
            setInviting(false);
            if (text) setMessage(text);
          }}
        />
      ) : null}
    </>
  );
}

function InviteDialog({ onClose }: { onClose: (message?: string) => void }) {
  const api = useApi();
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [headJury, setHeadJury] = useState(false);
  const invite = useApiMutation(
    () => api.POST('/api/v1/admin/dance-festival/jury', { body: { name, email, headJury } }),
    [JURY_KEY],
  );
  return (
    <Dialog open title="Jurylid uitnodigen" onClose={() => onClose()}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          invite.mutate(undefined, { onSuccess: () => onClose(`${name} is uitgenodigd.`) });
        }}
      >
        <p className="muted">
          Het jurylid krijgt een e-mail met uitleg: inloggen in de app met dit e-mailadres en een code per e-mail.
        </p>
        <Field label="Naam" required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        <Field
          label="E-mailadres"
          type="email"
          required
          maxLength={254}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Checkbox label="Hoofdjury" checked={headJury} onChange={(e) => setHeadJury(e.target.checked)} />
        <ProblemAlert error={invite.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={invite.isPending}>
            Uitnodigen
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/**
 * Uitslag van het Dansgardefestival (2026-10-15, Figma P2): per categorie wie heeft ingediend en de ranglijst. Zolang nog
 * niet alles binnen is eerst de vraag of je de uitslag toch wilt bekijken. Daarna: naar de hoofdjury, afdrukken, en de
 * prijsuitreiking afronden (overrulen van de hoofdjury).
 */
export function FestivalResultsTab() {
  const api = useApi();
  const results = useQuery({
    queryKey: RESULTS_KEY,
    queryFn: async () => (await api.GET('/api/v1/admin/dance-festival/results')).data ?? null,
    refetchInterval: 15_000,
  });
  const [confirmed, setConfirmed] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);
  const publish = useApiMutation(
    (force: boolean) => api.POST('/api/v1/admin/dance-festival/results/head-jury', { body: { force } }),
    [RESULTS_KEY],
  );
  const awards = useApiMutation(
    (completed: boolean) => api.POST('/api/v1/admin/dance-festival/results/awards', { body: { completed } }),
    [RESULTS_KEY],
  );

  const r = results.data;
  if (!r) return results.error ? <ProblemAlert error={results.error} /> : <p>Laden…</p>;
  const done = r.categories.filter((c) => c.complete).length;
  const active = r.jurors.filter((j) => j.active).length;

  if (!r.complete && !confirmed) {
    return (
      <section className="card" aria-labelledby="uitslag-onvolledig">
        <h2 id="uitslag-onvolledig">Uitslag nog niet compleet</h2>
        <p>
          {done} van {r.categories.length} categorieën zijn door alle {active} juryleden ingediend. Weet je zeker dat je
          de uitslag nu wilt bekijken? Ontbrekende punten tellen nog niet mee.
        </p>
        <div className="actions">
          <button type="button" className="button" onClick={() => setConfirmed(true)}>
            Ja, uitslag bekijken
          </button>
        </div>
      </section>
    );
  }

  async function exportPdf() {
    setDownloadError(null);
    try {
      const response = await api.GET('/api/v1/admin/dance-festival/results/pdf', { parseAs: 'blob' });
      if (response.data) {
        const url = URL.createObjectURL(response.data as Blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'uitslag-dansgardefestival.pdf';
        a.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  const current = r.categories.find((c) => c.key === selected) ?? r.categories[0] ?? null;
  const steps: [string, boolean][] = [
    ['Jury dient in', r.complete],
    ['Naar hoofdjury', Boolean(r.headJuryPublishedAt)],
    ['Prijsuitreiking afgerond', Boolean(r.awardsCompletedAt)],
  ];

  return (
    <>
      <div className="kpis">
        <div className="kpi">
          <span className="kpi-label">Juryleden</span>
          <span className="kpi-value">{active}</span>
          <span className="kpi-hint">beoordelen alle dansen</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Categorieën compleet</span>
          <span className="kpi-value">
            {done} / {r.categories.length}
          </span>
          <span className="kpi-hint">alle juryleden ingediend</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Hoofdjury</span>
          <span className="kpi-value">{r.headJuryPublishedAt ? 'ontvangen' : 'nog niet'}</span>
          <span className="kpi-hint">
            {r.headJuryPublishedAt ? formatDateTime(r.headJuryPublishedAt) : 'uitslag in de app'}
          </span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Prijsuitreiking</span>
          <span className="kpi-value">{r.awardsCompletedAt ? 'afgerond' : 'nog niet'}</span>
          <span className="kpi-hint">
            {r.awardsCompletedAt ? `door ${r.awardsCompletedBy ?? 'hoofdjury'}` : 'de hoofdjury rondt af'}
          </span>
        </div>
      </div>

      <div className="page-header">
        <h2>Uitslag</h2>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => void exportPdf()}>
            Exporteren (PDF)
          </button>
          <button type="button" className="button" onClick={() => setPublishing(true)}>
            {r.headJuryPublishedAt ? 'Opnieuw naar hoofdjury' : 'Publiceer naar hoofdjury'}
          </button>
        </div>
      </div>
      <ol className="chips steps" aria-label="Stappen">
        {steps.map(([label, ok], i) => (
          <li key={label} className={`badge ${ok ? 'ok' : 'neutral'}`}>
            {i + 1} {label}
            {ok ? ' ✓' : ''}
          </li>
        ))}
      </ol>
      <SuccessMessage message={message} />
      <ProblemAlert error={publish.error ?? awards.error ?? downloadError} />

      <div className="results-grid">
        <section className="card table-wrapper" aria-label="Categorieën">
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Categorieën (tabel)">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Categorie</th>
                  <th scope="col">Deel</th>
                  <th scope="col">Dansen</th>
                  <th scope="col">Jury</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {r.categories.map((c) => (
                  <tr key={c.key} className={current?.key === c.key ? 'selected' : undefined}>
                    <th scope="row">
                      <button type="button" className="link-button" onClick={() => setSelected(c.key)}>
                        {c.name}
                      </button>
                    </th>
                    <td>{c.part}</td>
                    <td>{c.rows.length}</td>
                    <td>
                      {c.submitted}/{c.jurors}
                    </td>
                    <td>
                      <span className={`badge ${c.complete ? 'ok' : 'warn'}`}>{c.complete ? 'Compleet' : 'Bezig'}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
        {current ? <CategoryDetail category={current} /> : null}
      </div>

      <section className="card" aria-labelledby="prijsuitreiking">
        <h2 id="prijsuitreiking">Prijsuitreiking</h2>
        <p className="muted">
          De hoofdjury rondt de prijsuitreiking af in de app. Lukt dat niet, of is het per ongeluk gebeurd, dan kun je
          het hier zetten of terugdraaien. Pas daarna kunnen de uitslag op de website en de rapporten naar de
          verenigingen.
        </p>
        <button
          type="button"
          className="button secondary"
          disabled={awards.isPending}
          onClick={() =>
            awards.mutate(!r.awardsCompletedAt, {
              onSuccess: () =>
                setMessage(r.awardsCompletedAt ? 'Prijsuitreiking weer open.' : 'Prijsuitreiking staat op afgerond.'),
            })
          }
        >
          {r.awardsCompletedAt ? 'Prijsuitreiking weer openzetten' : 'Prijsuitreiking afgerond (overrulen)'}
        </button>
      </section>

      <AfterAwards results={r} onMessage={setMessage} />

      <ConfirmDialog
        open={publishing}
        title={r.complete ? 'Uitslag naar de hoofdjury?' : 'Uitslag nog niet compleet'}
        message={
          r.complete
            ? 'De hoofdjury ziet de uitslag dan in de app, per categorie van de laatste plaats naar de winnaar.'
            : `Nog niet alle juryleden hebben alles ingediend (${done} van ${r.categories.length} categorieën compleet). Weet je zeker dat je de uitslag nu naar de hoofdjury stuurt?`
        }
        confirmLabel={r.complete ? 'Publiceren' : 'Toch publiceren'}
        busy={publish.isPending}
        onCancel={() => setPublishing(false)}
        onConfirm={() =>
          publish.mutate(!r.complete, {
            onSuccess: () => setMessage('De uitslag staat klaar voor de hoofdjury.'),
            onSettled: () => setPublishing(false),
          })
        }
      />
    </>
  );
}

/** Na de prijsuitreiking: de uitslag op de website (nieuwsbericht) en de juryrapporten naar de verenigingen. */
function AfterAwards({
  results,
  onMessage,
}: {
  results: Schemas['FestivalResults'];
  onMessage: (text: string) => void;
}) {
  const api = useApi();
  const status = useQuery({
    queryKey: [...RESULTS_KEY, 'publish'],
    queryFn: async () => (await api.GET('/api/v1/admin/dance-festival/results/publish')).data ?? null,
  });
  const [sending, setSending] = useState(false);
  const [previewError, setPreviewError] = useState<unknown>(null);
  const website = useApiMutation(() => api.POST('/api/v1/admin/dance-festival/results/website'), [RESULTS_KEY]);
  const reports = useApiMutation(
    async () => (await api.POST('/api/v1/admin/dance-festival/results/reports')).data?.queued ?? 0,
    [RESULTS_KEY],
  );
  const open = Boolean(results.awardsCompletedAt);
  const s = status.data;
  const clubs = new Map(results.categories.flatMap((c) => c.rows.map((r) => [r.registrationId, r.club] as const)));

  async function preview(registrationId: string, club: string) {
    setPreviewError(null);
    try {
      const response = await api.GET('/api/v1/admin/dance-festival/results/reports/{registrationId}/pdf', {
        params: { path: { registrationId } },
        parseAs: 'blob',
      });
      if (response.data) {
        const url = URL.createObjectURL(response.data as Blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `juryrapport-${club}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setPreviewError(error);
    }
  }

  return (
    <section className="card" aria-labelledby="na-prijsuitreiking">
      <h2 id="na-prijsuitreiking">Website en rapporten</h2>
      <p className="muted">
        {open
          ? 'De prijsuitreiking is afgerond. Zet de uitslag op de website (een nieuwsbericht met plaatsen en punten) en stuur elke vereniging haar juryrapport.'
          : 'Kan pas na de prijsuitreiking: de hoofdjury rondt die af in de app (of zet hem hierboven op afgerond).'}
      </p>
      <ProblemAlert error={website.error ?? reports.error ?? previewError} />
      <div className="actions">
        <button
          type="button"
          className="button secondary"
          disabled={!open || website.isPending}
          onClick={() =>
            website.mutate(undefined, {
              onSuccess: () => {
                onMessage('De uitslag staat op de website.');
                void status.refetch();
              },
            })
          }
        >
          {s?.newsId ? 'Website bijwerken' : 'Publiceer op website'}
        </button>
        <button type="button" className="button" disabled={!open || reports.isPending} onClick={() => setSending(true)}>
          Rapporten versturen
        </button>
      </div>
      {s ? (
        <p className="muted small-text">
          {s.newsSlug ? (
            <>
              Op de website:{' '}
              <a href={`/nieuws/${s.newsSlug}`} target="_blank" rel="noreferrer">
                /nieuws/{s.newsSlug}
              </a>
              {' · '}
            </>
          ) : null}
          Rapporten verstuurd: {s.reportsSent} van {s.registrations} inschrijvingen
        </p>
      ) : null}
      {clubs.size > 0 ? (
        <details>
          <summary>Rapport bekijken per vereniging</summary>
          <ul className="list">
            {[...clubs.entries()].map(([id, club]) => (
              <li key={id} className="list-row">
                <span className="grow">{club}</span>
                <button type="button" className="link-button" onClick={() => void preview(id, club)}>
                  Rapport (PDF)
                </button>
              </li>
            ))}
          </ul>
        </details>
      ) : null}
      <ConfirmDialog
        open={sending}
        title="Rapporten versturen?"
        message="Elke vereniging met goedgekeurde dansen krijgt een e-mail van dansgardefestival@vrolijkedrammers.nl met haar juryrapport (PDF). Wie er al een heeft gekregen, krijgt geen tweede."
        confirmLabel="Versturen"
        busy={reports.isPending}
        onCancel={() => setSending(false)}
        onConfirm={() =>
          reports.mutate(undefined, {
            onSuccess: (count) => {
              onMessage(`${count as number} rapporten worden verstuurd.`);
              void status.refetch();
            },
            onSettled: () => setSending(false),
          })
        }
      />
    </section>
  );
}

function CategoryDetail({ category }: { category: Category }) {
  return (
    <section className="card" aria-labelledby="uitslag-categorie">
      <div className="card-header">
        <h2 id="uitslag-categorie">{category.name}</h2>
        <span className={`badge ${category.complete ? 'ok' : 'warn'}`}>
          {category.complete ? 'Compleet' : `${category.submitted} van ${category.jurors} juryleden`}
        </span>
      </div>
      <p className="muted small-text">
        Totaal = som van de 4 onderdelen van alle juryleden. Gelijk totaal: hoogste uitvoering wint.
      </p>
      <ol className="ranking">
        {category.rows.map((row) => (
          <li key={row.entryId} className={row.place === 1 ? 'winner' : undefined}>
            <strong className="ranking-place">{row.place}e</strong>
            <span className="grow">
              <strong>{row.title ? `${row.title} · ${row.names}` : row.names}</strong>
              <span className="muted small-text"> {row.club}</span>
            </span>
            <span className="ranking-total">{row.total}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}
