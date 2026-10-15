import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

type Program = Schemas['ProgramView'];
type Item = Schemas['ProgramItemView'];

const KEY = ['dance-festival', 'program'];

/**
 * Programma van het Dansgardefestival (2026-10-15): categorieën uit de goedgekeurde dansen en blokken (opening, pauze,
 * prijsuitreiking). Per categorie een starttijd (leeg = aansluiten op het vorige) en minuten per dans; categorieën en
 * dansen schuiven met ↑↓; loten husselt de volgorde binnen elke categorie; afdrukken op A4.
 */
export function FestivalProgramTab() {
  const api = useApi();
  const program = useQuery({
    queryKey: KEY,
    queryFn: async () => (await api.GET('/api/v1/admin/dance-festival/program')).data ?? null,
  });
  const [open, setOpen] = useState<string[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [drawing, setDrawing] = useState(false);
  const [addingBlock, setAddingBlock] = useState(false);
  const [downloadError, setDownloadError] = useState<unknown>(null);
  const draw = useApiMutation(() => api.POST('/api/v1/admin/dance-festival/program/draw'), [KEY]);
  const moveItem = useApiMutation(
    ({ id, direction }: { id: string; direction: number }) =>
      api.POST('/api/v1/admin/dance-festival/program/items/{id}/move', {
        params: { path: { id } },
        body: { direction },
      }),
    [KEY],
  );
  const moveDance = useApiMutation(
    ({ id, direction }: { id: string; direction: number }) =>
      api.POST('/api/v1/admin/dance-festival/program/dances/{entryId}/move', {
        params: { path: { entryId: id } },
        body: { direction },
      }),
    [KEY],
  );

  const p = program.data;
  if (!p) return program.error ? <ProblemAlert error={program.error} /> : <p>Laden…</p>;

  async function print() {
    setDownloadError(null);
    try {
      const response = await api.GET('/api/v1/admin/dance-festival/program/pdf', { parseAs: 'blob' });
      if (response.data) {
        const url = URL.createObjectURL(response.data as Blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'programma-dansgardefestival.pdf';
        a.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  const toggle = (id: string) => setOpen((o) => (o.includes(id) ? o.filter((x) => x !== id) : [...o, id]));
  const error = draw.error ?? moveItem.error ?? moveDance.error ?? downloadError;

  return (
    <>
      <div className="kpis">
        <StartKpi program={p} />
        <div className="kpi">
          <span className="kpi-label">Categorieën</span>
          <span className="kpi-value">{p.categories}</span>
          <span className="kpi-hint">{p.dances} goedgekeurde dansen</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Einde</span>
          <span className="kpi-value">{p.end}</span>
          <span className="kpi-hint">inclusief blokken</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Loting</span>
          <span className="kpi-value">{p.drawnAt ? 'gedaan' : 'nog niet'}</span>
          <span className="kpi-hint">{p.drawnAt ? formatDateTime(p.drawnAt) : 'volgorde op inschrijfnummer'}</span>
        </div>
      </div>

      <div className="page-header">
        <h2>Programma</h2>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setDrawing(true)}>
            Loten
          </button>
          <button type="button" className="button secondary" onClick={() => setAddingBlock(true)}>
            + Blok (pauze…)
          </button>
          <button type="button" className="button" onClick={() => void print()}>
            Afdrukken (A4)
          </button>
        </div>
      </div>
      <p className="muted small-text">
        De starttijd van een categorie sluit vanzelf aan op het einde van het vorige onderdeel; vul een tijd in om een
        gat of uitloop te plannen (leeg = weer aansluiten). Schuif categorieën en dansen met ↑↓.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={error} />

      {p.items.length === 0 || p.dances === 0 ? (
        <p className="muted">
          Nog geen goedgekeurde dansen; het programma vult zich met de goedgekeurde inschrijvingen.
        </p>
      ) : null}

      <ol className="program-list" aria-label="Programma">
        {p.items.map((item, index) => (
          <li key={item.id} className={`program-item${item.kind === 'Block' ? ' block' : ''}`}>
            <ItemRow
              key={`${item.id}-${item.start}-${item.minutes}-${item.startOverridden}`}
              item={item}
              first={index === 0}
              last={index === p.items.length - 1}
              expanded={open.includes(item.id)}
              onToggle={() => toggle(item.id)}
              onMove={(direction) => moveItem.mutate({ id: item.id, direction })}
            />
            {item.kind === 'Category' && open.includes(item.id) ? (
              <ol className="program-dances" aria-label={`Dansen ${item.title}`}>
                {item.dances.map((d, i) => (
                  <li key={d.entryId}>
                    <span className="muted">{d.number}</span>
                    <span className="muted">{d.time}</span>
                    <span className="grow">
                      <strong>{d.title ? `${d.title} · ${d.names}` : d.names}</strong>
                      <span className="muted small-text"> {d.club}</span>
                    </span>
                    <Arrows
                      label={d.names}
                      first={i === 0}
                      last={i === item.dances.length - 1}
                      onMove={(direction) => moveDance.mutate({ id: d.entryId, direction })}
                    />
                  </li>
                ))}
              </ol>
            ) : null}
          </li>
        ))}
      </ol>

      <ConfirmDialog
        open={drawing}
        title="Loten?"
        message="De volgorde binnen elke categorie wordt willekeurig. Daarna kun je nog schuiven."
        confirmLabel="Loten"
        busy={draw.isPending}
        onCancel={() => setDrawing(false)}
        onConfirm={() =>
          draw.mutate(undefined, {
            onSuccess: () => setMessage('Geloot: de volgorde binnen elke categorie is nieuw.'),
            onSettled: () => setDrawing(false),
          })
        }
      />
      {addingBlock ? (
        <BlockDialog
          items={p.items}
          onClose={(text) => {
            setAddingBlock(false);
            if (text) setMessage(text);
          }}
        />
      ) : null}
    </>
  );
}

function StartKpi({ program }: { program: Program }) {
  const api = useApi();
  const [value, setValue] = useState(program.start);
  const save = useApiMutation(
    () => api.PUT('/api/v1/admin/dance-festival/program/start', { body: { start: value } }),
    [KEY],
  );
  return (
    <div className="kpi">
      <label className="kpi-label" htmlFor="programma-start">
        Start (opening)
      </label>
      <span className="program-start">
        <input
          id="programma-start"
          type="time"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onBlur={() => value && value !== program.start && save.mutate(undefined)}
        />
      </span>
      <span className="kpi-hint">{save.error ? 'Opslaan mislukt' : 'de rest sluit hierop aan'}</span>
    </div>
  );
}

function ItemRow({
  item,
  first,
  last,
  expanded,
  onToggle,
  onMove,
}: {
  item: Item;
  first: boolean;
  last: boolean;
  expanded: boolean;
  onToggle: () => void;
  onMove: (direction: number) => void;
}) {
  const api = useApi();
  const [start, setStart] = useState(item.startOverridden ? item.start : '');
  const [minutes, setMinutes] = useState(String(item.minutes));
  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/dance-festival/program/items/{id}', {
        params: { path: { id: item.id } },
        body: { start: start || null, minutes: Number(minutes) || 0, title: null },
      }),
    [KEY],
  );
  const remove = useApiMutation(
    () => api.DELETE('/api/v1/admin/dance-festival/program/items/{id}', { params: { path: { id: item.id } } }),
    [KEY],
  );
  const follow = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/dance-festival/program/items/{id}', {
        params: { path: { id: item.id } },
        body: { start: null, minutes: item.minutes, title: null },
      }),
    [KEY],
  );
  const commit = () => {
    if ((start || '') !== (item.startOverridden ? item.start : '') || Number(minutes) !== item.minutes)
      save.mutate(undefined);
  };
  const category = item.kind === 'Category';

  return (
    <div className="program-row">
      {category ? (
        <button
          type="button"
          className="link-button program-toggle"
          aria-expanded={expanded}
          aria-label={`${expanded ? 'Verberg' : 'Toon'} dansen ${item.title}`}
          onClick={onToggle}
        >
          {expanded ? '▾' : '▸'}
        </button>
      ) : null}
      <input
        className="program-time"
        type="time"
        aria-label={`Starttijd ${item.title}`}
        placeholder={item.start}
        value={start || item.start}
        onChange={(e) => setStart(e.target.value)}
        onBlur={commit}
      />
      <span className="grow">
        <strong>{item.title}</strong>
        <span className="muted small-text">
          {' '}
          {category
            ? `${item.dances.length} ${item.dances.length === 1 ? 'dans' : 'dansen'} · tot ${item.end}`
            : `tot ${item.end}`}
          {item.startOverridden ? ' · vaste tijd' : ''}
        </span>
      </span>
      {item.startOverridden ? (
        <button type="button" className="link-button small-text" onClick={() => follow.mutate(undefined)}>
          Aansluiten
        </button>
      ) : null}
      <label className="program-minutes">
        <span className="muted small-text">{category ? 'min. per dans' : 'minuten'}</span>
        <input
          type="number"
          inputMode="numeric"
          min={category ? 1 : 0}
          max={240}
          aria-label={`${category ? 'Minuten per dans' : 'Duur in minuten'} ${item.title}`}
          value={minutes}
          onChange={(e) => setMinutes(e.target.value)}
          onBlur={commit}
        />
      </label>
      <Arrows label={item.title} first={first} last={last} onMove={onMove} />
      {!category ? (
        <button
          type="button"
          className="link-button"
          aria-label={`${item.title} verwijderen`}
          onClick={() => remove.mutate(undefined)}
        >
          ✕
        </button>
      ) : null}
      <ProblemAlert error={save.error ?? remove.error ?? follow.error} />
    </div>
  );
}

