import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { createContext, useContext, useMemo, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, type Schemas } from '../api/hooks';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

type TaskView = Schemas['TaskView'];
type TaskStatus = Schemas['CarnivalTaskStatus'];
type YearView = Schemas['TaskYearView'];
type Category = Schemas['TaskCategoryView'];
type Person = Schemas['TaskPerson'];
type ReportRow = Schemas['TaskReportRow'];
type TaskBody = Schemas['TaskRequest'];
type CategoryBody = Schemas['TaskCategoryRequest'];
export type TaskListKind = 'carnival' | 'festival';

/**
 * Twee takenlijsten (2026-10-15): carnaval (/admin/tasks, recht task.manage) en het Dansgardefestival
 * (/admin/festival/tasks, recht festival.manage). Zelfde schermen, andere adressen.
 */
function useTaskApi(list: TaskListKind) {
  const api = useApi();
  return useMemo(() => {
    const f = list === 'festival';
    return {
      key: ['tasks', list],
      year: async (yearId: number | null) =>
        (f
          ? await api.GET('/api/v1/admin/festival/tasks', { params: { query: { yearId: yearId ?? undefined } } })
          : await api.GET('/api/v1/admin/tasks', { params: { query: { yearId: yearId ?? undefined } } })
        ).data ?? null,
      categories: async () =>
        (f ? await api.GET('/api/v1/admin/festival/tasks/categories') : await api.GET('/api/v1/admin/tasks/categories'))
          .data ?? [],
      members: async (search: string) =>
        (f
          ? await api.GET('/api/v1/admin/festival/tasks/members', { params: { query: { search } } })
          : await api.GET('/api/v1/admin/tasks/members', { params: { query: { search } } })
        ).data ?? [],
      create: (yearId: number, body: TaskBody) =>
        f
          ? api.POST('/api/v1/admin/festival/tasks', { params: { query: { yearId } }, body })
          : api.POST('/api/v1/admin/tasks', { params: { query: { yearId } }, body }),
      update: (id: string, body: TaskBody) =>
        f
          ? api.PUT('/api/v1/admin/festival/tasks/{id}', { params: { path: { id } }, body })
          : api.PUT('/api/v1/admin/tasks/{id}', { params: { path: { id } }, body }),
      remove: (id: string) =>
        f
          ? api.DELETE('/api/v1/admin/festival/tasks/{id}', { params: { path: { id } } })
          : api.DELETE('/api/v1/admin/tasks/{id}', { params: { path: { id } } }),
      copy: async (fromYearId: number, taskIds: string[]) =>
        (f
          ? await api.POST('/api/v1/admin/festival/tasks/copy', { body: { fromYearId, taskIds } })
          : await api.POST('/api/v1/admin/tasks/copy', { body: { fromYearId, taskIds } })
        ).data,
      report: async () =>
        (f ? await api.GET('/api/v1/admin/festival/tasks/report') : await api.GET('/api/v1/admin/tasks/report')).data ??
        null,
      addCategory: (body: CategoryBody) =>
        f
          ? api.POST('/api/v1/admin/festival/tasks/categories', { body })
          : api.POST('/api/v1/admin/tasks/categories', { body }),
      updateCategory: (id: string, body: CategoryBody) =>
        f
          ? api.PUT('/api/v1/admin/festival/tasks/categories/{id}', { params: { path: { id } }, body })
          : api.PUT('/api/v1/admin/tasks/categories/{id}', { params: { path: { id } }, body }),
      removeCategory: (id: string) =>
        f
          ? api.DELETE('/api/v1/admin/festival/tasks/categories/{id}', { params: { path: { id } } })
          : api.DELETE('/api/v1/admin/tasks/categories/{id}', { params: { path: { id } } }),
    };
  }, [api, list]);
}

type TaskApi = ReturnType<typeof useTaskApi>;
const TaskApiContext = createContext<TaskApi | null>(null);
const useTasks = () => useContext(TaskApiContext)!;
const STATUS: Record<TaskStatus, [string, string]> = {
  NotStarted: ['Nog niet gestart', 'neutral'],
  InProgress: ['Bezig', 'info'],
  Done: ['Afgerond', 'ok'],
  Cancelled: ['Geannuleerd', 'neutral'],
};
type Filter = 'all' | 'mine' | 'progress' | 'overdue' | 'done';

