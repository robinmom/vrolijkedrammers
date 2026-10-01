import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useAdminParades,
  useApiMutation,
  useCarnivalYears,
  useParadeCategories,
  type AdminParade,
  type CategoryRequest,
  type ParadeCategory,
  type ParadeRequest,
} from '../api/hooks';
import { Dialog } from '../components/Dialog';
import { FixedEntriesCard } from '../components/FixedEntriesCard';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import {
  countBasisLabels,
  formatDate,
  formatDateTime,
  fromLocalInput,
  paradeStatusLabels,
  toLocalInput,
  validationModeLabels,
} from '../format';

function emptyParade(carnivalYearId: number): ParadeRequest {
  return {
    carnivalYearId,
    name: '',
    paradeDate: '',
    startTime: '13:30:00',
    startLocation: null,
    routeDescription: null,
    routeLengthKm: null,
    registrationOpensAt: '',
    registrationClosesAt: '',
    editDeadlineAt: null,
    subjectRequired: true,
    defaultSpacingMeters: 5,
    maxDocumentsPerRegistration: 5,
    maxDocumentSizeMb: 10,
    status: 'Planned',
    infoText: null,
  };
}

// eslint-disable-next-line @typescript-eslint/no-unused-vars -- vaste plekken gaan via een eigen endpoint
const toRequest = ({ fixedEntries, ...p }: AdminParade): ParadeRequest => ({ ...p });

/**
 * Optocht (fase 11a, <c>parade.config</c>): optochten per carnavalsjaar (fase 22a: meer dan één mogelijk) met datum, route, inschrijfperiode en
 * uploadlimieten, plus de categorieën met hun deelnemersgrenzen (OQ-10: alleen de doelgroep telt; OQ-12: 10 = groot).
 */
