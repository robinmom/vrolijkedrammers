import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  GROUP_KEYS,
  useApiMutation,
  useCarnivalGroup,
  useCarnivalGroups,
  useMe,
  useUnlinkedRegistrations,
  type CarnivalGroupDetail,
  type CarnivalGroupRequest,
  type CarnivalGroupYear,
  type CarnivalGroupYearRequest,
  type UnlinkedRegistration,
} from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime, registrationStatusLabels } from '../format';
import { useQuery } from '@tanstack/react-query';

/** De categorieën om bij een jaar te kiezen; op deze eigen route, dus ook zonder parade.config. */
function useGroupCategories() {
  const api = useApi();
  return useQuery({
    queryKey: ['parade-groups', 'categories'],
    queryFn: async () => (await api.GET('/api/v1/admin/parade-groups/categories')).data ?? [],
  });
}

const thisYear = new Date().getFullYear();

/** Hoeveel jaar de groep meedoet, gerekend vanaf het eerste jaar dat we kennen. */
function yearsParticipating(since: number | null, firstYear: number | null) {
  const start = since ?? firstYear;
  return start === null ? null : Math.max(1, thisYear - start + 1);
}

/**
 * Optocht → Carnavalsgroepen (2026-11-17): het register van de groepen van het dorp. Per jaar hoe de wagen heette, in
 * welke categorie ze meededen en op welke plaats ze eindigden; zodra een groep zich inschrijft hangen de gegevens van
 * die inschrijving er zelf bij.
 */