function useTaskYear(tasks: TaskApi, yearId: number | null) {
  return useQuery({ queryKey: [...tasks.key, 'year', yearId], queryFn: () => tasks.year(yearId) });
}

function useCategories(tasks: TaskApi) {
  return useQuery({ queryKey: [...tasks.key, 'categories'], queryFn: () => tasks.categories() });
}

/** Korte voortgangsbalk met percentage, zoals bij de campagne. */
export function MiniBar({ percent, done, width = 90 }: { percent: number; done?: boolean; width?: number }) {
  return (
    <span className="mini-bar">
      <span className="mini-bar-track" style={{ width }} aria-hidden="true">
        <span className={`mini-bar-fill${done || percent >= 100 ? ' done' : ''}`} style={{ width: `${percent}%` }} />
      </span>
      <strong>{percent}%</strong>
    </span>
  );
}

/**
 * Takenlijst carnaval (2026-10-15): taken per carnavalsjaar en categorie, met leden, status, voortgang, gestart op en
 * verwacht klaar; kopiëren naar volgend jaar met vinkjes. Met <c>list="festival"</c> de lijst van het Dansgardefestival
 * (als tabblad op de festivalpagina), met import uit het draaiboek en de rapportage erbij.
 */
export function TasksPage() {
  return <TaskListPage list="carnival" />;
}

export function TaskListPage({ list }: { list: TaskListKind }) {
  const tasksApi = useTaskApi(list);
  return (
    <TaskApiContext.Provider value={tasksApi}>
      <TaskList festival={list === 'festival'} />
    </TaskApiContext.Provider>
  );
}