export function ParadePage() {
  const api = useApi();
  const parades = useAdminParades();
  const years = useCarnivalYears();
  const categories = useParadeCategories();
  const [editing, setEditing] = useState<{ id: string | null; form: ParadeRequest } | null>(null);
  const [category, setCategory] = useState<{ id: number | null; form: CategoryRequest } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  // Optocht verwijderen (besluit 2026-10-01): altijd mogelijk, na het intypen van de naam.
  const [deleting, setDeleting] = useState<{ parade: AdminParade; confirm: string } | null>(null);
  const remove = useApiMutation(
    (v: { id: string; confirmName: string }) =>
      api.DELETE('/api/v1/admin/parades/{id}', { params: { path: { id: v.id }, query: { confirmName: v.confirmName } } }),
    [['admin-parades'], ['jury'], ['results']],
  );
  const activeYear = years.data?.find((y) => y.active);

  const save = useApiMutation(
    (v: { id: string | null; form: ParadeRequest }) =>
      v.id === null
        ? api.POST('/api/v1/admin/parades', { body: v.form })
        : api.PUT('/api/v1/admin/parades/{id}', { params: { path: { id: v.id } }, body: v.form }),
    [['admin-parades']],
  );
  const saveCategory = useApiMutation(
    (v: { id: number | null; form: CategoryRequest }) =>
      v.id === null
        ? api.POST('/api/v1/admin/parade-categories', { body: v.form })
        : api.PUT('/api/v1/admin/parade-categories/{id}', { params: { path: { id: v.id } }, body: v.form }),
    [['parade-categories']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    if (editing) {
      save.mutate(editing, {
        onSuccess: () => {
          setEditing(null);
          setMessage('Optocht opgeslagen.');
        },
      });
    }
  }

  function submitCategory(event: FormEvent) {
    event.preventDefault();
    if (category) {
      saveCategory.mutate(category, {
        onSuccess: () => {
          setCategory(null);
          setMessage('Categorie opgeslagen.');
        },
      });
    }
  }

  const set = (change: Partial<ParadeRequest>) => editing && setEditing({ ...editing, form: { ...editing.form, ...change } });
  const setCat = (change: Partial<CategoryRequest>) => category && setCategory({ ...category, form: { ...category.form, ...change } });
  const yearName = (id: number) => years.data?.find((y) => y.id === id)?.name ?? '';
  const number = (value: string) => (value === '' ? null : Number(value));

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Optocht</h1>
          <p className="page-subtitle">
            Een optocht hoort bij een carnavalsjaar; meestal één per jaar. Leden schrijven hun groep in via de app zolang de
            inschrijving open is; het opgavenummer volgt de volgorde van binnenkomst. Een nieuwe optocht neemt de jury-indeling
            en weging over van de vorige.
          </p>
        </div>
        {activeYear ? (
          <button type="button" className="button" onClick={() => setEditing({ id: null, form: emptyParade(activeYear.id) })}>
            Optocht toevoegen
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={parades.error ?? categories.error} />

      {(parades.data ?? []).map((p) => (
        <section key={p.id} className="card" aria-labelledby={`optocht-${p.id}`}>
          <div className="card-header">
            <h2 id={`optocht-${p.id}`}>
              {p.name} <span className="badge">{paradeStatusLabels[p.status] ?? p.status}</span>
            </h2>
            <span className="actions">
              <button type="button" className="button secondary small" onClick={() => setEditing({ id: p.id, form: toRequest(p) })}>
                Wijzigen <span className="visually-hidden">{p.name}</span>
              </button>
              <button type="button" className="button ghost danger small" onClick={() => setDeleting({ parade: p, confirm: '' })}>
                Verwijderen <span className="visually-hidden">{p.name}</span>
              </button>
            </span>
          </div>
          <dl className="stats">
            <div>
              <dt>Datum en start</dt>
              <dd>
                {formatDate(p.paradeDate)} · {p.startTime.slice(0, 5)}
              </dd>
            </div>
            <div>
              <dt>Carnavalsjaar</dt>
              <dd>{yearName(p.carnivalYearId)}</dd>
            </div>
            <div>
              <dt>Inschrijving</dt>
              <dd>
                {formatDateTime(p.registrationOpensAt)} – {formatDateTime(p.registrationClosesAt)}
              </dd>
            </div>
            <div>
              <dt>Onderwerp verplicht</dt>
              <dd>{p.subjectRequired ? 'Ja' : 'Nee'}</dd>
            </div>
          </dl>
        </section>
      ))}
      {parades.data?.map((p) => <FixedEntriesCard key={`vast-${p.id}`} parade={p} canEdit />)}
      {parades.data?.length === 0 ? <p className="muted">Nog geen optocht ingesteld.</p> : null}

      <section className="card" aria-labelledby="categorieen">
        <div className="card-header">
          <h2 id="categorieen">Categorieën</h2>
          <button
            type="button"
            className="button secondary small"
            onClick={() =>
              setCategory({
                id: null,
                form: {
                  code: '',
                  name: '',
                  ageGroup: 'Adult',
                  type: 'WalkingGroupSmall',
                  minimumParticipants: null,
                  maximumParticipants: null,
                  participantCountBasis: 'AdultsOnly',
                  validationMode: 'Block',
                  hasVehicle: false,
                  active: true,
                  sortOrder: 100,
                },
              })
            }
          >
            Categorie toevoegen
          </button>
        </div>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Categorieën">
          <table className="table">
            <caption className="visually-hidden">Categorieën</caption>
            <thead>
              <tr>
                <th scope="col">Categorie</th>
                <th scope="col">Deelnemers</th>
                <th scope="col">Telt</th>
                <th scope="col">Controle</th>
                <th scope="col">Status</th>
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {(categories.data ?? []).map((c: ParadeCategory) => (
                <tr key={c.id}>
                  <td>
                    {c.name}
                    <div className="muted small-text">{c.code}</div>
                  </td>
                  <td>
                    {c.minimumParticipants ?? 1}
                    {c.maximumParticipants ? `–${c.maximumParticipants}` : '+'}
                  </td>
                  <td>{countBasisLabels[c.participantCountBasis] ?? c.participantCountBasis}</td>
                  <td>{validationModeLabels[c.validationMode] ?? c.validationMode}</td>
                  <td>{c.active ? <span className="badge ok">Actief</span> : 'Inactief'}</td>
                  <td className="actions">
                    <button type="button" className="button secondary small" onClick={() => setCategory({ id: c.id, form: { ...c } })}>
                      Wijzigen <span className="visually-hidden">{c.name}</span>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <Dialog open={editing !== null} title={editing?.id === null ? 'Optocht toevoegen' : 'Optocht wijzigen'} onClose={() => setEditing(null)}>
        {editing ? (
          <form onSubmit={submit}>
            <Field label="Naam" required maxLength={100} value={editing.form.name} onChange={(e) => set({ name: e.target.value })} />
            <div className="field">
              <label htmlFor="optocht-jaar">Carnavalsjaar</label>
              <select id="optocht-jaar" value={editing.form.carnivalYearId} onChange={(e) => set({ carnivalYearId: Number(e.target.value) })}>
                {(years.data ?? []).map((y) => (
                  <option key={y.id} value={y.id}>
                    {y.name}
                    {y.active ? ' (actief)' : ''}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-grid">
              <Field label="Datum" type="date" required value={editing.form.paradeDate} onChange={(e) => set({ paradeDate: e.target.value })} />
              <Field
                label="Starttijd"
                type="time"
                required
                value={editing.form.startTime.slice(0, 5)}
                onChange={(e) => set({ startTime: `${e.target.value}:00` })}
              />
              <Field label="Startlocatie" value={editing.form.startLocation ?? ''} onChange={(e) => set({ startLocation: e.target.value || null })} />
              <Field
                label="Lengte route (km)"
                type="number"
                step="0.1"
                min="0"
                value={editing.form.routeLengthKm ?? ''}
                onChange={(e) => set({ routeLengthKm: number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label htmlFor="optocht-route">Route</label>
              <textarea
                id="optocht-route"
                rows={3}
                value={editing.form.routeDescription ?? ''}
                onChange={(e) => set({ routeDescription: e.target.value || null })}
              />
            </div>
            <div className="form-grid">
              <Field
                label="Inschrijving opent"
                type="datetime-local"
                required
                value={toLocalInput(editing.form.registrationOpensAt)}
                onChange={(e) => set({ registrationOpensAt: fromLocalInput(e.target.value) ?? '' })}
              />
              <Field
                label="Inschrijving sluit"
                type="datetime-local"
                required
                value={toLocalInput(editing.form.registrationClosesAt)}
                onChange={(e) => set({ registrationClosesAt: fromLocalInput(e.target.value) ?? '' })}
              />
              <Field
                label="Wijzigen tot (optioneel)"
                type="datetime-local"
                hint="Daarna kan een groep alleen nog de contactgegevens wijzigen. Leeg: tot de sluiting."
                value={toLocalInput(editing.form.editDeadlineAt)}
                onChange={(e) => set({ editDeadlineAt: fromLocalInput(e.target.value) })}
              />
              <div className="field">
                <label htmlFor="optocht-status">Status</label>
                <select
                  id="optocht-status"
                  value={editing.form.status}
                  onChange={(e) => set({ status: e.target.value as ParadeRequest['status'] })}
                >
                  {Object.entries(paradeStatusLabels).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </div>
              <Field
                label="Standaardafstand tussen groepen (m)"
                type="number"
                step="0.5"
                min="0"
                required
                value={editing.form.defaultSpacingMeters}
                onChange={(e) => set({ defaultSpacingMeters: Number(e.target.value) })}
              />
              <Field
                label="Documenten per inschrijving"
                type="number"
                min="0"
                max="20"
                required
                value={editing.form.maxDocumentsPerRegistration}
                onChange={(e) => set({ maxDocumentsPerRegistration: Number(e.target.value) })}
              />
              <Field
                label="Maximale grootte per document (MB)"
                type="number"
                min="1"
                max="25"
                required
                value={editing.form.maxDocumentSizeMb}
                onChange={(e) => set({ maxDocumentSizeMb: Number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label htmlFor="optocht-info">Informatie over meedoen (Markdown)</label>
              <textarea
                id="optocht-info"
                rows={6}
                maxLength={8000}
                aria-describedby="optocht-info-hint"
                value={editing.form.infoText ?? ''}
                onChange={(e) => set({ infoText: e.target.value || null })}
              />
              <small id="optocht-info-hint" className="muted">
                Leden zonder de rol Groepsverantwoordelijke zien alleen deze tekst in de app, bijvoorbeeld hoe ze zich als groep kunnen aanmelden.
              </small>
            </div>
            <Checkbox
              label="Onderwerp verplicht"
              checked={editing.form.subjectRequired}
              onChange={(e) => set({ subjectRequired: e.target.checked })}
            />
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

      <Dialog open={category !== null} title={category?.id === null ? 'Categorie toevoegen' : 'Categorie wijzigen'} onClose={() => setCategory(null)}>
        {category ? (
          <form onSubmit={submitCategory}>
            <div className="form-grid">
              <Field label="Code" required pattern="^[A-Za-z0-9_]{2,40}$" value={category.form.code} onChange={(e) => setCat({ code: e.target.value })} />
              <Field label="Naam" required maxLength={100} value={category.form.name} onChange={(e) => setCat({ name: e.target.value })} />
              <div className="field">
                <label htmlFor="categorie-leeftijd">Leeftijdsgroep</label>
                <select
                  id="categorie-leeftijd"
                  value={category.form.ageGroup}
                  onChange={(e) => setCat({ ageGroup: e.target.value as CategoryRequest['ageGroup'] })}
                >
                  <option value="Adult">Volwassenen</option>
                  <option value="Youth">Jeugd</option>
                </select>
              </div>
              <div className="field">
                <label htmlFor="categorie-telt">Wie telt mee</label>
                <select
                  id="categorie-telt"
                  value={category.form.participantCountBasis}
                  onChange={(e) => setCat({ participantCountBasis: e.target.value as CategoryRequest['participantCountBasis'] })}
                >
                  {Object.entries(countBasisLabels).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </div>
              <Field
                label="Minimum deelnemers"
                type="number"
                min="0"
                value={category.form.minimumParticipants ?? ''}
                onChange={(e) => setCat({ minimumParticipants: number(e.target.value) })}
              />
              <Field
                label="Maximum deelnemers"
                type="number"
                min="1"
                hint="Leeg: geen maximum"
                value={category.form.maximumParticipants ?? ''}
                onChange={(e) => setCat({ maximumParticipants: number(e.target.value) })}
              />
              <div className="field">
                <label htmlFor="categorie-controle">Controle</label>
                <select
                  id="categorie-controle"
                  value={category.form.validationMode}
                  onChange={(e) => setCat({ validationMode: e.target.value as CategoryRequest['validationMode'] })}
                >
                  {Object.entries(validationModeLabels).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </div>
              <Field
                label="Volgorde"
                type="number"
                value={category.form.sortOrder}
                onChange={(e) => setCat({ sortOrder: Number(e.target.value) })}
              />
            </div>
            <Checkbox label="Met voertuig (wagen)" checked={category.form.hasVehicle} onChange={(e) => setCat({ hasVehicle: e.target.checked })} />
            <Checkbox label="Actief (kiesbaar)" checked={category.form.active} onChange={(e) => setCat({ active: e.target.checked })} />
            <ProblemAlert error={saveCategory.error} />
            <div className="actions">
              <button type="button" className="button secondary" onClick={() => setCategory(null)}>
                Annuleren
              </button>
              <button type="submit" className="button" disabled={saveCategory.isPending}>
                Opslaan
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>
      <Dialog open={deleting !== null} title={`${deleting?.parade.name ?? ''} verwijderen?`} onClose={() => setDeleting(null)}>
        {deleting ? (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              remove.mutate(
                { id: deleting.parade.id, confirmName: deleting.confirm },
                {
                  onSuccess: () => {
                    setMessage(`${deleting.parade.name} is verwijderd.`);
                    setDeleting(null);
                  },
                },
              );
            }}
          >
            <p>
              Dit verwijdert de optocht definitief, met alle inschrijvingen, documenten, de jury-indeling, de scores en de uitslag.
              Dit kan niet ongedaan worden gemaakt.
            </p>
            <Field
              label={`Typ ter bevestiging de naam: ${deleting.parade.name}`}
              required
              autoComplete="off"
              value={deleting.confirm}
              onChange={(e) => setDeleting({ ...deleting, confirm: e.target.value })}
            />
            <ProblemAlert error={remove.error} />
            <div className="actions">
              <button type="button" className="button secondary" onClick={() => setDeleting(null)}>
                Annuleren
              </button>
              <button
                type="submit"
                className="button danger"
                disabled={remove.isPending || deleting.confirm.trim().toLowerCase() !== deleting.parade.name.toLowerCase()}
              >
                Definitief verwijderen
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>
    </>
  );
}
