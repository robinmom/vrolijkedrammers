import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate } from '../format';

const ROLE_LABELS: Record<string, string> = { prins: 'Prins(es)', adjudant: 'Adjudant' };
const INFO_HINTS: Record<string, string> = {
  prins: 'De prins(es) ziet dit in de app onder "Info", na "Welkom prins(es) …".',
  adjudant: 'De adjudanten zien dit in de app onder "Info", na "Welkom adjudant … van prins(es) …".',
};

/**
 * Portal → Prins (2026-10-10): wie dit jaar prins(es) en adjudant is, en de informatie die zij in de app zien. De rollen
 * ken je toe bij Gebruikers (maximaal één prins(es) en twee adjudanten, met een einddatum).
 */
export function RoyalPage() {
  const api = useApi();
  const overview = useQuery({
    queryKey: ['royal'],
    queryFn: async () => (await api.GET('/api/v1/admin/royal')).data,
  });

  return (
    <>
      <h1>Prins en adjudanten</h1>
      <p className="lead">
        De prins(es) en de adjudanten zien in de app een eigen welkom en een knop Info met de tekst hieronder.
      </p>
      <ProblemAlert error={overview.error} />

      <section className="card" aria-labelledby="dit-jaar">
        <h2 id="dit-jaar">Dit carnavalsjaar</h2>
        {overview.data && overview.data.holders.length > 0 ? (
          <ul className="list">
            {overview.data.holders.map((h) => (
              <li key={`${h.roleCode}-${h.userId}`} className="list-row">
                <span className="badge info">{ROLE_LABELS[h.roleCode] ?? h.roleCode}</span>
                <Link to="/gebruikers/$id" params={{ id: h.userId }} className="grow">
                  {h.name}
                </Link>
                <span className="muted small-text">{h.validTo ? `tot ${formatDate(h.validTo)}` : 'zonder einddatum'}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted">Er is nog geen prins(es) of adjudant.</p>
        )}
        <p className="muted small-text">
          Toekennen: <Link to="/gebruikers">Gebruikers</Link> → kies het lid → Rollen → Prins(es) of Adjudant, met als
          einddatum het einde van het carnavalsjaar. Maximaal één prins(es) en twee adjudanten tegelijk.
        </p>
      </section>

      {overview.data?.infos.map((info) => (
        <InfoCard key={info.roleCode} roleCode={info.roleCode} body={info.body} />
      ))}
    </>
  );
}

function InfoCard({ roleCode, body }: { roleCode: string; body: string }) {
  const api = useApi();
  const [text, setText] = useState(body);
  const [saved, setSaved] = useState(false);
  const save = useApiMutation(
    (value: string) =>
      api.PUT('/api/v1/admin/royal/info/{roleCode}', { params: { path: { roleCode } }, body: { body: value } }),
    [['royal']],
  );
  const id = `info-${roleCode}`;

  return (
    <section className="card" aria-labelledby={`${id}-kop`}>
      <h2 id={`${id}-kop`}>Info voor {roleCode === 'prins' ? 'de prins(es)' : 'de adjudanten'}</h2>
      <p className="muted">{INFO_HINTS[roleCode]} Opmaak: **vet**, *schuin*, # kop, - opsomming, [link](https://…).</p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSaved(false);
          save.mutate(text, { onSuccess: () => setSaved(true) });
        }}
      >
        <div className="field">
          <label htmlFor={id}>Tekst</label>
          <textarea
            id={id}
            rows={12}
            maxLength={20000}
            value={text}
            onChange={(e) => {
              setText(e.target.value);
              setSaved(false);
            }}
          />
        </div>
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
          {saved ? <span className="muted">Opgeslagen.</span> : null}
        </div>
      </form>
    </section>
  );
}
