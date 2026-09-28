import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { Link } from '@tanstack/react-router';
import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { ApiError } from '../api/errors';
import {
  useApiMutation,
  useMe,
  useParadeComposition,
  type CompositionCard,
  type StartNumberMode,
  type StartNumberPreview,
} from '../api/hooks';
import { Icon } from '../components/Icon';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { registrationStatusLabels } from '../format';

const meters = (value: number) => `${(Math.round(value * 10) / 10).toString().replace('.', ',')} m`;

/** Inhoud van een kaart: opgave- en startnummer, groep, categorie, deelnemers, lengte en een teken bij extra info. */
function CardBody({ card }: { card: CompositionCard }) {
  return (
    <div className="grow">
      <div>
        <strong>
          <Link to="/optocht/inschrijvingen/$id" params={{ id: card.id }}>
            {card.groupName ?? 'Zonder naam'}
          </Link>
        </strong>{' '}
        <span className="muted small-text">
          nr. {card.registrationNumber} · start {card.startNumber ?? '–'}
          {card.status === 'StartNumberAssigned' ? ' (gepubliceerd)' : ''}
        </span>
      </div>
      <div className="small-text">
        {card.categoryName ?? 'Zonder categorie'}
        {card.youth ? <span className="badge info">Jeugd</span> : null}
        {card.hasVehicle ? <span className="badge">Voertuig</span> : null} · {card.participants} deelnemers ·{' '}
        {meters(card.lengthMeters)} {card.lengthMeasured ? 'gemeten' : 'geschat'}
        {card.subject ? ` · ${card.subject}` : ''}
      </div>
      {card.additionalInformation ? (
        <div className="small-text" title={card.additionalInformation}>
          <Icon name="waarschuwing" size={14} /> Extra info: {card.additionalInformation.slice(0, 80)}
          {card.additionalInformation.length > 80 ? '…' : ''}
        </div>
      ) : null}
    </div>
  );
}

function SortableCard({
  card,
  index,
  count,
  canEdit,
  onMove,
  onRemove,
}: {
  card: CompositionCard;
  index: number;
  count: number;
  canEdit: boolean;
  onMove: (from: number, to: number) => void;
  onRemove: (id: string) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: card.id,
    disabled: !canEdit,
  });
  const name = card.groupName ?? 'Zonder naam';
  return (
    <li
      ref={setNodeRef}
      className={`list-row lineup-card${isDragging ? ' dragging' : ''}`}
      style={{ transform: CSS.Transform.toString(transform), transition }}
    >
      <span className="lineup-position" aria-label={`Positie ${index + 1}`}>
        {index + 1}
      </span>
      {canEdit ? (
        <button
          type="button"
          className="icon-button drag-handle"
          aria-label={`Verplaats ${name}`}
          {...attributes}
          {...listeners}
        >
          <Icon name="slepen" />
        </button>
      ) : null}
      <CardBody card={card} />
      {canEdit ? (
        <div className="actions">
          <button
            type="button"
            className="button secondary small"
            disabled={index === 0}
            onClick={() => onMove(index, index - 1)}
          >
            Omhoog <span className="visually-hidden">{name}</span>
          </button>
          <button
            type="button"
            className="button secondary small"
            disabled={index === count - 1}
            onClick={() => onMove(index, index + 1)}
          >
            Omlaag <span className="visually-hidden">{name}</span>
          </button>
          <button type="button" className="button secondary small" onClick={() => onRemove(card.id)}>
            Uit volgorde <span className="visually-hidden">{name}</span>
          </button>
        </div>
      ) : null}
    </li>
  );
}

/**
 * Optocht samenstellen (fase 12b, docs/13 §7.3): goedgekeurde groepen op volgorde zetten door te slepen (ook met het
 * toetsenbord: spatie, pijltjes, spatie) of met de knoppen Omhoog/Omlaag. Elke wijziging wordt direct bewaard; slepen
 * wijzigt nooit een startnummer. Daarvoor is "Startnummers genereren" met eerst een voorbeeld.
 */
