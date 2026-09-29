import { useEffect, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type AdminParade } from '../api/hooks';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

type Entry = AdminParade['fixedEntries'][number];

const MAX = 10;

/**
 * Vaste plekken vooraan in de optocht (fase 12c): elk jaar dezelfde, zoals de geluidswagen, de verenigingswagen en het
 * Convent. Ze krijgen startnummer 1, 2, 3 …; de groepen beginnen daarna. Staan ook in de export.
 */
export function FixedEntriesCard({ parade, canEdit }: { parade: AdminParade; canEdit: boolean }) {
  const api = useApi();
  const [entries, setEntries] = useState<Entry[]>(parade.fixedEntries);
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => setEntries(parade.fixedEntries), [parade.fixedEntries]);
  const save = useApiMutation(
    async () =>
      (
        await api.PUT('/api/v1/admin/parades/{id}/fixed-entries', {
          params: { path: { id: parade.id } },
          body: { entries },
        })
      ).data,
    [['admin-parades'], ['parade-composition']],
  );
  const update = (index: number, patch: Partial<Entry>) =>
    setEntries(entries.map((e, i) => (i === index ? { ...e, ...patch } : e)));
  const move = (index: number, delta: number) => {
    const next = [...entries];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item!);
    setEntries(next);
  };

  return (
    <section className="card" aria-labelledby={`vaste-plekken-${parade.id}`}>
      <h2 id={`vaste-plekken-${parade.id}`}>Vaste plekken vooraan</h2>
      <p className="card-hint">
        Deze krijgen startnummer 1 tot en met {entries.length || '…'}; de groepen beginnen bij {entries.length + 1}. Ze
        staan ook bovenaan in de export.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={save.error} />
      <div className="table-scroll" tabIndex={0} role="region" aria-label="Vaste plekken">
        <table className="table">
          <caption className="visually-hidden">Vaste plekken vooraan</caption>
          <thead>
            <tr>
              <th scope="col">Startnummer</th>
              <th scope="col">Naam</th>
              <th scope="col">Volwassenen</th>
              <th scope="col">Kinderen</th>
              <th scope="col">Muziek</th>
              <th scope="col">
                <span className="visually-hidden">Acties</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {entries.map((e, i) => (
              <tr key={i}>
                <td>{i + 1}</td>
                <td>
                  <input
                    aria-label={`Naam vaste plek ${i + 1}`}
                    value={e.name}
                    maxLength={100}
                    disabled={!canEdit}
                    onChange={(ev) => update(i, { name: ev.target.value })}
                  />
                </td>
                <td>
                  <input
                    aria-label={`Volwassenen bij vaste plek ${i + 1}`}
                    type="number"
                    min={0}
                    max={1000}
                    value={e.adultCount}
                    disabled={!canEdit}
                    onChange={(ev) => update(i, { adultCount: Number(ev.target.value) || 0 })}
                  />
                </td>
                <td>
                  <input
                    aria-label={`Kinderen bij vaste plek ${i + 1}`}
                    type="number"
                    min={0}
                    max={1000}
                    value={e.childrenCount}
                    disabled={!canEdit}
                    onChange={(ev) => update(i, { childrenCount: Number(ev.target.value) || 0 })}
                  />
                </td>
                <td>
                  <input
                    type="checkbox"
                    aria-label={`Muziek bij vaste plek ${i + 1}`}
                    checked={e.hasMusic}
                    disabled={!canEdit}
                    onChange={(ev) => update(i, { hasMusic: ev.target.checked })}
                  />
                </td>
                <td>
                  {canEdit ? (
                    <div className="toolbar">
                      <button
                        type="button"
                        className="button ghost small"
                        disabled={i === 0}
                        aria-label={`Vaste plek ${i + 1} omhoog`}
                        onClick={() => move(i, -1)}
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        className="button ghost small"
                        disabled={i === entries.length - 1}
                        aria-label={`Vaste plek ${i + 1} omlaag`}
                        onClick={() => move(i, 1)}
                      >
                        ↓
                      </button>
                      <button
                        type="button"
                        className="button ghost small"
                        aria-label={`Vaste plek ${i + 1} verwijderen`}
                        onClick={() => setEntries(entries.filter((_, j) => j !== i))}
                      >
                        Verwijderen
                      </button>
                    </div>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {canEdit ? (
        <div className="toolbar">
          <button
            type="button"
            className="button secondary small"
            disabled={entries.length >= MAX}
            onClick={() => setEntries([...entries, { name: '', adultCount: 0, childrenCount: 0, hasMusic: false }])}
          >
            + Vaste plek
          </button>
          <button
            type="button"
            className="button"
            disabled={save.isPending || entries.some((e) => !e.name.trim())}
            onClick={() => save.mutate(undefined, { onSuccess: () => setMessage('Vaste plekken opgeslagen.') })}
          >
            Opslaan
          </button>
        </div>
      ) : null}
    </section>
  );
}