function TaskList({ festival }: { festival: boolean }) {
  const tasksApi = useTasks();
  const auth = useAuth();
  const [yearId, setYearId] = useState<number | null>(null);
  const year = useTaskYear(tasksApi, yearId);
  const categories = useCategories(tasksApi);
  const [showReport, setShowReport] = useState(false);
  const [importError, setImportError] = useState<unknown>(null);
  const me = useMe();
  const [filter, setFilter] = useState<Filter>('all');
  const [category, setCategory] = useState('');
  const [editing, setEditing] = useState<TaskView | 'new' | null>(null);
  const [copying, setCopying] = useState(false);
  const [managing, setManaging] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const y = year.data;
  if (!y) return year.error ? <ProblemAlert error={year.error} /> : <p>Laden…</p>;
  const myMember = me.data?.memberId ?? null;
  const tasks = y.tasks.filter(
    (t) =>
      (!category || t.categoryId === category) &&
      (filter === 'all' ||
        (filter === 'mine' && t.assignees.some((a) => a.memberId === myMember)) ||
        (filter === 'progress' && t.status === 'InProgress') ||
        (filter === 'overdue' && t.overdue) ||
        (filter === 'done' && t.status === 'Done')),
  );
  const groups = groupBy(tasks, (t) => t.categoryName);
  const count = (f: Filter) =>
    y.tasks.filter((t) =>
      f === 'mine'
        ? t.assignees.some((a) => a.memberId === myMember)
        : f === 'progress'
          ? t.status === 'InProgress'
          : f === 'overdue'
            ? t.overdue
            : f === 'done'
              ? t.status === 'Done'
              : true,
    ).length;
  const s = y.summary;

  async function importFile(file: File | undefined) {
    if (!file || !y) return;
    setImportError(null);
    const form = new FormData();
    form.append('file', file);
    try {
      const result = await uploadJson<{ categories: number; tasks: number; skipped: number }>(
        auth,
        `/api/v1/admin/festival/tasks/import?yearId=${y.yearId}`,
        form,
      );
      setMessage(
        `Draaiboek ingelezen: ${result.tasks} taken en ${result.categories} categorieën toegevoegd` +
          (result.skipped ? `, ${result.skipped} bestonden al.` : '.'),
      );
      await Promise.all([year.refetch(), categories.refetch()]);
    } catch (error) {
      setImportError(error);
    }
  }

  if (festival && showReport)
    return (
      <>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setShowReport(false)}>
            ← Terug naar de takenlijst
          </button>
        </div>
        <TaskReport festival />
      </>
    );

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          {festival ? <h2>Takenlijst Dansgardefestival {y.yearName}</h2> : <h1>Takenlijst carnaval {y.yearName}</h1>}
          <p className="page-subtitle">
            Carnaval begint {formatDate(y.carnivalStartDate)} · nog {y.daysUntilCarnival} dagen
          </p>
        </div>
        <div className="actions">
          <div className="field">
            <label htmlFor="taken-jaar" className="visually-hidden">
              Carnavalsjaar
            </label>
            <select id="taken-jaar" value={y.yearId} onChange={(e) => setYearId(Number(e.target.value))}>
              {y.years.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </select>
          </div>
          <button type="button" className="button secondary" onClick={() => setManaging(true)}>
            Categorieën
          </button>
          {festival ? (
            <>
              <button type="button" className="button secondary" onClick={() => setShowReport(true)}>
                Rapportage
              </button>
              <label className="button secondary file-button">
                Draaiboek importeren
                <input
                  type="file"
                  className="visually-hidden"
                  accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                  onChange={(e) => {
                    void importFile(e.target.files?.[0]);
                    e.target.value = '';
                  }}
                />
              </label>
            </>
          ) : null}
          <button
            type="button"
            className="button secondary"
            onClick={() => setCopying(true)}
            disabled={y.tasks.length === 0}
          >
            Kopiëren naar volgend jaar…
          </button>
          <button
            type="button"
            className="button"
            onClick={() => setEditing('new')}
            disabled={!categories.data?.some((c) => c.active)}
            title={categories.data?.some((c) => c.active) ? undefined : 'Maak eerst een categorie'}
          >
            + Taak
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={importError} />

      <section className="card" aria-labelledby="taken-totaal">
        <div className="card-header">
          <h2 id="taken-totaal">
            {festival ? 'Voorbereiding festival' : 'Voorbereiding carnaval'}: {s.percent}%
          </h2>
          <span className="muted">
            {s.done} van {s.total - s.cancelled} taken afgerond · {s.inProgress} bezig · {s.overdue} over datum
          </span>
        </div>
        <div
          className="progress big"
          role="progressbar"
          aria-label="Voortgang voorbereiding"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={s.percent}
        >
          <span style={{ width: `${s.percent}%` }} />
        </div>
        <p className="muted small-text">
          Gemiddelde van alle taken: nog niet gestart 0%, bezig het ingevulde percentage, afgerond 100%; geannuleerd
          telt niet mee.
        </p>
      </section>

      <div className="toolbar">
        <div className="chips" role="group" aria-label="Filter">
          {(
            [
              ['all', 'Alle'],
              ['mine', 'Mijn taken'],
              ['progress', 'Bezig'],
              ['overdue', 'Over datum'],
              ['done', 'Afgerond'],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              className={`chip${filter === key ? ' active' : ''}`}
              aria-pressed={filter === key}
              onClick={() => setFilter(key)}
            >
              {label} ({count(key)})
            </button>
          ))}
        </div>
        <div className="field">
          <label htmlFor="taken-categorie">Categorie</label>
          <select id="taken-categorie" value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">Alle categorieën</option>
            {(categories.data ?? []).map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      <section className="card table-wrapper" aria-label="Taken">
        {tasks.length === 0 ? (
          <p className="muted">
            {y.tasks.length === 0 ? 'Nog geen taken voor dit jaar.' : 'Geen taken met dit filter.'}
          </p>
        ) : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Takentabel">
            <table className="table tasks-table">
              <thead>
                <tr>
                  <th scope="col">Taak</th>
                  <th scope="col">Personen</th>
                  <th scope="col">Status</th>
                  <th scope="col">Voortgang</th>
                  <th scope="col">Gestart</th>
                  <th scope="col">Verwacht klaar</th>
                </tr>
              </thead>
              {groups.map(([name, rows]) => {
                const all = y.tasks.filter((t) => t.categoryName === name);
                const counted = all.filter((t) => t.effectiveProgress !== null && t.effectiveProgress !== undefined);
                const pct = counted.length
                  ? Math.round(counted.reduce((sum, t) => sum + (t.effectiveProgress ?? 0), 0) / counted.length)
                  : 0;
                return (
                  <tbody key={name}>
                    <tr className="group-row">
                      <th scope="rowgroup" colSpan={3}>
                        {name}{' '}
                        <span className="muted small-text">
                          {all.length} taken · {all.filter((t) => t.status === 'Done').length} afgerond
                        </span>
                      </th>
                      <td colSpan={3}>
                        <MiniBar percent={pct} />
                      </td>
                    </tr>
                    {rows.map((t) => {
                      const [label, tone] = STATUS[t.status];
                      return (
                        <tr key={t.id} className={t.status === 'Cancelled' ? 'cancelled' : undefined}>
                          <td>
                            <button type="button" className="link-button cell-title" onClick={() => setEditing(t)}>
                              {t.title}
                            </button>
                            {t.description ? <div className="muted small-text">{t.description}</div> : null}
                          </td>
                          <td>
                            {[...t.assignees.map((a) => a.name), ...(t.otherPeople ? [t.otherPeople] : [])].join(
                              ', ',
                            ) || <span className="muted">—</span>}
                          </td>
                          <td>
                            <span className={`badge ${tone}`}>{label}</span>
                          </td>
                          <td>
                            {t.status === 'Cancelled' ? (
                              <span className="muted">—</span>
                            ) : (
                              <MiniBar percent={t.effectiveProgress ?? 0} />
                            )}
                          </td>
                          <td>{t.startedOn ? formatDate(t.startedOn) : <span className="muted">—</span>}</td>
                          <td>
                            {t.dueOn ? (
                              <span className={t.overdue ? 'overdue' : undefined}>{formatDate(t.dueOn)}</span>
                            ) : (
                              '—'
                            )}
                            {t.overdue ? <span className="badge error"> over datum</span> : null}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                );
              })}
            </table>
          </div>
        )}
      </section>

      {editing ? (
        <TaskDialog
          yearId={y.yearId}
          task={editing === 'new' ? null : editing}
          categories={(categories.data ?? []).filter(
            (c) => c.active || (editing !== 'new' && c.id === editing.categoryId),
          )}
          onClose={(text) => {
            setEditing(null);
            if (text) setMessage(text);
          }}
        />
      ) : null}
      {copying ? (
        <CopyDialog
          year={y}
          onClose={(text) => {
            setCopying(false);
            if (text) setMessage(text);
          }}
        />
      ) : null}
      {managing ? <CategoriesDialog categories={categories.data ?? []} onClose={() => setManaging(false)} /> : null}
    </>
  );
}

function TaskDialog({
  yearId,
  task,
  categories,
  onClose,
}: {
  yearId: number;
  task: TaskView | null;
  categories: Category[];
  onClose: (message?: string) => void;
}) {
  const tasksApi = useTasks();
  const [form, setForm] = useState({
    title: task?.title ?? '',
    description: task?.description ?? '',
    categoryId: task?.categoryId ?? categories[0]?.id ?? '',
    dueOn: task?.dueOn ?? '',
    status: (task?.status ?? 'NotStarted') as TaskStatus,
    progress: task?.progress ?? 0,
    people: task?.assignees ?? ([] as Person[]),
    otherPeople: task?.otherPeople ?? '',
  });
  const [search, setSearch] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);
  const found = useQuery({
    queryKey: [...tasksApi.key, 'members', search],
    enabled: search.trim().length >= 2,
    queryFn: () => tasksApi.members(search.trim()),
  });
  const body = () => ({
    categoryId: form.categoryId,
    title: form.title,
    description: form.description || null,
    status: form.status,
    progress: form.progress,
    dueOn: form.dueOn || null,
    memberIds: form.people.map((p) => p.memberId),
    otherPeople: form.otherPeople || null,
  });
  const save = useApiMutation(
    () => (task ? tasksApi.update(task.id, body()) : tasksApi.create(yearId, body())),
    [tasksApi.key],
  );
  const remove = useApiMutation(() => tasksApi.remove(task!.id), [tasksApi.key]);
  const set = (change: Partial<typeof form>) => setForm({ ...form, ...change });

  return (
    <Dialog open title={task ? 'Taak bewerken' : 'Taak toevoegen'} onClose={() => onClose()}>
      <form
        className="task-form"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate(undefined, { onSuccess: () => onClose(task ? 'Taak opgeslagen.' : 'Taak toegevoegd.') });
        }}
      >
        <Field
          label="Taak"
          required
          maxLength={200}
          value={form.title}
          onChange={(e) => set({ title: e.target.value })}
        />
        <div className="field">
          <label htmlFor="taak-omschrijving">Omschrijving (niet verplicht)</label>
          <textarea
            id="taak-omschrijving"
            rows={2}
            maxLength={2000}
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
          />
        </div>
        <div className="form-grid">
          <div className="field">
            <label htmlFor="taak-categorie">Categorie</label>
            <select
              id="taak-categorie"
              required
              value={form.categoryId}
              onChange={(e) => set({ categoryId: e.target.value })}
            >
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <Field
            label="Verwacht klaar"
            type="date"
            value={form.dueOn}
            onChange={(e) => set({ dueOn: e.target.value })}
          />
        </div>
        <div className="field">
          <label htmlFor="taak-personen">Personen (leden)</label>
          {form.people.length ? (
            <div className="chips">
              {form.people.map((p) => (
                <button
                  key={p.memberId}
                  type="button"
                  className="chip active"
                  aria-label={`${p.name} verwijderen`}
                  onClick={() => set({ people: form.people.filter((x) => x.memberId !== p.memberId) })}
                >
                  {p.name} ×
                </button>
              ))}
            </div>
          ) : null}
          <input
            id="taak-personen"
            placeholder="Lid zoeken op naam"
            autoComplete="off"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {found.data?.length ? (
            <ul className="pick-list">
              {found.data
                .filter((m) => !form.people.some((p) => p.memberId === m.memberId))
                .map((m) => (
                  <li key={m.memberId}>
                    <button
                      type="button"
                      className="link-button"
                      onClick={() => {
                        set({ people: [...form.people, m] });
                        setSearch('');
                      }}
                    >
                      + {m.name}
                    </button>
                  </li>
                ))}
            </ul>
          ) : null}
        </div>
        <Field
          label="Overige personen (geen lid of een groep)"
          hint="Bijvoorbeeld: Raad van Elf, Hart van Loil"
          maxLength={300}
          value={form.otherPeople}
          onChange={(e) => set({ otherPeople: e.target.value })}
        />
        <fieldset className="task-status">
          <legend>Status</legend>
          <div className="segments-portal" role="radiogroup" aria-label="Status">
            {(Object.keys(STATUS) as TaskStatus[]).map((key) => (
              <label key={key} className={form.status === key ? 'active' : undefined}>
                <input
                  type="radio"
                  name="taak-status"
                  value={key}
                  checked={form.status === key}
                  onChange={() =>
                    set({ status: key, progress: key === 'Done' ? 100 : key === 'NotStarted' ? 0 : form.progress })
                  }
                />
                {STATUS[key][0]}
              </label>
            ))}
          </div>
        </fieldset>
        {form.status === 'InProgress' ? (
          <div className="field">
            <label htmlFor="taak-voortgang">Hoe ver ben je? {form.progress}%</label>
            <input
              id="taak-voortgang"
              type="range"
              min={0}
              max={100}
              step={5}
              value={form.progress}
              onChange={(e) => set({ progress: Number(e.target.value) })}
            />
            <div className="chips">
              {[0, 25, 50, 75, 100].map((p) => (
                <button
                  key={p}
                  type="button"
                  className={`chip${form.progress === p ? ' active' : ''}`}
                  onClick={() => set({ progress: p })}
                >
                  {p}%
                </button>
              ))}
            </div>
          </div>
        ) : null}
        {task?.startedOn ? (
          <p className="muted small-text">
            Gestart op {formatDate(task.startedOn)}
            {task.completedOn ? ` · afgerond op ${formatDate(task.completedOn)}` : ''}
          </p>
        ) : (
          <p className="muted small-text">De startdatum wordt vastgelegd zodra de taak op &quot;Bezig&quot; gaat.</p>
        )}
        <ProblemAlert error={save.error ?? remove.error} />
        <div className="actions">
          {task ? (
            confirmDelete ? (
              <button
                type="button"
                className="button danger"
                disabled={remove.isPending}
                onClick={() => remove.mutate(undefined, { onSuccess: () => onClose('Taak verwijderd.') })}
              >
                Echt verwijderen
              </button>
            ) : (
              <button type="button" className="button secondary danger-outline" onClick={() => setConfirmDelete(true)}>
                Verwijderen
              </button>
            )
          ) : null}
          <span className="grow" />
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

function CopyDialog({ year, onClose }: { year: YearView; onClose: (message?: string) => void }) {
  const tasksApi = useTasks();
  const [selected, setSelected] = useState<string[]>(() =>
    year.tasks.filter((t) => t.status !== 'Cancelled').map((t) => t.id),
  );
  const copy = useApiMutation(() => tasksApi.copy(year.yearId, selected), [tasksApi.key]);
  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));
  const groups = groupBy(year.tasks, (t) => t.categoryName);
  const next = year.nextYear;

  return (
    <Dialog open title={next ? `Kopiëren naar ${next.name}` : 'Kopiëren naar volgend jaar'} onClose={() => onClose()}>
      {!next ? (
        <p>Er is nog geen volgend carnavalsjaar. Maak dat eerst aan bij Configuratie → Carnavalsjaren.</p>
      ) : (
        <>
          <p className="muted">
            Vink aan welke taken volgend jaar terugkomen. Ze beginnen op &quot;nog niet gestart&quot; met dezelfde
            personen; &quot;verwacht klaar&quot; schuift een jaar op.
          </p>
          <div className="actions">
            <button type="button" className="link-button" onClick={() => setSelected(year.tasks.map((t) => t.id))}>
              Alles aanvinken
            </button>
            <button type="button" className="link-button" onClick={() => setSelected([])}>
              Alles uitvinken
            </button>
            <button
              type="button"
              className="link-button"
              onClick={() =>
                setSelected((s) => s.filter((id) => year.tasks.find((t) => t.id === id)?.status !== 'Cancelled'))
              }
            >
              Geannuleerde uitvinken
            </button>
          </div>
          <div className="copy-list">
            {groups.map(([name, rows]) => (
              <fieldset key={name}>
                <legend>
                  {name} ({rows.filter((t) => selected.includes(t.id)).length} van {rows.length})
                </legend>
                {rows.map((t) => (
                  <div key={t.id} className="copy-row">
                    <Checkbox label={t.title} checked={selected.includes(t.id)} onChange={() => toggle(t.id)} />
                    <span className={`badge ${STATUS[t.status][1]}`}>
                      {STATUS[t.status][0]}
                      {t.status === 'InProgress' ? ` ${t.progress}%` : ''}
                    </span>
                  </div>
                ))}
              </fieldset>
            ))}
          </div>
        </>
      )}
      <ProblemAlert error={copy.error} />
      <div className="actions">
        <span className="muted grow">
          {selected.length} van {year.tasks.length} taken geselecteerd
        </span>
        <button type="button" className="button secondary" onClick={() => onClose()}>
          Annuleren
        </button>
        {next ? (
          <button
            type="button"
            className="button"
            disabled={selected.length === 0 || copy.isPending}
            onClick={() =>
              copy.mutate(undefined, {
                onSuccess: (data) => {
                  const n = (data as { copied?: number } | undefined)?.copied ?? 0;
                  onClose(`${n} taken gekopieerd naar ${next.name}.`);
                },
              })
            }
          >
            {selected.length} taken kopiëren
          </button>
        ) : null}
      </div>
    </Dialog>
  );
}

