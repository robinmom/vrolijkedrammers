import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useCarnivalYears, type CarnivalYear } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

type YearForm = Omit<CarnivalYear, 'id' | 'active'>;
const emptyYear: YearForm = { name: '', startDate: '', endDate: '', carnivalStartDate: '', carnivalEndDate: '' };

/** yyyy-mm-dd plus een aantal dagen. */
function addDays(date: string, days: number) {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Een nieuw jaar sluit aan op het nieuwste jaar: het begint de dag erna (fase 21g). */
function nextYear(years: CarnivalYear[]): YearForm {
  const newest = [...years].sort((a, b) => b.startDate.localeCompare(a.startDate))[0];
  if (!newest) return emptyYear;
  const [first, second] = newest.name.split('/').map(Number);
  return { ...emptyYear, name: first && second ? `${first + 1}/${second + 1}` : '', startDate: addDays(newest.endDate, 1) };
}

/**
 * Carnavalsjaren; activeren maakt de andere jaren inactief (altijd precies één actief). De jaren sluiten op elkaar aan:
 * wijzig je het einde van een jaar, dan schuift het begin van het volgende jaar mee.
 */
export function CarnivalYearsPage() {
  const api = useApi();
  const years = useCarnivalYears();
  const [editing, setEditing] = useState<{ id: number | null; form: YearForm } | null>(null);
  const [activating, setActivating] = useState<CarnivalYear | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const save = useApiMutation(
    (v: { id: number | null; form: YearForm }) =>
      v.id === null
        ? api.POST('/api/v1/admin/carnival-years', { body: v.form })
        : api.PUT('/api/v1/admin/carnival-years/{id}', { params: { path: { id: v.id } }, body: v.form }),
    [['carnival-years']],
  );
  const activate = useApiMutation(
    (id: number) => api.POST('/api/v1/admin/carnival-years/{id}/activate', { params: { path: { id } } }),
    [['carnival-years'], ['dashboard']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    if (editing) {
      save.mutate(editing, {
        onSuccess: () => {
          setEditing(null);
          setMessage('Carnavalsjaar opgeslagen.');
        },
      });
    }
  }

  return (
    <>
      <div className="page-header">
        <h1>Carnavalsjaren</h1>
        <button type="button" className="button" onClick={() => setEditing({ id: null, form: nextYear(years.data ?? []) })}>
          Carnavalsjaar toevoegen
        </button>
      </div>
      <p className="muted">
        De jaren sluiten op elkaar aan: een nieuw jaar begint de dag na het vorige. Nieuws en foto's van het actieve jaar staan
        vooraan op de website en in de app; oudere jaren staan onder een knop met het jaartal.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={years.error ?? activate.error} />
      <div className="table-scroll" tabIndex={0} role="region" aria-label="Carnavalsjaren">
        <table className="table">
          <caption className="visually-hidden">Carnavalsjaren</caption>
          <thead>
            <tr>
              <th scope="col">Jaar</th>
              <th scope="col">Seizoen</th>
              <th scope="col">Carnaval</th>
              <th scope="col">Status</th>
              <th scope="col">
                <span className="visually-hidden">Acties</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(years.data ?? []).map((year) => (
              <tr key={year.id}>
                <td>{year.name}</td>
                <td>
                  {formatDate(year.startDate)} – {formatDate(year.endDate)}
                </td>
                <td>
                  {formatDate(year.carnivalStartDate)} – {formatDate(year.carnivalEndDate)}
                </td>
                <td>{year.active ? <span className="badge ok">Actief</span> : 'Inactief'}</td>
                <td className="actions">
                  <button type="button" className="button secondary small" onClick={() => setEditing({ id: year.id, form: year })}>
                    Wijzigen <span className="visually-hidden">{year.name}</span>
                  </button>
                  {!year.active ? (
                    <button type="button" className="button small" onClick={() => setActivating(year)}>
                      Activeren <span className="visually-hidden">{year.name}</span>
                    </button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Dialog open={editing !== null} title={editing?.id === null ? 'Carnavalsjaar toevoegen' : 'Carnavalsjaar wijzigen'} onClose={() => setEditing(null)}>
        {editing ? (
          <form onSubmit={submit}>
            <Field label="Naam" hint="bijv. 2027/2028" pattern="^\d{4}/\d{4}$" required value={editing.form.name} onChange={(e) => setEditing({ ...editing, form: { ...editing.form, name: e.target.value } })} />
            <div className="form-grid">
              <Field
                label="Seizoen vanaf"
                type="date"
                required
                hint={editing.id === null && (years.data ?? []).length > 0 ? 'De dag na het einde van het vorige jaar.' : 'Het vorige jaar eindigt de dag ervoor.'}
                value={editing.form.startDate} onChange={(e) => setEditing({ ...editing, form: { ...editing.form, startDate: e.target.value } })} />
              <Field label="Seizoen tot" type="date" required hint="Bijvoorbeeld Aswoensdag. Het volgende jaar begint de dag erna." value={editing.form.endDate} onChange={(e) => setEditing({ ...editing, form: { ...editing.form, endDate: e.target.value } })} />
              <Field label="Carnaval vanaf" type="date" required value={editing.form.carnivalStartDate} onChange={(e) => setEditing({ ...editing, form: { ...editing.form, carnivalStartDate: e.target.value } })} />
              <Field label="Carnaval tot" type="date" required value={editing.form.carnivalEndDate} onChange={(e) => setEditing({ ...editing, form: { ...editing.form, carnivalEndDate: e.target.value } })} />
            </div>
            <ProblemAlert error={save.error} />
            <div className="actions">
              <button type="button" className="button secondary" onClick={() => setEditing(null)}>
                Annuleren
              </button>
              <button type="submit" className="button" disabled={save.isPending}>
                Opslaan
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>
      <ConfirmDialog
        open={activating !== null}
        title={`${activating?.name ?? ''} activeren?`}
        message="Het huidige actieve jaar wordt inactief. De app toont daarna de datums van dit jaar."
        confirmLabel="Activeren"
        busy={activate.isPending}
        onCancel={() => setActivating(null)}
        onConfirm={() =>
          activating &&
          activate.mutate(activating.id, {
            onSettled: () => setActivating(null),
            onSuccess: () => setMessage(`${activating.name} is nu actief.`),
          })
        }
      />
    </>
  );
}