export function ParadeCompositionPage() {
  const api = useApi();
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canEdit = permissions.includes('parade.manage');
  const canAssign = permissions.includes('parade.assign-start-number');
  const composition = useParadeComposition();
  const [order, setOrder] = useState<string[]>([]);
  const [version, setVersion] = useState(0);
  const [status, setStatus] = useState<string | null>(null);
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  useEffect(() => {
    if (composition.data) {
      setOrder(composition.data.ordered.map((c) => c.id));
      setVersion(composition.data.version);
    }
  }, [composition.data]);

  const cards = useMemo(() => {
    const all = [...(composition.data?.ordered ?? []), ...(composition.data?.unassigned ?? [])];
    return new Map(all.map((c) => [c.id, c]));
  }, [composition.data]);
  const ordered = order.map((id) => cards.get(id)).filter((c): c is CompositionCard => Boolean(c));
  const unassigned = [...cards.values()].filter((c) => !order.includes(c.id));

  const save = useApiMutation(
    async (ids: string[]) => {
      const { data } = await api.PUT('/api/v1/admin/parade-composition/order', { body: { version, orderedIds: ids } });
      return data;
    },
    [['parade-composition'], ['parade-registrations']],
  );
  const changedElsewhere = save.error instanceof ApiError && save.error.problem.status === 412;

  function commit(next: string[], text: string) {
    const previous = order;
    setOrder(next);
    setStatus(null);
    save.mutate(next, {
      onSuccess: (result) => {
        const saved = result as { version: number } | undefined;
        if (saved) setVersion(saved.version);
        setStatus(`${text} Volgorde bewaard.`);
      },
      // Niet bewaard: de getoonde volgorde terugzetten, zodat scherm en server gelijk blijven.
      onError: () => setOrder(previous),
    });
  }

  function onDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (over && active.id !== over.id) {
      const from = order.indexOf(String(active.id));
      const to = order.indexOf(String(over.id));
      commit(
        arrayMove(order, from, to),
        `${cards.get(String(active.id))?.groupName ?? 'Groep'} staat nu op positie ${to + 1}.`,
      );
    }
  }

  if (!composition.data) {
    return (
      <>
        <h1>Optocht samenstellen</h1>
        {composition.error ? <ProblemAlert error={composition.error} /> : <p>Laden…</p>}
      </>
    );
  }
  const c = composition.data;
  const lengthOrdered = ordered.reduce((sum, card) => sum + card.lengthMeters + c.defaultSpacingMeters, 0);

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Optocht samenstellen</h1>
          <p className="page-subtitle">
            Zet de goedgekeurde groepen op volgorde door te slepen of met Omhoog/Omlaag. Elke wijziging wordt direct
            bewaard. Slepen verandert geen startnummers; gebruik daarvoor Startnummers genereren.
          </p>
        </div>
      </div>
      <section className="kpis" aria-label="Totalen van de volgorde">
        <div className="kpi">
          <span className="kpi-label">Ingedeeld</span>
          <span className="kpi-value">{ordered.length}</span>
          <span className="kpi-hint">{unassigned.length} nog niet ingedeeld</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Deelnemers</span>
          <span className="kpi-value">{ordered.reduce((sum, card) => sum + card.participants, 0)}</span>
          <span className="kpi-hint">In de volgorde</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Lengte</span>
          <span className="kpi-value">{meters(lengthOrdered)}</span>
          <span className="kpi-hint">Inclusief {meters(c.defaultSpacingMeters)} tussenruimte per groep</span>
        </div>
      </section>
      <div role="status" aria-live="polite" className="visually-hidden">
        {status}
      </div>
      <SuccessMessage message={status} />
      {changedElsewhere ? (
        <div role="alert" className="alert alert-warning">
          Iemand anders heeft de volgorde intussen gewijzigd.{' '}
          <button
            type="button"
            className="button small"
            onClick={() => {
              save.reset();
              void composition.refetch().then(({ data }) => {
                if (data) {
                  setOrder(data.ordered.map((card) => card.id));
                  setVersion(data.version);
                }
              });
            }}
          >
            Opnieuw laden
          </button>
        </div>
      ) : (
        <ProblemAlert error={save.error} />
      )}
      {c.warnings.length ? (
        <div className="alert alert-warning" role="status">
          <strong>Let op:</strong>
          <ul>
            {c.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        </div>
      ) : null}
      <div className="columns">
        <section className="card" aria-labelledby="volgorde">
          <h2 id="volgorde">Volgorde ({ordered.length})</h2>
          {ordered.length === 0 ? (
            <p className="muted">Nog geen groepen ingedeeld. Voeg ze toe vanuit Niet ingedeeld.</p>
          ) : null}
          <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
            <SortableContext items={order} strategy={verticalListSortingStrategy}>
              <ol className="list lineup">
                {ordered.map((card, index) => (
                  <SortableCard
                    key={card.id}
                    card={card}
                    index={index}
                    count={ordered.length}
                    canEdit={canEdit && !changedElsewhere}
                    onMove={(from, to) =>
                      commit(arrayMove(order, from, to), `${card.groupName ?? 'Groep'} staat nu op positie ${to + 1}.`)
                    }
                    onRemove={(id) =>
                      commit(
                        order.filter((x) => x !== id),
                        `${card.groupName ?? 'Groep'} is uit de volgorde gehaald.`,
                      )
                    }
                  />
                ))}
              </ol>
            </SortableContext>
          </DndContext>
        </section>
        <section className="card" aria-labelledby="niet-ingedeeld">
          <h2 id="niet-ingedeeld">Niet ingedeeld ({unassigned.length})</h2>
          {unassigned.length === 0 ? <p className="muted">Alle goedgekeurde groepen zijn ingedeeld.</p> : null}
          <ul className="list">
            {unassigned.map((card) => (
              <li key={card.id} className="list-row lineup-card">
                <CardBody card={card} />
                {canEdit && !changedElsewhere ? (
                  <button
                    type="button"
                    className="button secondary small"
                    onClick={() => commit([...order, card.id], `${card.groupName ?? 'Groep'} is achteraan toegevoegd.`)}
                  >
                    Toevoegen <span className="visually-hidden">{card.groupName}</span>
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        </section>
      </div>
      {canAssign ? <GenerateStartNumbers version={version} onApplied={() => void composition.refetch()} /> : null}
    </>
  );
}

const modeLabels: Record<StartNumberMode, string> = {
  FillEmpty: 'Alleen lege startnummers invullen',
  Renumber: 'Alle ingedeelde groepen opnieuw nummeren',
};

/** "Startnummers genereren uit volgorde": kiezen, voorbeeld bekijken, bevestigen (bij gepubliceerde nummers met HERNUMMER). */
function GenerateStartNumbers({ version, onApplied }: { version: number; onApplied: () => void }) {
  const api = useApi();
  const [mode, setMode] = useState<StartNumberMode>('FillEmpty');
  const [startAt, setStartAt] = useState('1');
  const [preview, setPreview] = useState<StartNumberPreview | null>(null);
  const [confirmation, setConfirmation] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const load = useApiMutation(async () => {
    const { data } = await api.POST('/api/v1/admin/parade-composition/start-numbers/preview', {
      body: { mode, startAt: Number(startAt) || 1 },
    });
    return data;
  }, []);
  const apply = useApiMutation(async () => {
    const { data } = await api.POST('/api/v1/admin/parade-composition/start-numbers/apply', {
      body: {
        version: preview?.version ?? version,
        mode,
        startAt: Number(startAt) || 1,
        confirmation: confirmation || null,
      },
    });
    return data;
  }, [['parade-composition'], ['parade-registrations'], ['parade-registration']]);

  function showPreview(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    setConfirmation('');
    apply.reset();
    load.mutate(undefined, { onSuccess: (data) => setPreview((data as StartNumberPreview | undefined) ?? null) });
  }

  return (
    <section className="card" aria-labelledby="genereren">
      <h2 id="genereren">Startnummers genereren uit de volgorde</h2>
      <SuccessMessage message={message} />
      <form className="toolbar" onSubmit={showPreview}>
        <fieldset className="field">
          <legend>Wat wil je doen?</legend>
          {(Object.keys(modeLabels) as StartNumberMode[]).map((m) => (
            <label key={m} className="check">
              <input type="radio" name="modus" value={m} checked={mode === m} onChange={() => setMode(m)} />{' '}
              {modeLabels[m]}
            </label>
          ))}
        </fieldset>
        <div className="field">
          <label htmlFor="eerste-nummer">Eerste startnummer</label>
          <input
            id="eerste-nummer"
            inputMode="numeric"
            maxLength={4}
            value={startAt}
            onChange={(e) => setStartAt(e.target.value.replace(/\D/g, ''))}
          />
        </div>
        <button type="submit" className="button secondary" disabled={load.isPending}>
          Voorbeeld tonen
        </button>
      </form>
      <ProblemAlert error={load.error} />
      {preview ? (
        <>
          {preview.changes.length === 0 ? (
            <p>Er verandert niets: alle groepen hebben al het juiste startnummer.</p>
          ) : (
            <table className="table">
              <caption>Voorbeeld: {preview.changes.length} wijzigingen</caption>
              <thead>
                <tr>
                  <th scope="col">Groep</th>
                  <th scope="col">Opgave</th>
                  <th scope="col">Nu</th>
                  <th scope="col">Wordt</th>
                </tr>
              </thead>
              <tbody>
                {preview.changes.map((change) => (
                  <tr key={change.id}>
                    <th scope="row">
                      {change.groupName}
                      {change.published ? <span className="badge warn">Gepubliceerd</span> : null}
                    </th>
                    <td>{change.registrationNumber}</td>
                    <td>{change.oldStartNumber ?? '–'}</td>
                    <td>
                      <strong>{change.newStartNumber ?? 'leeg'}</strong>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {preview.affectsPublished ? (
            <div className="field">
              <label htmlFor="bevestiging">
                Er veranderen gepubliceerde startnummers; die groepen krijgen bericht. Typ HERNUMMER om te bevestigen.
              </label>
              <input
                id="bevestiging"
                value={confirmation}
                onChange={(e) => setConfirmation(e.target.value)}
                autoComplete="off"
              />
            </div>
          ) : null}
          <ProblemAlert error={apply.error} />
          {preview.changes.length ? (
            <div className="actions">
              <button
                type="button"
                className="button"
                disabled={apply.isPending || (preview.affectsPublished && confirmation.trim() !== 'HERNUMMER')}
                onClick={() =>
                  apply.mutate(undefined, {
                    onSuccess: (data) => {
                      setMessage(
                        `${(data as { changed: number } | undefined)?.changed ?? 0} startnummers aangepast. Publiceer ze op de pagina Inschrijvingen.`,
                      );
                      setPreview(null);
                      onApplied();
                    },
                  })
                }
              >
                Toepassen
              </button>
            </div>
          ) : null}
        </>
      ) : null}
      <p className="muted small-text">
        Status van een groep: {registrationStatusLabels.Approved} of {registrationStatusLabels.StartNumberAssigned}.
        Nieuwe startnummers worden pas aan groepen gemeld na Startnummers publiceren (Inschrijvingen); gewijzigde, al
        gepubliceerde nummers direct.
      </p>
    </section>
  );
}
