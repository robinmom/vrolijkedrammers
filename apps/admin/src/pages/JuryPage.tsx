import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, type Schemas } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

type Juror = Schemas['JurorResponse'];
type JudgingCategory = Schemas['JudgingCategoryResponse'];
type Weights = Schemas['JudgingWeightsRequest'];
type Outside = Schemas['OutsideScoreResponse'];
type Decision = NonNullable<Outside['decision']>;

const criteria: { key: keyof Omit<Weights, 'judged'>; label: string; short: string }[] = [
  { key: 'originality', label: 'Originaliteit', short: 'Org.' },
  { key: 'carnivalesque', label: 'Carnavalesk', short: 'Carn.' },
  { key: 'quality', label: 'Kwaliteit', short: 'Kwal.' },
  { key: 'overall', label: 'Algemene indruk', short: 'Alg.' },
];

/**
 * Jury van de optocht (fase 22a). Bestuur en hoofdjury delen juryleden per optocht in categorieën in; alleen het bestuur
 * nodigt juryleden uit (zonder categorie, die verschilt per optocht), wijst de hoofdjury aan en stelt de weging in.
 */
export function JuryPage() {
  const api = useApi();
  const me = useMe();
  const canManage = (me.data?.permissions ?? []).includes('jury.manage');
  const jury = useQuery({ queryKey: ['jury'], queryFn: async () => (await api.GET('/api/v1/admin/jury')).data });
  const [inviting, setInviting] = useState<{ name: string; email: string } | null>(null);
  const [editing, setEditing] = useState<{ juror: Juror; categoryIds: number[]; headJury: boolean } | null>(null);
  const [weighing, setWeighing] = useState<{ category: JudgingCategory; form: Weights } | null>(null);
  const [removing, setRemoving] = useState<Juror | null>(null);
  const [reviewing, setReviewing] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const data = jury.data;
  const paradeId = data?.paradeId ?? '';
  const invite = useApiMutation(
    (body: { name: string; email: string }) => api.POST('/api/v1/admin/jury/jurors', { body }),
    [['jury']],
  );
  const save = useApiMutation(
    async (v: { juror: Juror; categoryIds: number[]; headJury: boolean }) => {
      await api.PUT('/api/v1/admin/jury/parades/{paradeId}/jurors/{userId}/categories', {
        params: { path: { paradeId, userId: v.juror.userId } },
        body: { categoryIds: v.categoryIds },
      });
      if (canManage && v.headJury !== v.juror.headJury) {
        await api.PUT('/api/v1/admin/jury/jurors/{userId}/head-jury', {
          params: { path: { userId: v.juror.userId } },
          body: { headJury: v.headJury },
        });
      }
    },
    [['jury']],
  );
  const resend = useApiMutation(
    (userId: string) => api.POST('/api/v1/admin/jury/jurors/{userId}/resend-invite', { params: { path: { userId } } }),
    [],
  );
  const remove = useApiMutation(
    (userId: string) => api.DELETE('/api/v1/admin/jury/jurors/{userId}', { params: { path: { userId } } }),
    [['jury']],
  );
  const weigh = useApiMutation(
    (v: { categoryId: number; form: Weights }) =>
      api.PUT('/api/v1/admin/jury/parades/{paradeId}/categories/{categoryId}', {
        params: { path: { paradeId, categoryId: v.categoryId } },
        body: v.form,
      }),
    [['jury']],
  );

  // Fase 22b: beoordelingen buiten categorie (alleen hoeveel passages, nooit de scores).
  const decide = useApiMutation(
    (decisions: { userId: string; registrationId: string; decision: Decision | null }[]) =>
      api.PUT('/api/v1/admin/jury/parades/{paradeId}/outside', { params: { path: { paradeId } }, body: { decisions } }),
    [['jury']],
  );
  const outside = data?.outside ?? [];
  const pendingByJuror = [...new Set(outside.filter((o) => !o.decision).map((o) => o.userId))].map((userId) => {
    const items = outside.filter((o) => o.userId === userId);
    return { userId, name: items[0]!.jurorName, pending: items.filter((o) => !o.decision) };
  });
  const decideAll = (items: Outside[], decision: Decision) =>
    decide.mutate(items.map((o) => ({ userId: o.userId, registrationId: o.registrationId, decision })));

  const categoryName = (id: number) => data?.categories.find((c) => c.categoryId === id)?.name ?? `Categorie ${id}`;

  function submitInvite(event: FormEvent) {
    event.preventDefault();
    if (!inviting) return;
    invite.mutate(inviting, {
      onSuccess: () => {
        setMessage(`${inviting.name} is uitgenodigd. Wijs met Aanpassen de categorieën toe.`);
        setInviting(null);
      },
    });
  }

  function submitEdit(event: FormEvent) {
    event.preventDefault();
    if (!editing) return;
    save.mutate(editing, {
      onSuccess: () => {
        setMessage(`${editing.juror.name} is opgeslagen.`);
        setEditing(null);
      },
    });
  }

  function submitWeights(event: FormEvent) {
    event.preventDefault();
    if (!weighing) return;
    weigh.mutate(
      { categoryId: weighing.category.categoryId, form: weighing.form },
      {
        onSuccess: () => {
          setMessage(`Weging van ${weighing.category.name} is opgeslagen.`);
          setWeighing(null);
        },
      },
    );
  }

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Jury</h1>
          <p className="page-subtitle">
            {data ? `${data.paradeName} · ${formatDate(data.paradeDate)} · ` : ''}
            elke wagen of groep wordt 3x beoordeeld op 4 criteria (0–100).
          </p>
        </div>
        {canManage ? (
          <button type="button" className="button" onClick={() => setInviting({ name: '', email: '' })}>
            Jurylid uitnodigen
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={jury.error ?? resend.error ?? remove.error ?? decide.error} />
      {pendingByJuror.map((j) => (
        <div key={j.userId} className="alert alert-warning outside-alert">
          <p>
            {j.name} heeft {j.pending.length === 1 ? '1 inzending' : `${j.pending.length} inzendingen`} gejureerd buiten
            de eigen categorieën ({[...new Set(j.pending.map((o) => o.categoryName))].join(', ')}).
          </p>
          <span className="actions">
            <button type="button" className="button secondary small" onClick={() => setReviewing(j.userId)}>
              Aanpassen <span className="visually-hidden">beoordelingen buiten categorie van {j.name}</span>
            </button>
            <button
              type="button"
              className="button ghost small"
              disabled={decide.isPending}
              onClick={() => decideAll(j.pending, 'Rejected')}
            >
              Afwijzen <span className="visually-hidden">alles van {j.name}</span>
            </button>
            <button
              type="button"
              className="button small"
              disabled={decide.isPending}
              onClick={() => decideAll(j.pending, 'Approved')}
            >
              Akkoord <span className="visually-hidden">alles van {j.name}</span>
            </button>
          </span>
        </div>
      ))}

      {data ? (
        <>
          <section className="card" aria-labelledby="juryleden-titel">
            <h2 id="juryleden-titel">Juryleden</h2>
            {data.jurors.length === 0 ? (
              <p className="muted">Nog geen juryleden. Nodig ze uit met de knop rechtsboven.</p>
            ) : null}
            {data.jurors.length > 0 ? (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Juryleden">
                <table className="table">
                  <caption className="visually-hidden">Juryleden en hun categorieën in deze optocht</caption>
                  <thead>
                    <tr>
                      <th scope="col">Naam</th>
                      <th scope="col">E-mail</th>
                      <th scope="col">Rol</th>
                      <th scope="col">Categorieën</th>
                      <th scope="col">Account</th>
                      <th scope="col">Jurering</th>
                      <th scope="col">
                        <span className="visually-hidden">Acties</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.jurors.map((j) => (
                      <tr key={j.userId}>
                        <td>{j.name}</td>
                        <td>{j.email}</td>
                        <td>{j.headJury ? <span className="badge info">Hoofdjury</span> : 'Jurylid'}</td>
                        <td>
                          {j.categoryIds.length === 0 ? (
                            <span className="muted">Nog geen categorie</span>
                          ) : (
                            <span className="chip-list">
                              {j.categoryIds.map((id) => (
                                <span key={id} className="badge">
                                  {categoryName(id)}
                                </span>
                              ))}
                            </span>
                          )}
                        </td>
                        <td>
                          {j.invited ? (
                            <span className="badge warn">Uitgenodigd</span>
                          ) : (
                            <span className="badge ok">Actief</span>
                          )}
                        </td>
                        <td>
                          {j.submittedAt ? (
                            <span className="badge ok">Ingediend</span>
                          ) : j.scored > 0 ? (
                            <span className="badge info">
                              Bezig {j.scored}/{j.assigned}
                            </span>
                          ) : (
                            <span className="muted">Nog niet</span>
                          )}
                        </td>
                        <td className="actions">
                          <button
                            type="button"
                            className="button secondary small"
                            onClick={() =>
                              setEditing({ juror: j, categoryIds: [...j.categoryIds], headJury: j.headJury })
                            }
                          >
                            Aanpassen <span className="visually-hidden">{j.name}</span>
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </section>

          <section className="card" aria-labelledby="categorieen-titel">
            <h2 id="categorieen-titel">Categorieën, weging en jury</h2>
            <p className="muted">
              De weging is hoe vaak een criterium meetelt in het totaal. Juryleden zien de weging niet.
            </p>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Categorieën">
              <table className="table">
                <caption className="visually-hidden">Categorieën met weging en aantal juryleden</caption>
                <thead>
                  <tr>
                    <th scope="col">Categorie</th>
                    {criteria.map((c) => (
                      <th key={c.key} scope="col" className="num">
                        <abbr title={c.label}>{c.short}</abbr>
                      </th>
                    ))}
                    <th scope="col" className="num">
                      Juryleden
                    </th>
                    <th scope="col" className="num">
                      Inzendingen
                    </th>
                    {canManage ? (
                      <th scope="col">
                        <span className="visually-hidden">Acties</span>
                      </th>
                    ) : null}
                  </tr>
                </thead>
                <tbody>
                  {data.categories.map((c) => (
                    <tr key={c.categoryId}>
                      <td>{c.name}</td>
                      {c.judged ? (
                        criteria.map((k) => (
                          <td key={k.key} className="num">
                            {c[k.key]}x
                          </td>
                        ))
                      ) : (
                        <td colSpan={4} className="muted">
                          Wordt niet beoordeeld
                        </td>
                      )}
                      <td className="num">{c.jurorCount}</td>
                      <td className="num">{c.entryCount}</td>
                      {canManage ? (
                        <td className="actions">
                          <button
                            type="button"
                            className="button secondary small"
                            onClick={() =>
                              setWeighing({
                                category: c,
                                form: {
                                  judged: c.judged,
                                  originality: c.originality,
                                  carnivalesque: c.carnivalesque,
                                  quality: c.quality,
                                  overall: c.overall,
                                },
                              })
                            }
                          >
                            Weging <span className="visually-hidden">{c.name}</span>
                          </button>
                        </td>
                      ) : null}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : null}

      <Dialog open={inviting !== null} title="Jurylid uitnodigen" onClose={() => setInviting(null)}>
        {inviting ? (
          <form onSubmit={submitInvite}>
            <p className="muted">
              Een jurylid hoeft geen lid te zijn. Hij krijgt een e-mail met uitleg over inloggen in de app. Categorieën
              wijs je daarna per optocht toe met Aanpassen.
            </p>
            <Field
              label="Naam"
              required
              maxLength={100}
              value={inviting.name}
              onChange={(e) => setInviting({ ...inviting, name: e.target.value })}
            />
            <Field
              label="E-mailadres"
              type="email"
              required
              maxLength={254}
              value={inviting.email}
              onChange={(e) => setInviting({ ...inviting, email: e.target.value })}
            />
            <ProblemAlert error={invite.error} />
            <div className="actions">
              <button type="button" className="button secondary" onClick={() => setInviting(null)}>
                Annuleren
              </button>
              <button type="submit" className="button" disabled={invite.isPending}>
                Uitnodiging versturen
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={editing !== null}
        title={editing ? `${editing.juror.name} aanpassen` : ''}
        onClose={() => setEditing(null)}
      >
        {editing && data ? (
          <form onSubmit={submitEdit}>
            <p className="muted">
              {editing.juror.email}
              {editing.juror.invited ? ' · uitgenodigd, nog niet ingelogd' : ''}
            </p>
            <fieldset>
              <legend>Categorieën voor {data.paradeName}</legend>
              {data.categories
                .filter((c) => c.judged)
                .map((c) => (
                  <Checkbox
                    key={c.categoryId}
                    label={c.name}
                    checked={editing.categoryIds.includes(c.categoryId)}
                    onChange={(e) =>
                      setEditing({
                        ...editing,
                        categoryIds: e.target.checked
                          ? [...editing.categoryIds, c.categoryId]
                          : editing.categoryIds.filter((id) => id !== c.categoryId),
                      })
                    }
                  />
                ))}
              <p className="muted">
                Een categorie heeft meerdere juryleden. Een volgende optocht begint met dezelfde indeling.
              </p>
            </fieldset>
            {canManage ? (
              <fieldset>
                <legend>Rol</legend>
                <Checkbox
                  label={
                    <>
                      Hoofdjury
                      <br />
                      <small className="muted">
                        Krijgt toegang tot het beheerportal (alleen Jury), deelt juryleden in en keurt beoordelingen
                        buiten categorie goed. Jureert zelf ook.
                      </small>
                    </>
                  }
                  checked={editing.headJury}
                  onChange={(e) => setEditing({ ...editing, headJury: e.target.checked })}
                />
              </fieldset>
            ) : null}
            <ProblemAlert error={save.error} />
            <div className="actions spread">
              {canManage ? (
                <span className="actions">
                  {editing.juror.invited ? (
                    <button
                      type="button"
                      className="button ghost"
                      disabled={resend.isPending}
                      onClick={() =>
                        resend.mutate(editing.juror.userId, {
                          onSuccess: () => setMessage(`Uitnodiging opnieuw gestuurd naar ${editing.juror.email}.`),
                        })
                      }
                    >
                      Uitnodiging opnieuw sturen
                    </button>
                  ) : null}
                  <button
                    type="button"
                    className="button ghost danger"
                    onClick={() => {
                      setRemoving(editing.juror);
                      setEditing(null);
                    }}
                  >
                    Uit de jury halen
                  </button>
                </span>
              ) : (
                <span />
              )}
              <span className="actions">
                <button type="button" className="button secondary" onClick={() => setEditing(null)}>
                  Annuleren
                </button>
                <button type="submit" className="button" disabled={save.isPending}>
                  Opslaan
                </button>
              </span>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={weighing !== null}
        title={weighing ? `Weging ${weighing.category.name}` : ''}
        onClose={() => setWeighing(null)}
      >
        {weighing ? (
          <form onSubmit={submitWeights}>
            <Checkbox
              label="Deze categorie wordt beoordeeld"
              checked={weighing.form.judged}
              onChange={(e) => setWeighing({ ...weighing, form: { ...weighing.form, judged: e.target.checked } })}
            />
            <div className="form-grid">
              {criteria.map((c) => (
                <div key={c.key} className="field">
                  <label htmlFor={`weging-${c.key}`}>{c.label}</label>
                  <select
                    id={`weging-${c.key}`}
                    value={weighing.form[c.key]}
                    disabled={!weighing.form.judged}
                    onChange={(e) =>
                      setWeighing({ ...weighing, form: { ...weighing.form, [c.key]: Number(e.target.value) } })
                    }
                  >
                    {[0, 1, 2, 3, 4, 5].map((n) => (
                      <option key={n} value={n}>
                        {n === 0 ? 'telt niet mee' : `${n}x`}
                      </option>
                    ))}
                  </select>
                </div>
              ))}
            </div>
            <ProblemAlert error={weigh.error} />
            <div className="actions">
              <button type="button" className="button secondary" onClick={() => setWeighing(null)}>
                Annuleren
              </button>
              <button type="submit" className="button" disabled={weigh.isPending}>
                Opslaan
              </button>
            </div>
          </form>
        ) : null}
      </Dialog>

      <Dialog
        open={reviewing !== null}
        title={`Beoordeling buiten categorie: ${outside.find((o) => o.userId === reviewing)?.jurorName ?? ''}`}
        onClose={() => setReviewing(null)}
      >
        <p className="muted">
          De scores zijn alleen voor het jurylid zelf. Geef per wagen of groep akkoord (telt mee in de uitslag) of wijs
          af (telt niet mee).
        </p>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Beoordelingen buiten categorie">
          <table className="table">
            <caption className="visually-hidden">Beoordelingen buiten categorie</caption>
            <thead>
              <tr>
                <th scope="col">Nr.</th>
                <th scope="col">Groep</th>
                <th scope="col">Categorie</th>
                <th scope="col">Beoordeeld</th>
                <th scope="col">Beslissing</th>
              </tr>
            </thead>
            <tbody>
              {outside
                .filter((o) => o.userId === reviewing)
                .map((o) => (
                  <tr key={o.registrationId}>
                    <td>{o.startNumber ?? '–'}</td>
                    <td>{o.groupName}</td>
                    <td>{o.categoryName}</td>
                    <td>{o.passes === 1 ? '1 passage' : `${o.passes} passages`}</td>
                    <td className="actions">
                      {o.decision === 'Approved' ? <span className="badge ok">Akkoord</span> : null}
                      {o.decision === 'Rejected' ? <span className="badge error">Afgewezen</span> : null}
                      {o.decision !== 'Rejected' ? (
                        <button
                          type="button"
                          className="button ghost small"
                          disabled={decide.isPending}
                          onClick={() => decideAll([o], 'Rejected')}
                        >
                          Afwijzen <span className="visually-hidden">{o.groupName}</span>
                        </button>
                      ) : null}
                      {o.decision !== 'Approved' ? (
                        <button
                          type="button"
                          className="button secondary small"
                          disabled={decide.isPending}
                          onClick={() => decideAll([o], 'Approved')}
                        >
                          Akkoord <span className="visually-hidden">{o.groupName}</span>
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
        <div className="actions">
          <button type="button" className="button" onClick={() => setReviewing(null)}>
            Klaar
          </button>
        </div>
      </Dialog>

      <ConfirmDialog
        open={removing !== null}
        title={`${removing?.name ?? ''} uit de jury halen?`}
        message="Het jurylid kan daarna niet meer jureren en verdwijnt uit alle categorieën. Het account blijft bestaan."
        confirmLabel="Uit de jury halen"
        busy={remove.isPending}
        onCancel={() => setRemoving(null)}
        onConfirm={() =>
          removing &&
          remove.mutate(removing.userId, {
            onSettled: () => setRemoving(null),
            onSuccess: () => setMessage(`${removing.name} is uit de jury gehaald.`),
          })
        }
      />
    </>
  );
}
