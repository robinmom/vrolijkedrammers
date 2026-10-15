import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, type Schemas } from '../api/hooks';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

type Row = Schemas['MemberHistoryRow'];

const KEY = ['reports', 'members', 'history'];
const SERIES = [
  { key: 'newMembers', label: 'Nieuwe leden', color: 'var(--dvd-action-secondary)', axis: 'left' },
  { key: 'leftMembers', label: 'Uitgeschreven', color: 'var(--dvd-action-primary)', axis: 'left' },
  { key: 'totalMembers', label: 'Totaal leden', color: 'var(--dvd-text-primary)', axis: 'right' },
] as const;
const RANGES = [
  { label: '10 jaar', years: 10 },
  { label: '25 jaar', years: 25 },
  { label: 'Alles', years: 0 },
] as const;

/**
 * Ledenverloop per carnavalsjaar (2026-10-15): nieuwe leden, uitschrijvingen en het totaal. Het totaal staat op een eigen
 * as rechts. Oudere jaren vult het bestuur in of importeert het uit Excel.
 */
export function MemberHistoryCard() {
  const api = useApi();
  const auth = useAuth();
  const canEdit = useMe().data?.permissions.includes('member.update') ?? false;
  const history = useQuery({
    queryKey: KEY,
    queryFn: async () => (await api.GET('/api/v1/admin/reports/members/history')).data ?? null,
  });
  const [range, setRange] = useState<number>(10);
  const [editing, setEditing] = useState<{ year: number; values: Record<string, string> } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [importError, setImportError] = useState<unknown>(null);
  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/reports/members/history/{year}', {
        params: { path: { year: editing!.year } },
        body: {
          newMembers: toNumber(editing!.values.newMembers),
          leftMembers: toNumber(editing!.values.leftMembers),
          totalMembers: toNumber(editing!.values.totalMembers),
        },
      }),
    [KEY],
  );

  const report = history.data;
  if (!report) return <ProblemAlert error={history.error} />;
  const rows = range ? report.rows.slice(-range) : report.rows;

  async function importFile(file: File | undefined) {
    if (!file) return;
    setImportError(null);
    const form = new FormData();
    form.append('file', file);
    try {
      const result = await uploadJson<{ years: number }>(auth, '/api/v1/admin/reports/members/history/import', form);
      setMessage(`${result.years} carnavalsjaren ingelezen.`);
      await history.refetch();
    } catch (error) {
      setImportError(error);
    }
  }

  return (
    <section className="card" aria-labelledby="ledenverloop">
      <div className="card-header">
        <h2 id="ledenverloop">Ledenverloop per carnavalsjaar</h2>
        <div className="chips" role="group" aria-label="Periode">
          {RANGES.map((r) => (
            <button
              key={r.label}
              type="button"
              className={`chip${range === r.years ? ' active' : ''}`}
              aria-pressed={range === r.years}
              onClick={() => setRange(r.years)}
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>
      <LineChart rows={rows} />
      <ul className="chart-legend">
        {SERIES.map((s) => (
          <li key={s.key}>
            <span className="swatch" style={{ background: s.color }} aria-hidden="true" />
            {s.label}
            {s.axis === 'right' ? ' (rechteras)' : ''}
          </li>
        ))}
      </ul>
      <p className="muted small-text">
        Nieuwe leden komen uit het inschrijfjaar (zonder gesplitste tweede leden). Uitschrijvingen en het totaal legt
        het systeem vanaf {report.trackedSince - 1}/{report.trackedSince} zelf vast; voor oudere jaren telt wat je
        hieronder invult of importeert.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={save.error ?? importError} />

      <details>
        <summary>Cijfers per jaar{canEdit ? ' bekijken en aanvullen' : ''}</summary>
        {canEdit ? (
          <div className="field">
            <label htmlFor="ledenverloop-import">
              Importeren uit Excel (kolommen Jaar, Nieuw, Uitgeschreven, Totaal)
            </label>
            <input
              id="ledenverloop-import"
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              onChange={(e) => void importFile(e.target.files?.[0])}
            />
          </div>
        ) : null}
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Cijfers per jaar">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Carnavalsjaar</th>
                <th scope="col">Nieuw</th>
                <th scope="col">Uitgeschreven</th>
                <th scope="col">Totaal</th>
                {canEdit ? (
                  <th scope="col">
                    <span className="visually-hidden">Aanpassen</span>
                  </th>
                ) : null}
              </tr>
            </thead>
            <tbody>
              {[...report.rows].reverse().map((r) =>
                editing?.year === r.year ? (
                  <tr key={r.year}>
                    <td>{label(r.year)}</td>
                    {(['newMembers', 'leftMembers', 'totalMembers'] as const).map((field) => (
                      <td key={field}>
                        <input
                          aria-label={`${SERIES.find((s) => s.key === field)!.label} ${label(r.year)}`}
                          inputMode="numeric"
                          size={5}
                          value={editing.values[field] ?? ''}
                          placeholder="berekend"
                          onChange={(e) =>
                            setEditing({ ...editing, values: { ...editing.values, [field]: e.target.value } })
                          }
                        />
                      </td>
                    ))}
                    <td>
                      <button
                        type="button"
                        className="button small"
                        disabled={save.isPending}
                        onClick={() =>
                          save.mutate(undefined, {
                            onSuccess: () => {
                              setMessage(`Cijfers van ${label(editing.year)} opgeslagen.`);
                              setEditing(null);
                            },
                          })
                        }
                      >
                        Opslaan
                      </button>{' '}
                      <button type="button" className="link-button" onClick={() => setEditing(null)}>
                        Annuleren
                      </button>
                    </td>
                  </tr>
                ) : (
                  <tr key={r.year}>
                    <td>{label(r.year)}</td>
                    <td>
                      <Value value={r.newMembers} manual={r.manual.newMembers} />
                    </td>
                    <td>
                      <Value value={r.leftMembers} manual={r.manual.leftMembers} />
                    </td>
                    <td>
                      <Value value={r.totalMembers} manual={r.manual.totalMembers} />
                    </td>
                    {canEdit ? (
                      <td>
                        <button
                          type="button"
                          className="link-button"
                          onClick={() =>
                            setEditing({
                              year: r.year,
                              values: {
                                newMembers: r.manual.newMembers ? String(r.newMembers ?? '') : '',
                                leftMembers: r.manual.leftMembers ? String(r.leftMembers ?? '') : '',
                                totalMembers: r.manual.totalMembers ? String(r.totalMembers ?? '') : '',
                              },
                            })
                          }
                        >
                          Aanpassen
                        </button>
                      </td>
                    ) : null}
                  </tr>
                ),
              )}
            </tbody>
          </table>
        </div>
      </details>
    </section>
  );
}

function label(year: number) {
  return `${year - 1}/${year}`;
}

function toNumber(value: string | undefined): number | null {
  return value && value.trim() !== '' ? Number(value) : null;
}

function Value({ value, manual }: { value: number | null | undefined; manual: boolean }) {
  if (value === null || value === undefined) return <span className="muted">—</span>;
  return (
    <>
      {value}
      {manual ? <span className="badge info"> ingevuld</span> : null}
    </>
  );
}

/** Lijngrafiek in SVG: nieuw en uitgeschreven op de linkeras, het totaal op de rechteras; ontbrekende jaren onderbreken de lijn. */
function LineChart({ rows }: { rows: Row[] }) {
  const width = 720;
  const height = 260;
  const pad = { top: 16, right: 52, bottom: 32, left: 44 };
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const leftMax = niceMax(Math.max(1, ...rows.flatMap((r) => [r.newMembers ?? 0, r.leftMembers ?? 0])));
  const rightMax = niceMax(Math.max(1, ...rows.map((r) => r.totalMembers ?? 0)));
  const x = (i: number) => pad.left + (rows.length <= 1 ? innerW / 2 : (i * innerW) / (rows.length - 1));
  const y = (value: number, max: number) => pad.top + innerH - (value / max) * innerH;
  const step = Math.max(1, Math.ceil(rows.length / 10));

  function path(key: 'newMembers' | 'leftMembers' | 'totalMembers', max: number) {
    let d = '';
    let pen = false;
    rows.forEach((r, i) => {
      const v = r[key];
      if (v === null || v === undefined) {
        pen = false;
        return;
      }
      d += `${pen ? 'L' : 'M'}${x(i).toFixed(1)},${y(v, max).toFixed(1)} `;
      pen = true;
    });
    return d.trim();
  }

  const summary = rows
    .slice(-3)
    .map(
      (r) =>
        `${label(r.year)}: ${r.newMembers ?? '—'} nieuw, ${r.leftMembers ?? '—'} uitgeschreven, ${r.totalMembers ?? '—'} totaal`,
    )
    .join('; ');

  return (
    <svg
      className="line-chart"
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={`Ledenverloop. Laatste jaren: ${summary}`}
    >
      {[0, 0.25, 0.5, 0.75, 1].map((f) => (
        <g key={f}>
          <line
            x1={pad.left}
            x2={width - pad.right}
            y1={pad.top + innerH * (1 - f)}
            y2={pad.top + innerH * (1 - f)}
            className="grid"
          />
          <text x={pad.left - 6} y={pad.top + innerH * (1 - f) + 4} textAnchor="end" className="axis">
            {Math.round(leftMax * f)}
          </text>
          <text x={width - pad.right + 6} y={pad.top + innerH * (1 - f) + 4} className="axis">
            {Math.round(rightMax * f)}
          </text>
        </g>
      ))}
      {rows.map((r, i) =>
        i % step === 0 || i === rows.length - 1 ? (
          <text key={r.year} x={x(i)} y={height - 10} textAnchor="middle" className="axis">
            {r.year}
          </text>
        ) : null,
      )}
      {SERIES.map((s) => (
        <path
          key={s.key}
          d={path(s.key, s.axis === 'right' ? rightMax : leftMax)}
          fill="none"
          stroke={s.color}
          strokeWidth={s.axis === 'right' ? 3 : 2}
        />
      ))}
      {SERIES.flatMap((s) =>
        rows.map((r, i) => {
          const v = r[s.key];
          return v === null || v === undefined ? null : (
            <circle
              key={`${s.key}-${r.year}`}
              cx={x(i)}
              cy={y(v, s.axis === 'right' ? rightMax : leftMax)}
              r={rows.length > 30 ? 1.5 : 3}
              fill={s.color}
            >
              <title>{`${s.label} ${label(r.year)}: ${v}`}</title>
            </circle>
          );
        }),
      )}
    </svg>
  );
}

/** Bovenkant van de as zodat de vier tussenstappen ronde getallen zijn (bijv. 40 → 10, 20, 30, 40). */
function niceMax(value: number) {
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const step = [0.5, 1, 2, 2.5, 5, 10].map((f) => f * magnitude).find((s) => s * 4 >= value) ?? 10 * magnitude;
  return step * 4;
}
