import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { BOARD_KEYS, useApiMutation, useBoardBodies, useMe, type BoardBody } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

/**
 * Kader → Categorieën (2026-11-18): bestuur, raad van elf en wat de vereniging er zelf bij wil. Per categorie staat of
 * er periodes van drie jaar bij horen; dat is het verschil tussen een verkiesbaar bestuur en een raad van elf die
 * begint en stopt wanneer iemand dat zelf wil.
 */
export function BoardBodiesPage() {
  const me = useMe();
  const bodies = useBoardBodies();
  const [editing, setEditing] = useState<BoardBody | 'nieuw' | null>(null);
  const [removing, setRemoving] = useState<BoardBody | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  const api = useApi();
  const list = bodies.data ?? [];
  const remove = useApiMutation(
    async (id: string) => api.DELETE('/api/v1/admin/board/bodies/{id}', { params: { path: { id } } }),
    BOARD_KEYS,
  );

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Categorieën in het kader</h1>
          <p className="muted">
            De volgorde hieronder is ook de volgorde van de knoppen bij Kader → Personen. Een categorie met periodes
            krijgt periodes van drie jaar, verlengbaar met één of twee jaar.
          </p>
        </div>
        {canEdit ? (
          <button type="button" className="button" onClick={() => setEditing('nieuw')}>
            Categorie toevoegen
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={bodies.error ?? remove.error} />

      <div className="table-wrapper">
        <table className="table">
          <caption className="visually-hidden">Categorieën</caption>
          <thead>
            <tr>
              <th scope="col">Categorie</th>
              <th scope="col">Periodes</th>
              <th scope="col">Volgorde</th>
              <th scope="col">Mensen</th>
              {canEdit ? (
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              ) : null}
            </tr>
          </thead>
          <tbody>
            {list.map((b) => (
              <tr key={b.id}>
                <td>
                  <strong>{b.name}</strong>
                </td>
                <td>{b.hasTerms ? 'elke drie jaar' : <span className="muted">geen</span>}</td>
                <td>{b.position}</td>
                <td>{b.seats}</td>
                {canEdit ? (
                  <td className="actions-cell">
                    <button type="button" className="button ghost small" onClick={() => setEditing(b)}>
                      Bewerken <span className="visually-hidden">{b.name}</span>
                    </button>
                    <button type="button" className="button ghost small" disabled={b.seats > 0} onClick={() => setRemoving(b)}>
                      Verwijderen <span className="visually-hidden">{b.name}</span>
                    </button>
                  </td>
                ) : null}
              </tr>
            ))}
            {list.length === 0 ? (
              <tr>
                <td colSpan={canEdit ? 5 : 4} className="muted">
                  Nog geen categorieën.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>

      {editing ? (
        <BodyDialog
          body={editing === 'nieuw' ? null : editing}
          nextPosition={Math.max(0, ...list.map((b) => b.position)) + 1}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setMessage(saved);
          }}
        />
      ) : null}
      <ConfirmDialog
        open={removing !== null}
        title="Categorie verwijderen?"
        message={`${removing?.name ?? ''} verdwijnt uit het kader. Dat kan alleen zolang er niemand in staat.`}
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

function BodyDialog({ body, nextPosition, onClose }: { body: BoardBody | null; nextPosition: number; onClose: (saved?: string) => void }) {
  const api = useApi();
  const [form, setForm] = useState({
    name: body?.name ?? '',
    hasTerms: body?.hasTerms ?? false,
    position: body?.position ?? nextPosition,
  });
  const save = useApiMutation(
    async () =>
      body
        ? (await api.PUT('/api/v1/admin/board/bodies/{id}', { params: { path: { id: body.id } }, body: form })).data
        : (await api.POST('/api/v1/admin/board/bodies', { body: form })).data,
    BOARD_KEYS,
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(undefined, { onSuccess: () => onClose('Opgeslagen.') });
  }

  return (
    <Dialog open title={body ? `${body.name} bijwerken` : 'Categorie toevoegen'} onClose={() => onClose()}>
      <form onSubmit={submit}>
        <Field
          label="Naam"
          required
          maxLength={60}
          hint="Bijvoorbeeld Bestuur, Raad van Elf, Commissie of Jeugdraad."
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
        />
        <Checkbox
          label="Periodes van drie jaar, verlengbaar met één of twee jaar"
          checked={form.hasTerms}
          onChange={(e) => setForm({ ...form, hasTerms: e.target.checked })}
        />
        {body?.hasTerms && !form.hasTerms ? (
          <p className="muted">
            Let op: de periodes van de {body.seats} {body.seats === 1 ? 'persoon' : 'mensen'} in deze categorie worden
            weggehaald. Begin- en einddatum blijven staan.
          </p>
        ) : null}
        <Field
          label="Volgorde"
          type="number"
          min={0}
          max={999}
          hint="Lager staat eerder in de rij knoppen."
          value={String(form.position)}
          onChange={(e) => setForm({ ...form, position: Number(e.target.value) })}
        />
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
