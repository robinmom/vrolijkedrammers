import { useState, type CSSProperties, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useBoardSeats,
  useMe,
  useMembers,
  type BoardSeat,
  type BoardSeatRequest,
  type GoverningBody,
} from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

/** Een bestuursperiode duurt drie jaar; daar staat in de balk elke keer een streep. */
const TERM_YEARS = 3;
const DAYS_PER_YEAR = 365.25;

const bodyLabels: Record<GoverningBody, string> = { Board: 'Bestuur', CouncilOfEleven: 'Raad van Elf' };

const BOARD_KEYS = [['board']];

function years(days: number) {
  return days / DAYS_PER_YEAR;
}

/** "4 jaar" of "8 maanden": hele jaren zolang het er meer dan één is. */
function servedLabel(days: number) {
  const whole = Math.floor(years(days));
  if (whole >= 1) {
    return `${whole} jaar`;
  }

  const months = Math.max(0, Math.round(days / 30.44));
  return months === 1 ? '1 maand' : `${months} maanden`;
}

/**
 * Leden → Bestuur (2026-11-17): wie zit er in het bestuur en de raad van elf, en hoe lang al. Het bestuur is
 * verkiesbaar: een periode duurt drie jaar en kan met één of twee jaar verlengd worden. De balk laat per persoon zien
 * hoeveel jaren hij erin zit, met elke drie jaar een streep, zodat je in één oogopslag ziet wie waar in zijn periode
 * zit.
 */