export function CarnivalGroupsPage() {
  const me = useMe();
  const [search, setSearch] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const groups = useCarnivalGroups(search, includeInactive);
  const canEdit = (me.data?.permissions ?? []).includes('parade.manage');
  const unlinked = useUnlinkedRegistrations(canEdit);
  const [creating, setCreating] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const list = groups.data ?? [];

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Carnavalsgroepen</h1>
          <p className="muted">
            Alle groepen die meedoen aan de optocht: wagens, loopgroepen en individuele deelnemers. Per jaar staat hier
            hoe de wagen heette, in welke categorie ze meededen en op welke plaats ze eindigden.
          </p>
        </div>
        {canEdit ? (
          <button type="button" className="button" onClick={() => setCreating(true)}>
            Groep toevoegen
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={groups.error} />

      {canEdit && (unlinked.data ?? []).length > 0 ? (
        <UnlinkedCard registrations={unlinked.data ?? []} onDone={setMessage} />
      ) : null}

      <div className="toolbar">
        <div className="field grow-field">
          <label htmlFor="groep-zoeken">Zoeken</label>
          <input id="groep-zoeken" type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Naam van de groep" />
        </div>
        <Checkbox
          label="Ook groepen die niet meer meedoen"
          checked={includeInactive}
          onChange={(e) => setIncludeInactive(e.target.checked)}
        />
      </div>

      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Carnavalsgroepen">
        <table className="table">
          <caption className="visually-hidden">Carnavalsgroepen</caption>
          <thead>
            <tr>
              <th scope="col">Groep</th>
              <th scope="col">Doet mee sinds</th>
              <th scope="col">Jaren bekend</th>
              <th scope="col">Laatste deelname</th>
              <th scope="col">Dit jaar</th>
            </tr>
          </thead>
          <tbody>
            {list.map((g) => {
              const years = yearsParticipating(g.participatingSince, g.firstYear);
              return (
                <tr key={g.id}>
                  <td>
                    <Link to="/optocht/groepen/$id" params={{ id: g.id }}>
                      {g.name}
                    </Link>
                    {g.active ? null : <span className="badge"> gestopt</span>}
                  </td>
                  <td>
                    {g.participatingSince ?? g.firstYear ?? <span className="muted">onbekend</span>}
                    {years === null ? null : <span className="muted"> ({years} jaar)</span>}
                  </td>
                  <td>{g.yearCount}</td>
                  <td>
                    {g.lastYear ? (
                      <>
                        {g.lastYear}
                        {g.lastFloatName ? ` · ${g.lastFloatName}` : ''}
                        {g.lastPlace ? ` · ${g.lastPlace}e plaats` : ''}
                      </>
                    ) : (
                      <span className="muted">–</span>
                    )}
                  </td>
                  <td>{g.registeredForCurrentParade ? <span className="badge ok">Ingeschreven</span> : <span className="muted">–</span>}</td>
                </tr>
              );
            })}
            {list.length === 0 ? (
              <tr>
                <td colSpan={5} className="muted">
                  {search ? 'Geen groep met deze naam.' : 'Nog geen groepen. Voeg ze toe, of koppel een inschrijving aan een nieuwe groep.'}
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
      {creating ? <GroupDialog group={null} onClose={(saved) => { setCreating(false); if (saved) setMessage(saved); }} /> : null}
    </>
  );
}

/** Inschrijvingen die nog aan geen groep hangen; met één klik aan de voorgestelde of een nieuwe groep. */
function UnlinkedCard({ registrations, onDone }: { registrations: UnlinkedRegistration[]; onDone: (message: string) => void }) {
  const api = useApi();
  const groups = useCarnivalGroups('', false);
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const link = useApiMutation(
    async ({ registrationId, groupId, createNew }: { registrationId: string; groupId: string | null; createNew: boolean }) =>
      api.POST('/api/v1/admin/parade-groups/link', { body: { registrationId, groupId, createNew } }),
    GROUP_KEYS,
  );

  return (
    <section className="card accent-gold" aria-labelledby="nog-koppelen">
      <h2 id="nog-koppelen">Nog koppelen ({registrations.length})</h2>
      <p className="muted">
        Deze inschrijvingen hebben een naam die niet precies bij een groep in het register past. Koppel ze, dan staan de
        gegevens bij de juiste groep.
      </p>
      <ProblemAlert error={link.error ?? groups.error} />
      <ul className="list">
        {registrations.map((r) => (
          <li key={r.id}>
            <strong>{r.groupName ?? 'Zonder naam'}</strong>{' '}
            <span className="muted">
              {r.year}
              {r.registrationNumber ? ` · nr. ${r.registrationNumber}` : ''}
              {r.contactName ? ` · ${r.contactName}` : ''}
            </span>
            <div className="actions">
              {r.suggestedGroupId ? (
                <button
                  type="button"
                  className="button small"
                  disabled={link.isPending}
                  onClick={() =>
                    link.mutate(
                      { registrationId: r.id, groupId: r.suggestedGroupId, createNew: false },
                      { onSuccess: () => onDone(`Gekoppeld aan ${r.suggestedGroupName}.`) },
                    )
                  }
                >
                  Koppelen aan {r.suggestedGroupName}
                </button>
              ) : null}
              <div className="field">
                <label htmlFor={`groep-${r.id}`} className="visually-hidden">
                  Groep voor {r.groupName ?? 'deze inschrijving'}
                </label>
                <select
                  id={`groep-${r.id}`}
                  value={chosen[r.id] ?? ''}
                  onChange={(e) => setChosen({ ...chosen, [r.id]: e.target.value })}
                >
                  <option value="">Kies een andere groep…</option>
                  {(groups.data ?? []).map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.name}
                    </option>
                  ))}
                </select>
              </div>
              <button
                type="button"
                className="button secondary small"
                disabled={!chosen[r.id] || link.isPending}
                onClick={() =>
                  link.mutate(
                    { registrationId: r.id, groupId: chosen[r.id] ?? null, createNew: false },
                    { onSuccess: () => onDone('Gekoppeld.') },
                  )
                }
              >
                Koppelen
              </button>
              <button
                type="button"
                className="button ghost small"
                disabled={link.isPending}
                onClick={() =>
                  link.mutate(
                    { registrationId: r.id, groupId: null, createNew: true },
                    { onSuccess: () => onDone(`Nieuwe groep ${r.groupName} aangemaakt.`) },
                  )
                }
              >
                Nieuwe groep
              </button>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Eén groep: de gegevens, de jaren en bij elk jaar de inschrijving die erbij hoort. */
export function CarnivalGroupDetailPage() {
  const { id } = useParams({ from: '/optocht/groepen/$id' });
  const api = useApi();
  const navigate = useNavigate();
  const me = useMe();
  const group = useCarnivalGroup(id);
  const [editing, setEditing] = useState(false);
  const [year, setYear] = useState<CarnivalGroupYear | 'nieuw' | null>(null);
  const [removingYear, setRemovingYear] = useState<CarnivalGroupYear | null>(null);
  const [removing, setRemoving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const canEdit = (me.data?.permissions ?? []).includes('parade.manage');
  const keys = [...GROUP_KEYS, ['parade-group', id]];
  const deleteYear = useApiMutation(
    async (y: number) => api.DELETE('/api/v1/admin/parade-groups/{id}/years/{year}', { params: { path: { id, year: y } } }),
    keys,
  );
  const deleteGroup = useApiMutation(async () => api.DELETE('/api/v1/admin/parade-groups/{id}', { params: { path: { id } } }), GROUP_KEYS);
  const unlink = useApiMutation(
    async (registrationId: string) => api.POST('/api/v1/admin/parade-groups/unlink', { body: { registrationId } }),
    keys,
  );

  if (!group.data) {
    return <>{group.error ? <ProblemAlert error={group.error} /> : <p>Laden…</p>}</>;
  }
  const g = group.data;
  const years = yearsParticipating(g.participatingSince, g.firstYear);

  return (
    <>
      <p>
        <Link to="/optocht/groepen">← Carnavalsgroepen</Link>
      </p>
      <div className="page-header">
        <div>
          <h1>{g.name}</h1>
          <p className="muted">
            {g.participatingSince ?? g.firstYear ? (
              <>
                Doet mee sinds {g.participatingSince ?? g.firstYear}
                {years === null ? '' : ` · ${years} jaar`}
              </>
            ) : (
              'Sinds wanneer ze meedoen is niet bekend; vul het in bij Wijzigen.'
            )}
            {g.active ? '' : ' · doet niet meer mee'}
          </p>
        </div>
        {canEdit ? (
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setEditing(true)}>
              Wijzigen
            </button>
            <button type="button" className="button" onClick={() => setYear('nieuw')}>
              Jaar toevoegen
            </button>
            <button type="button" className="button danger" onClick={() => setRemoving(true)}>
              Verwijderen
            </button>
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={deleteYear.error ?? deleteGroup.error ?? unlink.error} />

      {g.contactName || g.contactPhone || g.contactEmail || g.notes ? (
        <section className="card" aria-labelledby="groep-gegevens">
          <h2 id="groep-gegevens">Vaste gegevens</h2>
          <dl className="details">
            {g.contactName ? (
              <>
                <dt>Contactpersoon</dt>
                <dd>{g.contactName}</dd>
              </>
            ) : null}
            {g.contactPhone ? (
              <>
                <dt>Telefoon</dt>
                <dd>{g.contactPhone}</dd>
              </>
            ) : null}
            {g.contactEmail ? (
              <>
                <dt>E-mail</dt>
                <dd>{g.contactEmail}</dd>
              </>
            ) : null}
            {g.notes ? (
              <>
                <dt>Opmerking</dt>
                <dd>{g.notes}</dd>
              </>
            ) : null}
          </dl>
        </section>
      ) : null}

      <h2>Per jaar ({g.years.length})</h2>
      {g.years.length === 0 ? (
        <p className="muted">
          Nog geen jaren bekend. Voeg de jaren van vóór de app zelf toe; nieuwe jaren komen er vanzelf bij zodra de groep
          zich inschrijft.
        </p>
      ) : null}
      {g.years.map((y) => (
        <section key={y.year} className="card" aria-labelledby={`jaar-${y.year}`}>
          <div className="page-header">
            <div>
              <h3 id={`jaar-${y.year}`}>
                {y.year}
                {y.floatName ? ` · ${y.floatName}` : ''}
              </h3>
              <p className="muted">
                {y.categoryName ?? 'Categorie onbekend'}
                {y.place ? ` · ${y.place}e plaats` : ''}
                {y.place && y.placeFromResults ? ' (uit de uitslag)' : ''}
                {y.registration ? '' : ' · geen inschrijving in het systeem'}
              </p>
            </div>
            {canEdit ? (
              <div className="actions">
                <button type="button" className="button secondary small" onClick={() => setYear(y)}>
                  Bewerken <span className="visually-hidden">{y.year}</span>
                </button>
                {y.hasOwnRow ? (
                  <button type="button" className="button ghost small" onClick={() => setRemovingYear(y)}>
                    Jaar weghalen <span className="visually-hidden">{y.year}</span>
                  </button>
                ) : null}
              </div>
            ) : null}
          </div>
          {y.notes ? <p>{y.notes}</p> : null}
          {y.registration ? (
            <>
              <dl className="details">
                <dt>Inschrijving</dt>
                <dd>
                  <Link to="/optocht/inschrijvingen/$id" params={{ id: y.registration.id }}>
                    {y.registration.registrationNumber ? `Opgavenummer ${y.registration.registrationNumber}` : 'Inschrijving openen'}
                  </Link>{' '}
                  · {registrationStatusLabels[y.registration.status] ?? y.registration.status}
                  {y.registration.startNumber ? ` · startnummer ${y.registration.startNumber}` : ''}
                </dd>
                <dt>Opgegeven naam</dt>
                <dd>{y.registration.groupName ?? '–'}</dd>
                <dt>Contactpersoon</dt>
                <dd>
                  {y.registration.contactName ?? '–'}
                  {y.registration.contactPhone ? ` · ${y.registration.contactPhone}` : ''}
                  {y.registration.contactEmail ? ` · ${y.registration.contactEmail}` : ''}
                </dd>
                <dt>Bouwadres</dt>
                <dd>{y.registration.buildAddress ?? '–'}</dd>
                <dt>Adres jurykeuring</dt>
                <dd>{y.registration.juryAddress ?? '–'}</dd>
                <dt>Deelnemers</dt>
                <dd>
                  {y.registration.adultCount} volwassenen, {y.registration.childrenCount} kinderen
                  {y.registration.hasMusic === null ? '' : y.registration.hasMusic ? ' · met muziek' : ' · zonder muziek'}
                </dd>
                <dt>Lengte</dt>
                <dd>
                  {y.registration.measuredLengthMeters ?? y.registration.estimatedLengthMeters
                    ? `${y.registration.measuredLengthMeters ?? y.registration.estimatedLengthMeters} m${
                        y.registration.measuredLengthMeters ? ' (gemeten)' : ' (opgegeven)'
                      }`
                    : '–'}
                </dd>
                <dt>Ingediend</dt>
                <dd>{formatDateTime(y.registration.submittedAt)}</dd>
              </dl>
              {y.registration.subjectDescription ? <p>{y.registration.subjectDescription}</p> : null}
              {canEdit ? (
                <button
                  type="button"
                  className="button ghost small"
                  onClick={() =>
                    unlink.mutate(y.registration!.id, { onSuccess: () => setMessage('De inschrijving hangt niet meer aan deze groep.') })
                  }
                >
                  Inschrijving losmaken van deze groep
                </button>
              ) : null}
            </>
          ) : null}
        </section>
      ))}

      {editing ? (
        <GroupDialog
          group={g}
          onClose={(saved) => {
            setEditing(false);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      {year ? (
        <YearDialog
          groupId={id}
          year={year === 'nieuw' ? null : year}
          taken={g.years.map((y) => y.year)}
          onClose={(saved) => {
            setYear(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removingYear !== null}
        title="Jaar weghalen?"
        message={`Wat je zelf hebt ingevuld bij ${removingYear?.year ?? ''} verdwijnt. De inschrijving van dat jaar blijft staan.`}
        confirmLabel="Weghalen"
        busy={deleteYear.isPending}
        onCancel={() => setRemovingYear(null)}
        onConfirm={() =>
          removingYear &&
          deleteYear.mutate(removingYear.year, {
            onSuccess: () => {
              setRemovingYear(null);
              setMessage('Jaar weggehaald.');
            },
          })
        }
      />
      <ConfirmDialog
        open={removing}
        title="Groep verwijderen?"
        message={`${g.name} verdwijnt uit het register. Dat kan alleen zolang er geen inschrijving aan hangt; anders zet je de groep op "doet niet meer mee".`}
        confirmLabel="Verwijderen"
        busy={deleteGroup.isPending}
        onCancel={() => setRemoving(false)}
        onConfirm={() => deleteGroup.mutate(undefined, { onSuccess: () => void navigate({ to: '/optocht/groepen' }) })}
      />
    </>
  );
}

function GroupDialog({ group, onClose }: { group: CarnivalGroupDetail | null; onClose: (saved?: string) => void }) {
  const api = useApi();
  const navigate = useNavigate();
  const [form, setForm] = useState<CarnivalGroupRequest>({
    name: group?.name ?? '',
    contactName: group?.contactName ?? null,
    contactPhone: group?.contactPhone ?? null,
    contactEmail: group?.contactEmail ?? null,
    participatingSince: group?.participatingSince ?? null,
    notes: group?.notes ?? null,
    active: group?.active ?? true,
  });
  const save = useApiMutation(
    async (body: CarnivalGroupRequest) =>
      group
        ? (await api.PUT('/api/v1/admin/parade-groups/{id}', { params: { path: { id: group.id } }, body })).data
        : (await api.POST('/api/v1/admin/parade-groups', { body })).data,
    [...GROUP_KEYS, ...(group ? [['parade-group', group.id]] : [])],
  );
  const set = (change: Partial<CarnivalGroupRequest>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (data) => {
        onClose('Opgeslagen.');
        const created = (data as { id?: string } | undefined)?.id;
        if (!group && created) {
          void navigate({ to: '/optocht/groepen/$id', params: { id: created } });
        }
      },
    });
  }

  return (
    <Dialog open title={group ? 'Groep wijzigen' : 'Groep toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <Field label="Naam van de groep" required maxLength={100} value={form.name} onChange={(e) => set({ name: e.target.value })} />
        <Field
          label="Doet mee sinds (jaar)"
          type="number"
          min={1900}
          max={2200}
          hint="Vul in vanaf welk jaar ze meedoen; de jaren van vóór de app staan nergens anders."
          value={form.participatingSince ?? ''}
          onChange={(e) => set({ participatingSince: e.target.value ? Number(e.target.value) : null })}
        />
        <div className="form-grid">
          <Field label="Contactpersoon" maxLength={100} value={form.contactName ?? ''} onChange={(e) => set({ contactName: e.target.value || null })} />
          <Field label="Telefoon" maxLength={20} value={form.contactPhone ?? ''} onChange={(e) => set({ contactPhone: e.target.value || null })} />
        </div>
        <Field
          label="E-mail"
          type="email"
          maxLength={254}
          value={form.contactEmail ?? ''}
          onChange={(e) => set({ contactEmail: e.target.value || null })}
        />
        <div className="field">
          <label htmlFor="groep-opmerking">Opmerking</label>
          <textarea
            id="groep-opmerking"
            rows={4}
            maxLength={2000}
            value={form.notes ?? ''}
            onChange={(e) => set({ notes: e.target.value || null })}
          />
        </div>
        <Checkbox label="Doet mee (koppelt automatisch bij een nieuwe inschrijving)" checked={form.active} onChange={(e) => set({ active: e.target.checked })} />
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function YearDialog({
  groupId,
  year,
  taken,
  onClose,
}: {
  groupId: string;
  year: CarnivalGroupYear | null;
  taken: number[];
  onClose: (saved?: string) => void;
}) {
  const api = useApi();
  const categories = useGroupCategories();
  const [number, setNumber] = useState(year?.year ?? thisYear);
  const [form, setForm] = useState<CarnivalGroupYearRequest>({
    floatName: year?.floatName ?? null,
    categoryId: year?.categoryId ?? null,
    place: year?.place ?? null,
    notes: year?.notes ?? null,
  });
  const save = useApiMutation(
    async (body: CarnivalGroupYearRequest) =>
      api.PUT('/api/v1/admin/parade-groups/{id}/years/{year}', { params: { path: { id: groupId, year: number } }, body }),
    [...GROUP_KEYS, ['parade-group', groupId]],
  );
  const set = (change: Partial<CarnivalGroupYearRequest>) => setForm({ ...form, ...change });
  const duplicate = year === null && taken.includes(number);

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, { onSuccess: () => onClose('Jaar opgeslagen.') });
  }

  return (
    <Dialog open title={year ? `${year.year} bijwerken` : 'Jaar toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        {year ? null : (
          <Field
            label="Jaar"
            type="number"
            required
            min={1900}
            max={2200}
            value={number}
            onChange={(e) => setNumber(Number(e.target.value))}
          />
        )}
        {duplicate ? <p className="muted">Dit jaar staat al bij de groep; opslaan werkt het bij.</p> : null}
        <Field
          label="Naam van de wagen of loopgroep"
          maxLength={150}
          hint={
            year?.registration?.subject
              ? `Leeg laten neemt het onderwerp van de inschrijving over: "${year.registration.subject}".`
              : 'Zoals de groep hem noemde.'
          }
          value={form.floatName ?? ''}
          onChange={(e) => set({ floatName: e.target.value || null })}
        />
        <div className="form-grid">
          <div className="field">
            <label htmlFor="jaar-categorie">Categorie</label>
            <select
              id="jaar-categorie"
              value={form.categoryId ?? ''}
              onChange={(e) => set({ categoryId: e.target.value ? Number(e.target.value) : null })}
            >
              <option value="">{year?.registration ? 'Uit de inschrijving' : 'Onbekend'}</option>
              {(categories.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                  {c.active ? '' : ' (oud)'}
                </option>
              ))}
            </select>
          </div>
          <Field
            label="Plaats in de uitslag"
            type="number"
            min={1}
            max={200}
            hint={year?.placeFromResults ? 'Nu overgenomen uit de uitslag; zelf invullen gaat voor.' : undefined}
            value={form.place ?? ''}
            onChange={(e) => set({ place: e.target.value ? Number(e.target.value) : null })}
          />
        </div>
        <div className="field">
          <label htmlFor="jaar-opmerking">Opmerking</label>
          <textarea
            id="jaar-opmerking"
            rows={3}
            maxLength={2000}
            value={form.notes ?? ''}
            onChange={(e) => set({ notes: e.target.value || null })}
          />
        </div>
        <ProblemAlert error={save.error ?? categories.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </Dialog>
  );
}
