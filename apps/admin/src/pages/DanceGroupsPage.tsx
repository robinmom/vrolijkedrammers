import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useDanceGroups, useMe } from '../api/hooks';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

const SHOWN = 6;

/**
 * Dansgarde → Dansgroepen (fase 17): de indeling van de dansgarde in groepen zoals Mini Drammers en Drammerinekes.
 * Een dansgroep is een portalgroep van het type Dansgarde; leden en leiding beheer je op de groepspagina, indelen kan ook
 * snel vanuit het Overzicht.
 */
export function DanceGroupsPage() {
  const api = useApi();
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canEdit = permissions.includes('member.update');
  const canNotify = permissions.includes('notification.send') || permissions.includes('notification.send.group');
  const groups = useDanceGroups();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const create = useApiMutation(
    async () =>
      (
        await api.POST('/api/v1/admin/groups', {
          body: {
            name,
            description: description.trim() || null,
            type: 'DanceGuard',
            carnivalYearId: null,
            active: true,
          },
        })
      ).data,
    [['dance-groups'], ['dansgarde'], ['groups']],
  );

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate(undefined, {
      onSuccess: () => {
        setMessage(`Dansgroep ${name} is aangemaakt.`);
        setName('');
        setDescription('');
      },
    });
  }

  const d = groups.data;
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Dansgroepen</h1>
          <p className="page-subtitle">
            De indeling van de dansgarde. Alleen leden met groep "Dansgarde" in e-Boekhouden kunnen in een dansgroep.
          </p>
        </div>
      </div>
      <ProblemAlert error={groups.error} />
      <SuccessMessage message={message} />
      {d ? (
        <div className="card-grid">
          {d.groups.map((g) => (
            <section key={g.id} className="card" aria-labelledby={`dansgroep-${g.id}`}>
              <div className="card-header">
                <h2 id={`dansgroep-${g.id}`}>{g.name}</h2>
                <span className="badge">{g.members.length} leden</span>
              </div>
              <p className="muted small-text">
                {[
                  g.description,
                  g.leaders.length ? `Leiding: ${g.leaders.join(', ')}` : 'Nog geen leiding',
                  g.active ? null : 'inactief',
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
              <div className="chips">
                {g.members.slice(0, SHOWN).map((m) => (
                  <span key={m.memberId} className="badge info">
                    {m.fullName}
                  </span>
                ))}
                {g.members.length > SHOWN ? <span className="badge">+ {g.members.length - SHOWN}</span> : null}
              </div>
              <div className="toolbar">
                <Link to="/groepen/$id" params={{ id: g.id }} className="button secondary small">
                  Leden en leiding
                </Link>
                {canNotify ? (
                  <Link to="/meldingen/nieuw" search={{ groep: g.id }} className="button ghost small">
                    Melding aan groep
                  </Link>
                ) : null}
              </div>
            </section>
          ))}
          {canEdit ? (
            <section className="card" aria-labelledby="nieuwe-dansgroep">
              <h2 id="nieuwe-dansgroep">Nieuwe dansgroep</h2>
              <p className="muted small-text">
                Geef leiding in de groep de functie Leiding; met de rol Dansgarde-leiding kunnen zij meldingen sturen
                aan hun eigen groep. Die komen ook bij de gekoppelde ouders aan.
              </p>
              <form onSubmit={submit}>
                <Field label="Naam" value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} />
                <Field
                  label="Omschrijving (bijv. leeftijden)"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  maxLength={500}
                />
                <ProblemAlert error={create.error} />
                <button type="submit" className="button" disabled={create.isPending}>
                  + Dansgroep toevoegen
                </button>
              </form>
            </section>
          ) : null}
        </div>
      ) : null}
      {d && d.unassigned.length ? (
        <p>
          <span className="badge warn">{d.unassigned.length} niet ingedeeld</span> Nog niet in een dansgroep:{' '}
          {d.unassigned.map((m) => m.fullName).join(', ')} — deel ze in via het <Link to="/dansgarde">Overzicht</Link>.
        </p>
      ) : null}
    </>
  );
}