function Arrows({
  label,
  first,
  last,
  onMove,
}: {
  label: string;
  first: boolean;
  last: boolean;
  onMove: (direction: number) => void;
}) {
  return (
    <span className="order-buttons">
      <button
        type="button"
        className="button secondary small"
        disabled={first}
        aria-label={`${label} omhoog`}
        onClick={() => onMove(-1)}
      >
        ↑
      </button>
      <button
        type="button"
        className="button secondary small"
        disabled={last}
        aria-label={`${label} omlaag`}
        onClick={() => onMove(1)}
      >
        ↓
      </button>
    </span>
  );
}

function BlockDialog({ items, onClose }: { items: Item[]; onClose: (message?: string) => void }) {
  const api = useApi();
  const [title, setTitle] = useState('Pauze');
  const [minutes, setMinutes] = useState('15');
  const [after, setAfter] = useState(items.at(-2)?.id ?? '');
  const add = useApiMutation(
    () =>
      api.POST('/api/v1/admin/dance-festival/program/blocks', {
        body: { title, minutes: Number(minutes) || 0, afterItemId: after || null },
      }),
    [KEY],
  );
  return (
    <Dialog open title="Blok toevoegen" onClose={() => onClose()}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate(undefined, { onSuccess: () => onClose(`${title} toegevoegd.`) });
        }}
      >
        <div className="form-grid">
          <Field label="Naam" required maxLength={80} value={title} onChange={(e) => setTitle(e.target.value)} />
          <Field
            label="Minuten"
            type="number"
            min={0}
            max={240}
            value={minutes}
            onChange={(e) => setMinutes(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="blok-na">Na</label>
          <select id="blok-na" value={after} onChange={(e) => setAfter(e.target.value)}>
            {items.map((i) => (
              <option key={i.id} value={i.id}>
                {i.start} {i.title}
              </option>
            ))}
          </select>
        </div>
        <ProblemAlert error={add.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={add.isPending}>
            Toevoegen
          </button>
        </div>
      </form>
    </Dialog>
  );
}