function CategoriesDialog({ categories, onClose }: { categories: Category[]; onClose: () => void }) {
  const tasksApi = useTasks();
  const [name, setName] = useState('');
  const add = useApiMutation(
    () => tasksApi.addCategory({ name, sortOrder: (categories.at(-1)?.sortOrder ?? 0) + 10, active: true }),
    [tasksApi.key],
  );
  const update = useApiMutation(
    (c: Category) => tasksApi.updateCategory(c.id, { name: c.name, sortOrder: c.sortOrder, active: c.active }),
    [tasksApi.key],
  );
  const remove = useApiMutation((id: string) => tasksApi.removeCategory(id), [tasksApi.key]);

  return (
    <Dialog open title="Categorieën" onClose={onClose}>
      <p className="muted">De categorieën van de takenlijst, bijvoorbeeld Pronkzitting, Optocht of Drammerskrant.</p>
      <ul className="list">
        {categories.map((c) => (
          <li key={c.id} className="list-row">
            <span className="grow">
              {c.name} <span className="muted small-text">({c.taskCount} taken)</span>
            </span>
            <Checkbox
              label="Actief"
              checked={c.active}
              onChange={(e) => update.mutate({ ...c, active: e.target.checked })}
            />
            {c.taskCount === 0 ? (
              <button type="button" className="link-button" onClick={() => remove.mutate(c.id)}>
                Verwijderen
              </button>
            ) : null}
          </li>
        ))}
      </ul>
      <form
        className="actions"
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate(undefined, { onSuccess: () => setName('') });
        }}
      >
        <Field label="Nieuwe categorie" maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
        <button type="submit" className="button" disabled={!name.trim() || add.isPending}>
          Toevoegen
        </button>
      </form>
      <ProblemAlert error={add.error ?? update.error ?? remove.error} />
      <div className="actions">
        <button type="button" className="button secondary" onClick={onClose}>
          Sluiten
        </button>
      </div>
    </Dialog>
  );
}

