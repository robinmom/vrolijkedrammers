import { useEffect, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { SALES_KEYS, useEvenings, useSeating, type SeatingLayout, type SeatingParty, type SeatingTable } from '../api/sales';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

/** Tien goed te onderscheiden kleuren voor de gezelschappen (huisstijl eerst). */
const PARTY_COLORS = [
  '#ED0012',
  '#087BC1',
  '#39A935',
  '#F4B942',
  '#8E44AD',
  '#E67E22',
  '#16A085',
  '#C0392B',
  '#2C3E50',
  '#D81B8C',
];

function colorFor(index: number): string {
  return PARTY_COLORS[((index % PARTY_COLORS.length) + PARTY_COLORS.length) % PARTY_COLORS.length] ?? PARTY_COLORS[0]!;
}

type SeatPart = SeatingTable['parts'][number];

function Dot({ color }: { color: number }) {
  return <span className="seat-dot" style={{ background: colorFor(color) }} aria-hidden="true" />;
}

/** De plaatsen (op volgorde) uitvouwen tot losse stoelen: per stoel de kleur van het gezelschap, of null voor leeg. */
function seatColors(parts: readonly SeatPart[], capacity: number): (number | null)[] {
  const seats: (number | null)[] = [];
  for (const p of parts) {
    for (let i = 0; i < p.seats; i++) seats.push(p.color);
  }
  while (seats.length < capacity) seats.push(null);
  return seats;
}

/** Eén stoel (een gekleurde stip, grijs als leeg). */
function Chair({ color }: { color: number | null }) {
  return (
    <span
      className={color === null ? 'seat-chair empty' : 'seat-chair'}
      style={color === null ? undefined : { background: colorFor(color) }}
      aria-hidden="true"
    />
  );
}

/** De tafel met stoelen: een verticaal tafelblad met vier stoelen links en vier rechts, gekleurd per gezelschap. */
function TableTop({ parts }: { parts: readonly SeatPart[] }) {
  const seats = seatColors(parts, 8);
  const left = seats.slice(0, 4);
  const right = seats.slice(4, 8);
  return (
    <div className="seat-top" aria-hidden="true">
      <div className="seat-side">
        {left.map((c, i) => (
          <Chair key={`l${i}`} color={c} />
        ))}
      </div>
      <div className="seat-board" />
      <div className="seat-side">
        {right.map((c, i) => (
          <Chair key={`r${i}`} color={c} />
        ))}
      </div>
    </div>
  );
}

/** Eén tafel in de plattegrond: het nummer, wie er zit (sleepbaar) en de tafel met stoelen. */
function TableCard({
  table,
  dragging,
  active,
  onEnter,
  onLeave,
  onDropHere,
  onPartyDragStart,
  onPartyDragEnd,
}: {
  table: SeatingTable;
  dragging: boolean;
  active: boolean;
  onEnter: () => void;
  onLeave: () => void;
  onDropHere: () => void;
  onPartyDragStart: (partyKey: string) => void;
  onPartyDragEnd: () => void;
}) {
  const classes = ['seat-table'];
  if (table.overflow) classes.push('overflow');
  if (table.seats === 0) classes.push('empty');
  if (active) classes.push('droptarget');
  return (
    <div
      className={classes.join(' ')}
      aria-label={`Tafel ${table.number}`}
      onDragOver={(e) => {
        if (dragging) e.preventDefault();
      }}
      onDragEnter={() => {
        if (dragging) onEnter();
      }}
      onDragLeave={onLeave}
      onDrop={(e) => {
        e.preventDefault();
        onDropHere();
      }}
    >
      <div className="seat-table-head">
        <span className="seat-table-number">Tafel {table.number}</span>
        <span className="seat-table-count">{table.seats}/8</span>
      </div>
      {table.parts.length === 0 ? (
        <p className="seat-table-empty">Vrije plaatsen</p>
      ) : (
        <ul className="seat-table-parts">
          {table.parts.map((p) => (
            <li
              key={p.partyKey}
              draggable
              title={`Sleep ${p.partyName} naar een andere tafel`}
              onDragStart={() => onPartyDragStart(p.partyKey)}
              onDragEnd={onPartyDragEnd}
            >
              <Dot color={p.color} />
              <span className="seat-part-name">{p.partyName}</span>
              <span className="seat-part-count">{p.seats}p</span>
            </li>
          ))}
        </ul>
      )}
      <TableTop parts={table.parts} />
      {table.overflow ? <span className="badge warn seat-overflow-badge">extra tafel</span> : null}
    </div>
  );
}

/** Losse stoelen ná de laatste tafel in een rij (als er minder dan vier plaatsen overblijven). */
function LooseChairs({ parts }: { parts: readonly SeatPart[] }) {
  const total = parts.reduce((n, p) => n + p.seats, 0);
  return (
    <div className="seat-chairs" aria-label={`Losse stoelen: ${parts.map((p) => `${p.partyName} ${p.seats}`).join(', ')}`}>
      <div className="seat-table-head">
        <span className="seat-table-number">Losse stoelen</span>
        <span className="seat-table-count">{total}</span>
      </div>
      <ul className="seat-table-parts">
        {parts.map((p) => (
          <li key={p.partyKey}>
            <Dot color={p.color} />
            <span className="seat-part-name">{p.partyName}</span>
            <span className="seat-part-count">{p.seats}p</span>
          </li>
        ))}
      </ul>
      <div className="seat-chairs-row" aria-hidden="true">
        {seatColors(parts, total).map((c, i) => (
          <Chair key={i} color={c} />
        ))}
      </div>
    </div>
  );
}

/**
 * Pronkzitting → Tafelindeling (fase 19t, Figma "Tafelindeling" + "Afdruklijst"): per avond een plattegrond met vier
 * rijen en tafels van acht plaatsen, het podium bovenaan. Elke groep of losse bestelling zit bij elkaar en kan over
 * meerdere tafels in dezelfde rij lopen; kleine bestellingen vullen de lege plaatsen aan. Gezelschappen zijn te
 * verslepen of met de knoppen te verplaatsen, het aantal tafels per rij is instelbaar en de indeling is als afdruklijst
 * (gezelschap → tafelnummers) te exporteren.
 */
export function PronkzittingSeatingPage() {
  const evenings = useEvenings();
  const [productId, setProductId] = useState<string | null>(null);
  const effectiveId = productId ?? evenings.data?.[0]?.productId ?? null;
  const seating = useSeating(effectiveId);
  const [message, setMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<unknown>(null);
  const [dragKey, setDragKey] = useState<string | null>(null);
  const [dropTable, setDropTable] = useState<number | null>(null);
  const api = useApi();

  const layout = seating.data;
  const [tablesPerRow, setTablesPerRow] = useState(6);
  useEffect(() => {
    if (layout) setTablesPerRow(layout.tablesPerRow);
  }, [layout]);

  const place = useApiMutation(
    ({ partyKey, table }: { partyKey: string; table: number | null }) =>
      api.PUT('/api/v1/admin/sales/pronkzitting/{productId}/seating/place', {
        params: { path: { productId: effectiveId! } },
        body: { partyKey, table },
      }),
    SALES_KEYS,
  );
  const saveTables = useApiMutation(
    (value: number) =>
      api.PUT('/api/v1/admin/sales/pronkzitting/{productId}/seating/tables-per-row', {
        params: { path: { productId: effectiveId! } },
        body: { tablesPerRow: value },
      }),
    SALES_KEYS,
  );
  const arrange = useApiMutation(
    () =>
      api.POST('/api/v1/admin/sales/pronkzitting/{productId}/seating/arrange', {
        params: { path: { productId: effectiveId! } },
      }),
    SALES_KEYS,
  );

  function placeAt(party: SeatingParty, table: number | null) {
    place.mutate({ partyKey: party.key, table });
  }

  async function exportExcel() {
    if (!effectiveId) return;
    setExportError(null);
    try {
      const { data, response } = await api.GET('/api/v1/admin/sales/pronkzitting/{productId}/seating/export', {
        params: { path: { productId: effectiveId } },
        parseAs: 'blob',
      });
      if (data) {
        const disposition = response.headers.get('content-disposition') ?? '';
        const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name ? decodeURIComponent(name) : 'Pronkzitting afdruklijst.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (e) {
      setExportError(e);
    }
  }

  const avondLabel = (l: SeatingLayout) => (l.date ? formatDate(l.date) : l.productName);

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Tafelindeling pronkzitting</h1>
          <p className="page-subtitle">
            Vier rijen, acht plaatsen per tafel, podium bovenaan. Elke groep zit bij elkaar. Sleep een gezelschap op een
            tafel of kies een starttafel; de rest wordt automatisch in de vrije plekken gevuld.
          </p>
        </div>
        <div className="actions">
          <button type="button" className="button secondary" disabled={!effectiveId} onClick={() => void exportExcel()}>
            Afdruklijst (Excel)
          </button>
        </div>
      </div>

      <SuccessMessage message={message} />
      <ProblemAlert error={evenings.error ?? seating.error ?? place.error ?? saveTables.error ?? arrange.error ?? exportError} />

      {(evenings.data ?? []).length > 1 ? (
        <div className="filters" role="group" aria-label="Kies de avond">
          {(evenings.data ?? []).map((e) => (
            <button
              key={e.productId}
              type="button"
              className={e.productId === effectiveId ? 'button small' : 'button ghost small'}
              aria-pressed={e.productId === effectiveId}
              onClick={() => setProductId(e.productId)}
            >
              {e.date ? formatDate(e.date) : e.name}
            </button>
          ))}
        </div>
      ) : null}

      {layout ? (
        <>
          <section className="card" aria-labelledby="tafelindeling-kop">
            <div className="card-header">
              <h2 id="tafelindeling-kop">
                {avondLabel(layout)} · {layout.totalSeats} plaatsen
              </h2>
              <div className="actions">
                <label htmlFor="tafels-per-rij" className="inline-label">
                  Tafels per rij
                </label>
                <input
                  id="tafels-per-rij"
                  type="number"
                  min={1}
                  max={50}
                  className="narrow-number"
                  value={tablesPerRow}
                  onChange={(e) => setTablesPerRow(Math.max(1, Math.min(50, Number(e.target.value) || 1)))}
                  onBlur={() => {
                    if (tablesPerRow !== layout.tablesPerRow)
                      saveTables.mutate(tablesPerRow, { onSuccess: () => setMessage('Aantal tafels per rij opgeslagen.') });
                  }}
                />
                <button
                  type="button"
                  className="button ghost small"
                  disabled={arrange.isPending}
                  onClick={() =>
                    arrange.mutate(undefined, { onSuccess: () => setMessage('De indeling is automatisch verdeeld.') })
                  }
                >
                  Automatisch verdelen
                </button>
              </div>
            </div>

            {layout.overflow ? (
              <p className="card-hint" role="status">
                In een rij passen niet alle plaatsen op het ingestelde aantal tafels. Er staat een <strong>extra tafel</strong>{' '}
                bij (vanaf vier plaatsen); bij minder dan vier plaatsen komen er <strong>losse stoelen</strong> na de laatste
                tafel. Je kunt ook het aantal tafels per rij verhogen of gezelschappen naar een andere rij verslepen.
              </p>
            ) : null}

            <div className="seat-plan">
              <div className="seat-podium" aria-hidden="true">
                PODIUM
              </div>
              {/* Vier rijen naast elkaar: rij 1 links, rij 4 rechts; de tafels lopen binnen een rij van boven naar onder.
                  Sleep een gezelschap op een tafel om het daar te laten starten. */}
              <div className="seat-columns" tabIndex={0} role="region" aria-label="Plattegrond met de rijen">
                {layout.rows.map((row) => (
                  <div key={row.rowIndex} className="seat-column">
                    <div className="seat-column-label">
                      <strong>Rij {row.rowIndex + 1}</strong>
                      <span className="muted small-text">{row.seats} plaatsen</span>
                    </div>
                    <div className="seat-column-tables">
                      {row.tables.map((table) => (
                        <TableCard
                          key={table.number}
                          table={table}
                          dragging={dragKey !== null}
                          active={dropTable === table.number}
                          onEnter={() => setDropTable(table.number)}
                          onLeave={() => setDropTable((t) => (t === table.number ? null : t))}
                          onDropHere={() => {
                            const party = dragKey ? layout.parties.find((p) => p.key === dragKey) : null;
                            if (party) placeAt(party, table.number);
                            setDragKey(null);
                            setDropTable(null);
                          }}
                          onPartyDragStart={(key) => setDragKey(key)}
                          onPartyDragEnd={() => {
                            setDragKey(null);
                            setDropTable(null);
                          }}
                        />
                      ))}
                      {row.chairs.length > 0 ? <LooseChairs parts={row.chairs} /> : null}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </section>

          <section className="card" aria-labelledby="gezelschappen-kop">
            <h2 id="gezelschappen-kop">Gezelschappen</h2>
            <p className="card-hint">
              Sleep een gezelschap op een tafel in de plattegrond, of kies hieronder de starttafel. "Automatisch" laat het
              gezelschap de eerste vrije plek vullen. Een handmatig gekozen tafel blijft staan, ook als eerdere tafels leeg zijn.
            </p>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Gezelschappen">
              <table className="table compact">
                <thead>
                  <tr>
                    <th scope="col">Gezelschap</th>
                    <th scope="col">Aantal</th>
                    <th scope="col">Starttafel</th>
                    <th scope="col">Tafel(s)</th>
                    <th scope="col">Opmerking / wensen</th>
                  </tr>
                </thead>
                <tbody>
                  {layout.parties.map((party) => (
                    <tr
                      key={party.key}
                      draggable
                      onDragStart={() => setDragKey(party.key)}
                      onDragEnd={() => {
                        setDragKey(null);
                        setDropTable(null);
                      }}
                    >
                      <td>
                        <Dot color={party.color} />
                        <strong>{party.name}</strong>
                        <div className="muted small-text">{party.isGroup ? 'groep' : 'losse kaarten'}</div>
                      </td>
                      <td>{party.seats}</td>
                      <td>
                        <label className="visually-hidden" htmlFor={`tafel-${party.key}`}>
                          Starttafel voor {party.name}
                        </label>
                        <select
                          id={`tafel-${party.key}`}
                          value={party.manual ? String(party.startTable) : 'auto'}
                          disabled={place.isPending}
                          onChange={(e) => placeAt(party, e.target.value === 'auto' ? null : Number(e.target.value))}
                        >
                          <option value="auto">Automatisch</option>
                          {layout.rows.map((r) => (
                            <optgroup key={r.rowIndex} label={`Rij ${r.rowIndex + 1}`}>
                              {r.tables.map((t) => (
                                <option key={t.number} value={t.number}>
                                  Tafel {t.number}
                                </option>
                              ))}
                            </optgroup>
                          ))}
                        </select>
                      </td>
                      <td>{party.tables.join(', ')}</td>
                      <td>{party.remark ?? ''}</td>
                    </tr>
                  ))}
                  {layout.parties.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="muted">
                        Nog geen betaalde bestellingen voor deze avond.
                      </td>
                    </tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : effectiveId === null && !evenings.isLoading ? (
        <section className="card">
          <p className="muted">Er is nog geen pronkzittingavond. Maak eerst een avond aan onder Pronkzitting.</p>
        </section>
      ) : null}
    </>
  );
}