export function BoardPage() {
  const me = useMe();
  const [body, setBody] = useState<GoverningBody>('Board');
  const [includePast, setIncludePast] = useState(false);
  const seats = useBoardSeats(body, includePast);
  const [editing, setEditing] = useState<BoardSeat | 'nieuw' | null>(null);
  const [removing, setRemoving] = useState<BoardSeat | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  const api = useApi();
  const list = seats.data ?? [];
  const remove = useApiMutation(
    async (id: string) => api.DELETE('/api/v1/admin/board/{id}', { params: { path: { id } } }),
    BOARD_KEYS,
  );
  const extend = useApiMutation(
    async ({ id, extendYears }: { id: string; extendYears: number }) =>
      api.POST('/api/v1/admin/board/{id}/extend', { params: { path: { id } }, body: { years: extendYears, note: null } }),
    BOARD_KEYS,
  );
  const renew = useApiMutation(
    async (id: string) => api.POST('/api/v1/admin/board/{id}/renew', { params: { path: { id } }, body: { startDate: null, note: null } }),
    BOARD_KEYS,
  );

  // Eén schaal voor alle balken, afgerond op hele periodes, zodat de strepen van iedereen op dezelfde plek staan.
  const longest = Math.max(TERM_YEARS, ...list.map((s) => years(s.daysServed)));
  const scale = Math.ceil(longest / TERM_YEARS) * TERM_YEARS;

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Bestuur en raad van elf</h1>
          <p className="muted">
            Wie zit erin en hoe lang al. Een bestuursperiode duurt drie jaar en kan met één of twee jaar verlengd worden;
            de raad van elf begint en stopt wanneer iemand dat zelf wil.
          </p>
        </div>
        {canEdit ? (
          <button type="button" className="button" onClick={() => setEditing('nieuw')}>
            Toevoegen
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={seats.error ?? remove.error ?? extend.error ?? renew.error} />

      <div className="chips" role="group" aria-label="Orgaan">
        {(Object.keys(bodyLabels) as GoverningBody[]).map((b) => (
          <button key={b} type="button" className={body === b ? 'chip active' : 'chip'} aria-pressed={body === b} onClick={() => setBody(b)}>
            {bodyLabels[b]}
          </button>
        ))}
        <button
          type="button"
          className={includePast ? 'chip active' : 'chip'}
          aria-pressed={includePast}
          onClick={() => setIncludePast(!includePast)}
        >
          Ook wie gestopt is
        </button>
      </div>

      <p className="muted">
        In de balk is elke streep {TERM_YEARS} jaar — bij het bestuur dus één periode. De schaal loopt tot {scale} jaar.
      </p>

      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label={bodyLabels[body]}>
        <table className="table">
          <caption className="visually-hidden">{bodyLabels[body]}</caption>
          <thead>
            <tr>
              <th scope="col">Naam</th>
              <th scope="col">Functie</th>
              <th scope="col">Sinds</th>
              <th scope="col">Hoe lang</th>
              <th scope="col">{body === 'Board' ? 'Periode loopt t/m' : 'Gestopt'}</th>
              {canEdit ? (
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              ) : null}
            </tr>
          </thead>
          <tbody>
            {list.map((s) => (
              <tr key={s.id}>
                <td>
                  <strong>{s.name}</strong>
                  {s.notes ? <div className="muted">{s.notes}</div> : null}
                </td>
                <td>{s.function ?? <span className="muted">lid</span>}</td>
                <td>{formatDate(s.startDate)}</td>
                <td>
                  <ServiceBar seat={s} scale={scale} />
                </td>
                <td>
                  <TermStatus seat={s} />
                  {canEdit && s.body === 'Board' && s.endDate === null && (s.daysUntilTermEnd ?? Infinity) <= 365 ? (
                    <div className="term-actions">
                      {[1, 2].map((jaren) => (
                        <button
                          key={jaren}
                          type="button"
                          className="button ghost small"
                          disabled={extend.isPending}
                          onClick={() =>
                            extend.mutate(
                              { id: s.id, extendYears: jaren },
                              { onSuccess: () => setMessage(`De periode van ${s.name} is met ${jaren} jaar verlengd.`) },
                            )
                          }
                        >
                          +{jaren} jaar <span className="visually-hidden">verlengen voor {s.name}</span>
                        </button>
                      ))}
                      <button
                        type="button"
                        className="button ghost small"
                        disabled={renew.isPending}
                        onClick={() => renew.mutate(s.id, { onSuccess: () => setMessage(`${s.name} is aan een nieuwe periode begonnen.`) })}
                      >
                        Nieuwe periode <span className="visually-hidden">voor {s.name}</span>
                      </button>
                    </div>
                  ) : null}
                </td>
                {canEdit ? (
                  <td className="actions-cell">
                    <button type="button" className="button ghost small" onClick={() => setEditing(s)}>
                      Bewerken <span className="visually-hidden">{s.name}</span>
                    </button>
                    <button type="button" className="button ghost small" onClick={() => setRemoving(s)}>
                      Verwijderen <span className="visually-hidden">{s.name}</span>
                    </button>
                  </td>
                ) : null}
              </tr>
            ))}
            {list.length === 0 ? (
              <tr>
                <td colSpan={canEdit ? 6 : 5} className="muted">
                  Nog niemand ingevoerd voor {bodyLabels[body].toLowerCase()}.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {editing ? (
        <SeatDialog
          seat={editing === 'nieuw' ? null : editing}
          defaultBody={body}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removing !== null}
        title="Uit het overzicht halen?"
        message={`${removing?.name ?? ''} verdwijnt uit dit overzicht, met alle periodes. Iemand die gestopt is, kun je beter een einddatum geven.`}
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() =>
          removing &&
          remove.mutate(removing.id, {
            onSuccess: () => {
              setRemoving(null);
              setMessage('Verwijderd.');
            },
          })
        }
      />
    </>
  );
}

/**
 * De balk met het aantal jaren, met elke drie jaar een streep (één bestuursperiode). De streep bij het einde van de
 * huidige periode staat er apart in, want een verlenging schuift die op.
 */
function ServiceBar({ seat, scale }: { seat: BoardSeat; scale: number }) {
  const served = years(seat.daysServed);
  const style = { '--term-step': `${(TERM_YEARS / scale) * 100}%` } as CSSProperties;
  const termEnd = seat.termEnd === null ? null : years(seat.daysServed + (seat.daysUntilTermEnd ?? 0));
  return (
    <div className="term-bar-cell">
      <div className="term-bar" style={style} role="img" aria-label={`${servedLabel(seat.daysServed)} in het ${bodyLabels[seat.body].toLowerCase()}`}>
        <span className="term-bar-fill" style={{ width: `${Math.min(100, (served / scale) * 100)}%` }} />
        <span className="term-bar-ticks" />
        {termEnd !== null && termEnd <= scale ? (
          // Net binnen de balk houden: op precies 100% valt de streep weg tegen de rand.
          <span className="term-bar-end" style={{ left: `${Math.min(99, (termEnd / scale) * 100)}%` }} />
        ) : null}
      </div>
      <span className="term-bar-label">{servedLabel(seat.daysServed)}</span>
    </div>
  );
}

/** Wanneer de periode afloopt (bestuur), of wanneer iemand gestopt is. */
function TermStatus({ seat }: { seat: BoardSeat }) {
  if (seat.endDate !== null) {
    return <>{formatDate(seat.endDate)}</>;
  }

  if (seat.body !== 'Board' || seat.termEnd === null) {
    return <span className="muted">–</span>;
  }

  const days = seat.daysUntilTermEnd ?? 0;
  const current = seat.terms.find((t) => t.isCurrent);
  const extension = current && current.extensionYears > 0 ? ` · verlengd met ${current.extensionYears} jaar` : '';
  const months = Math.max(1, Math.round(days / 30.44));
  return (
    <>
      {formatDate(seat.termEnd)}
      <div className="muted">
        {days < 0 ? 'periode verlopen' : days <= 365 ? `nog ${months} ${months === 1 ? 'maand' : 'maanden'}` : `periode ${current?.number ?? 1}`}
        {extension}
      </div>
    </>
  );
}

interface SeatForm {
  body: GoverningBody;
  memberId: string | null;
  name: string;
  function: string;
  startDate: string;
  endDate: string;
  notes: string;
  terms: { startDate: string; extensionYears: number; note: string | null }[];
}

function SeatDialog({ seat, defaultBody, onClose }: { seat: BoardSeat | null; defaultBody: GoverningBody; onClose: (saved?: string) => void }) {
  const api = useApi();
  const [form, setForm] = useState<SeatForm>({
    body: seat?.body ?? defaultBody,
    memberId: seat?.memberId ?? null,
    name: seat?.name ?? '',
    function: seat?.function ?? '',
    startDate: seat?.startDate ?? '',
    endDate: seat?.endDate ?? '',
    notes: seat?.notes ?? '',
    terms: (seat?.terms ?? []).map((t) => ({ startDate: t.startDate, extensionYears: t.extensionYears, note: t.note })),
  });
  const [search, setSearch] = useState('');
  const results = useMembers({ search, status: '', syncState: '' }, 1);
  const matches = search.trim().length >= 2 ? (results.data?.items ?? []).slice(0, 8) : [];
  const save = useApiMutation(
    async (body: BoardSeatRequest) =>
      seat
        ? (await api.PUT('/api/v1/admin/board/{id}', { params: { path: { id: seat.id } }, body })).data
        : (await api.POST('/api/v1/admin/board', { body })).data,
    BOARD_KEYS,
  );
  const set = (change: Partial<SeatForm>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(
      {
        body: form.body,
        memberId: form.memberId,
        name: form.name || null,
        function: form.function || null,
        startDate: form.startDate,
        endDate: form.endDate || null,
        notes: form.notes || null,
        terms:
          form.body === 'Board'
            ? form.terms.map((t) => ({ startDate: t.startDate, extensionYears: t.extensionYears, note: t.note }))
            : null,
      },
      { onSuccess: () => onClose('Opgeslagen.') },
    );
  }

  return (
    <Dialog open title={seat ? `${seat.name} bijwerken` : 'Toevoegen aan bestuur of raad van elf'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <div className="field">
          <label htmlFor="orgaan">Orgaan</label>
          <select id="orgaan" value={form.body} onChange={(e) => set({ body: e.target.value as GoverningBody })}>
            {(Object.keys(bodyLabels) as GoverningBody[]).map((b) => (
              <option key={b} value={b}>
                {bodyLabels[b]}
              </option>
            ))}
          </select>
        </div>
        {form.memberId ? (
          <p>
            Gekoppeld aan het lid <strong>{form.name}</strong>.{' '}
            <button type="button" className="button ghost small" onClick={() => set({ memberId: null })}>
              Koppeling weghalen
            </button>
          </p>
        ) : (
          <>
            <Field label="Zoek het lid op naam of lidnummer" value={search} onChange={(e) => setSearch(e.target.value)} />
            {matches.length > 0 ? (
              <ul className="list" aria-label="Zoekresultaten">
                {matches.map((m) => (
                  <li key={m.id}>
                    {m.fullName} ({m.memberNumber}){' '}
                    <button
                      type="button"
                      className="button secondary small"
                      onClick={() => {
                        setForm({ ...form, memberId: m.id, name: m.fullName });
                        setSearch('');
                      }}
                    >
                      Kiezen
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
            <Field
              label="Of een naam zelf"
              maxLength={100}
              hint="Voor iemand van lang geleden die niet in de ledenlijst staat."
              value={form.name}
              onChange={(e) => set({ name: e.target.value })}
            />
          </>
        )}
        <div className="form-grid">
          <Field
            label="Functie"
            maxLength={60}
            hint="Bijvoorbeeld Voorzitter of Vorst; leeg laten betekent lid."
            value={form.function}
            onChange={(e) => set({ function: e.target.value })}
          />
          <Field label="In functie sinds" type="date" required value={form.startDate} onChange={(e) => set({ startDate: e.target.value })} />
        </div>
        <Field
          label="Gestopt op"
          type="date"
          hint="Leeg laten zolang iemand er nog in zit."
          value={form.endDate}
          onChange={(e) => set({ endDate: e.target.value })}
        />
        {form.body === 'Board' ? (
          <fieldset>
            <legend>Bestuursperiodes</legend>
            <p className="muted">
              Elke periode duurt {TERM_YEARS} jaar. Laat je dit leeg, dan begint de eerste periode op de datum hierboven.
            </p>
            {form.terms.map((t, i) => (
              <div className="form-grid" key={i}>
                <Field
                  label={`Periode ${i + 1} begint`}
                  type="date"
                  required
                  value={t.startDate}
                  onChange={(e) =>
                    set({ terms: form.terms.map((x, j) => (i === j ? { ...x, startDate: e.target.value } : x)) })
                  }
                />
                <div className="field">
                  <label htmlFor={`verlenging-${i}`}>Verlengd met</label>
                  <select
                    id={`verlenging-${i}`}
                    value={t.extensionYears}
                    onChange={(e) =>
                      set({ terms: form.terms.map((x, j) => (i === j ? { ...x, extensionYears: Number(e.target.value) } : x)) })
                    }
                  >
                    <option value={0}>niet verlengd</option>
                    <option value={1}>1 jaar</option>
                    <option value={2}>2 jaar</option>
                  </select>
                </div>
              </div>
            ))}
            <div className="actions">
              <button
                type="button"
                className="button secondary small"
                onClick={() =>
                  set({
                    terms: [
                      ...form.terms,
                      { startDate: form.terms.at(-1)?.startDate ?? form.startDate, extensionYears: 0, note: null },
                    ],
                  })
                }
              >
                Periode toevoegen
              </button>
              {form.terms.length > 0 ? (
                <button type="button" className="button ghost small" onClick={() => set({ terms: form.terms.slice(0, -1) })}>
                  Laatste periode weghalen
                </button>
              ) : null}
            </div>
          </fieldset>
        ) : null}
        <div className="field">
          <label htmlFor="bestuur-opmerking">Opmerking</label>
          <textarea
            id="bestuur-opmerking"
            rows={3}
            maxLength={2000}
            value={form.notes}
            onChange={(e) => set({ notes: e.target.value })}
          />
        </div>
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