/** Rapportage takenlijst: kerncijfers, per categorie en per persoon met korte balkjes. */
export function TaskReportPage() {
  const tasksApi = useTaskApi('carnival');
  return (
    <TaskApiContext.Provider value={tasksApi}>
      <TaskReport festival={false} />
    </TaskApiContext.Provider>
  );
}

function TaskReport({ festival }: { festival: boolean }) {
  const tasksApi = useTasks();
  const report = useQuery({ queryKey: [...tasksApi.key, 'report'], queryFn: () => tasksApi.report() });
  const r = report.data;
  if (!r) return report.error ? <ProblemAlert error={report.error} /> : <p>Laden…</p>;
  const s = r.summary;
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          {festival ? (
            <h2>Rapportage takenlijst festival {r.yearName}</h2>
          ) : (
            <h1>Rapportage takenlijst {r.yearName}</h1>
          )}
          <p className="page-subtitle">Hoe ver staat de voorbereiding per categorie en per persoon</p>
        </div>
        {festival ? null : (
          <Link to="/taken" className="button secondary">
            Naar de takenlijst
          </Link>
        )}
      </div>
      <div className="kpis">
        <div className="kpi">
          <span className="kpi-label">Totaal</span>
          <span className="kpi-value">{s.percent}%</span>
          <span className="kpi-hint">gemiddelde van {s.total - s.cancelled} taken</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Afgerond</span>
          <span className="kpi-value">{s.done}</span>
          <span className="kpi-hint">van {s.total - s.cancelled}</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Bezig</span>
          <span className="kpi-value">{s.inProgress}</span>
          <span className="kpi-hint">nog niet gestart: {s.notStarted}</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Over datum</span>
          <span className="kpi-value">{s.overdue}</span>
          <span className="kpi-hint">verwacht klaar verstreken</span>
        </div>
      </div>
      <ReportTable title="Per categorie" header="Categorie" rows={r.perCategory} />
      <ReportTable title="Per persoon" header="Persoon" rows={r.perPerson} />
    </>
  );
}

