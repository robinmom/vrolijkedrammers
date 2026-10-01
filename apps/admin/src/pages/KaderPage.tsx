import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { useCommittees, WEBSITE_KEYS, type Committee, type CommitteeMember, type CommitteeMemberRequest } from '../api/website';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

/** Website → Kader: per commissie de kaderleden met functie en pasfoto; meestal gekozen uit de ledenlijst. */
export function KaderPage() {
  const api = useApi();
  const committees = useCommittees();
  const [active, setActive] = useState<number | null>(null);
  const [editing, setEditing] = useState<CommitteeMember | 'nieuw' | null>(null);
  const [removing, setRemoving] = useState<CommitteeMember | null>(null);
  const [managing, setManaging] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const list = committees.data ?? [];
  const committee = list.find((c) => c.id === active) ?? list[0];

  const reorder = useApiMutation(
    async ({ id, ids }: { id: number; ids: string[] }) =>
      api.PUT('/api/v1/admin/website/committees/{id}/order', { params: { path: { id } }, body: { ids } }),
    WEBSITE_KEYS,
  );
  const remove = useApiMutation(
    async (id: string) => api.DELETE('/api/v1/admin/website/kader/{id}', { params: { path: { id } } }),
    WEBSITE_KEYS,
  );

  function move(index: number, delta: number) {
    if (!committee) return;
    const ids = committee.members.map((m) => m.id);
    const [item] = ids.splice(index, 1);
    ids.splice(index + delta, 0, item!);
    reorder.mutate({ id: committee.id, ids }, { onSuccess: () => setMessage('Volgorde opgeslagen.') });
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Kader</h1>
          <p className="muted">Wie staat er in het bestuur en de commissies? Dit staat op de website bij Vereniging → Kader.</p>
        </div>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setManaging(true)}>
            Commissies beheren
          </button>
          <button type="button" className="button" onClick={() => setEditing('nieuw')} disabled={!committee}>
            Kaderlid toevoegen
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={committees.error ?? reorder.error ?? remove.error} />
      <div className="tabs" role="tablist" aria-label="Commissies">
        {list.map((c) => (
          <button
            key={c.id}
            type="button"
            role="tab"
            className="tab"
            aria-selected={c.id === committee?.id}
            onClick={() => setActive(c.id)}
          >
            {c.name} <span className="muted">{c.members.length}</span>
          </button>
        ))}
      </div>
      {committee ? (
        <div className="table-scroll table-wrapper" tabIndex={0} role="tabpanel" aria-label={committee.name}>
          <table className="table">
            <caption className="visually-hidden">Kaderleden {committee.name}</caption>
            <thead>
              <tr>
                <th scope="col">Volgorde</th>
                <th scope="col">Foto</th>
                <th scope="col">Naam</th>
                <th scope="col">Functie</th>
                <th scope="col">Ledenlijst</th>
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {committee.members.map((m, index) => (
                <tr key={m.id}>
                  <td>
                    <div className="order-buttons">
                      <button type="button" className="button ghost small" disabled={index === 0 || reorder.isPending} onClick={() => move(index, -1)}>
                        ↑ <span className="visually-hidden">{m.name} omhoog</span>
                      </button>
                      <button
                        type="button"
                        className="button ghost small"
                        disabled={index === committee.members.length - 1 || reorder.isPending}
                        onClick={() => move(index, 1)}
                      >
                        ↓ <span className="visually-hidden">{m.name} omlaag</span>
                      </button>
                    </div>
                  </td>
                  <td>{m.photoUrl ? <img src={m.photoUrl} alt="" className="thumb thumb-round" /> : <span className="thumb thumb-round thumb-empty" />}</td>
                  <td>
                    <strong>{m.name}</strong>
                  </td>
                  <td>{m.function}</td>
                  <td>{m.memberNumber ? <span className="badge ok">Lid {m.memberNumber}</span> : <span className="badge">Geen lid gekoppeld</span>}</td>
                  <td className="actions-cell">
                    <button type="button" className="button ghost small" onClick={() => setEditing(m)}>
                      Bewerken <span className="visually-hidden">{m.name}</span>
                    </button>
                    <button type="button" className="button ghost small" onClick={() => setRemoving(m)}>
                      Weghalen <span className="visually-hidden">{m.name}</span>
                    </button>
                  </td>
                </tr>
              ))}
              {committee.members.length === 0 ? (
                <tr>
                  <td colSpan={6} className="muted">
                    Nog geen kaderleden in {committee.name}.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      ) : null}
      {editing && committee ? (
        <KaderDialog
          member={editing === 'nieuw' ? null : editing}
          committees={list}
          committeeId={editing === 'nieuw' ? committee.id : editing.committeeId}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removing !== null}
        title="Kaderlid weghalen?"
        message={`${removing?.name ?? ''} verdwijnt uit het kader op de website. Het lid blijft gewoon in de ledenlijst.`}
        confirmLabel="Weghalen"
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() =>
          removing &&
          remove.mutate(removing.id, {
            onSuccess: () => {
              setRemoving(null);
              setMessage('Kaderlid weggehaald.');
            },
          })
        }
      />
      {managing ? <CommitteesDialog committees={list} onClose={() => setManaging(false)} /> : null}
    </>
  );
}

function KaderDialog({
  member,
  committees,
  committeeId,
  onClose,
}: {
  member: CommitteeMember | null;
  committees: Committee[];
  committeeId: number;
  onClose: (saved?: string) => void;
}) {
  const api = useApi();
  const [form, setForm] = useState<CommitteeMemberRequest>({
    committeeId,
    memberId: member?.memberId ?? null,
    name: member?.name ?? '',
    function: member?.function ?? null,
    photo: null,
  });
  const [search, setSearch] = useState('');
  const [term, setTerm] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setTerm(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);
  const candidates = useQuery({
    queryKey: ['website', 'member-search', term],
    enabled: term.length >= 2,
    queryFn: async () => (await api.GET('/api/v1/admin/website/member-search', { params: { query: { q: term } } })).data ?? [],
  });
  const save = useApiMutation(
    async (body: CommitteeMemberRequest) =>
      member
        ? api.PUT('/api/v1/admin/website/kader/{id}', { params: { path: { id: member.id } }, body })
        : api.POST('/api/v1/admin/website/kader', { body }),
    WEBSITE_KEYS,
  );
  const set = (change: Partial<CommitteeMemberRequest>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, { onSuccess: () => onClose(member ? 'Kaderlid opgeslagen.' : 'Kaderlid toegevoegd.') });
  }

  return (
    <Dialog open title={member ? `${member.name} bewerken` : 'Kaderlid toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <p className="muted">Kies iemand uit de ledenlijst. Functie en pasfoto staan niet in e-Boekhouden en worden alleen hier bewaard.</p>
        <Field label="Zoek in de ledenlijst" hint="Naam of lidnummer, minstens 2 tekens." value={search} onChange={(e) => setSearch(e.target.value)} />
        {candidates.data && candidates.data.length > 0 ? (
          <ul className="pick-list" aria-label="Gevonden leden">
            {candidates.data.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  className={form.memberId === c.id ? 'pick selected' : 'pick'}
                  aria-pressed={form.memberId === c.id}
                  onClick={() => set({ memberId: c.id, name: c.fullName })}
                >
                  <strong>{c.fullName}</strong>
                  <span className="muted">
                    Lidnummer {c.memberNumber}
                    {c.city ? ` · ${c.city}` : ''}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        ) : term.length >= 2 && candidates.isSuccess ? (
          <p className="muted">Geen leden gevonden.</p>
        ) : null}
        <Field
          label="Naam op de website"
          required
          maxLength={150}
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          hint={form.memberId ? 'Gekoppeld aan de ledenlijst.' : 'Niet gekoppeld aan een lid.'}
        />
        {form.memberId ? (
          <button type="button" className="button ghost small" onClick={() => set({ memberId: null })}>
            Koppeling met de ledenlijst weghalen
          </button>
        ) : null}
        <div className="field">
          <label htmlFor="kader-commissie">Commissie</label>
          <select id="kader-commissie" value={form.committeeId} onChange={(e) => set({ committeeId: Number(e.target.value) })}>
            {committees.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
        <Field label="Functie (op de website)" maxLength={100} value={form.function ?? ''} onChange={(e) => set({ function: e.target.value || null })} />
        <ImagePicker label="Pasfoto" round uploadPath="/api/v1/admin/website/images" currentUrl={member?.photoUrl} onChange={(photo) => set({ photo })} />
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => onClose()}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            {member ? 'Opslaan' : 'Toevoegen'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function CommitteesDialog({ committees, onClose }: { committees: Committee[]; onClose: () => void }) {
  const api = useApi();
  const [name, setName] = useState('');
  const create = useApiMutation(
    async (n: string) => api.POST('/api/v1/admin/website/committees', { body: { name: n, sortOrder: (committees.at(-1)?.sortOrder ?? 0) + 10 } }),
    WEBSITE_KEYS,
  );
  const rename = useApiMutation(
    async (c: { id: number; name: string; sortOrder: number }) =>
      api.PUT('/api/v1/admin/website/committees/{id}', { params: { path: { id: c.id } }, body: { name: c.name, sortOrder: c.sortOrder } }),
    WEBSITE_KEYS,
  );
  const remove = useApiMutation(
    async (id: number) => api.DELETE('/api/v1/admin/website/committees/{id}', { params: { path: { id } } }),
    WEBSITE_KEYS,
  );
  return (
    <Dialog open title="Commissies beheren" onClose={onClose}>
      <ul className="plain-list">
        {committees.map((c) => (
          <li key={c.id} className="row-edit">
            <Field
              label={`Naam commissie ${c.name}`}
              defaultValue={c.name}
              onBlur={(e) => e.target.value.trim() && e.target.value !== c.name && rename.mutate({ id: c.id, name: e.target.value, sortOrder: c.sortOrder })}
            />
            <button type="button" className="button ghost small" disabled={c.members.length > 0} onClick={() => remove.mutate(c.id)}>
              Verwijderen <span className="visually-hidden">{c.name}</span>
            </button>
          </li>
        ))}
      </ul>
      <p className="muted">Een commissie met kaderleden kun je niet verwijderen.</p>
      <form
        className="row-edit"
        onSubmit={(e) => {
          e.preventDefault();
          if (name.trim()) create.mutate(name.trim(), { onSuccess: () => setName('') });
        }}
      >
        <Field label="Nieuwe commissie" value={name} onChange={(e) => setName(e.target.value)} />
        <button type="submit" className="button secondary" disabled={create.isPending}>
          Toevoegen
        </button>
      </form>
      <ProblemAlert error={create.error ?? rename.error ?? remove.error} />
      <div className="actions">
        <button type="button" className="button" onClick={onClose}>
          Klaar
        </button>
      </div>
    </Dialog>
  );
}