function ReportTable({ title, header, rows }: { title: string; header: string; rows: ReportRow[] }) {
  return (
    <section className="card table-wrapper" aria-label={title}>
      <h2>{title}</h2>
      {rows.length === 0 ? (
        <p className="muted">Nog geen taken.</p>
      ) : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label={`${title} (tabel)`}>
          <table className="table">
            <thead>
              <tr>
                <th scope="col">{header}</th>
                <th scope="col">Taken</th>
                <th scope="col">Afgerond</th>
                <th scope="col">Bezig</th>
                <th scope="col">Niet gestart</th>
                <th scope="col">Voortgang</th>
                <th scope="col">Over datum</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.name}>
                  <th scope="row">{row.name}</th>
                  <td>{row.total - row.cancelled}</td>
                  <td>{row.done}</td>
                  <td>{row.inProgress}</td>
                  <td>{row.notStarted}</td>
                  <td>
                    <MiniBar percent={row.percent} width={140} />
                  </td>
                  <td>
                    {row.overdue ? (
                      <span className="badge error">{row.overdue}</span>
                    ) : (
                      <span className="muted">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function groupBy<T>(items: T[], key: (item: T) => string): [string, T[]][] {
  const map = new Map<string, T[]>();
  for (const item of items) map.set(key(item), [...(map.get(key(item)) ?? []), item]);
  return [...map.entries()];
}
