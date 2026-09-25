import { useState } from 'react';
import { useAudienceGroups, useMe, useMember, useMembers, useRoles, type Schemas } from '../api/hooks';
import { fromLocalInput, statusLabels, toLocalInput, visibilityLabels } from '../format';
import { Checkbox, Field } from './Field';

export type Publication = Schemas['PublicationRequest'];

export const defaultPublication: Publication = {
  visibility: 'Public',
  audienceRoles: [],
  status: 'Draft',
  publishAt: null,
  audienceGroups: [],
  audienceMembers: [],
};

/**
 * Zichtbaarheid (Iedereen/Leden/Beperkt) en publicatie (status en moment); gedeeld door agenda, nieuws en foto's.
 * Bij "Beperkt" kies je rollen, groepen en (met toegang tot ledengegevens) individuele leden.
 */
export function PublicationFields({ value, onChange }: { value: Publication; onChange: (value: Publication) => void }) {
  const roles = useRoles();
  const groups = useAudienceGroups();
  const me = useMe();
  const audience = value.audienceRoles ?? [];
  const audienceGroups = value.audienceGroups ?? [];
  const canPickMembers = (me.data?.permissions ?? []).includes('member.read');
  return (
    <>
      <fieldset>
        <legend>Zichtbaar voor</legend>
        {(Object.keys(visibilityLabels) as Publication['visibility'][]).map((v) => (
          <div key={v} className="checkbox">
            <input
              type="radio"
              id={`zichtbaar-${v}`}
              name="zichtbaarheid"
              checked={value.visibility === v}
              onChange={() => onChange({ ...value, visibility: v })}
            />
            <label htmlFor={`zichtbaar-${v}`}>{visibilityLabels[v]}</label>
          </div>
        ))}
        {value.visibility === 'Restricted' ? (
          <>
            <p className="muted">Zichtbaar voor iedereen met een van de gekozen rollen, in een van de gekozen groepen, of een van de gekozen leden.</p>
            <fieldset>
              <legend>Rollen</legend>
              {(roles.data ?? []).map((role) => (
                <Checkbox
                  key={role.code}
                  label={role.name}
                  checked={audience.includes(role.code)}
                  onChange={(e) =>
                    onChange({
                      ...value,
                      audienceRoles: e.target.checked ? [...audience, role.code] : audience.filter((c) => c !== role.code),
                    })
                  }
                />
              ))}
            </fieldset>
            <fieldset>
              <legend>Groepen</legend>
              {(groups.data ?? []).length === 0 ? <p className="muted">Er zijn nog geen groepen.</p> : null}
              {(groups.data ?? []).map((group) => (
                <Checkbox
                  key={group.id}
                  label={group.name}
                  checked={audienceGroups.includes(group.id)}
                  onChange={(e) =>
                    onChange({
                      ...value,
                      audienceGroups: e.target.checked ? [...audienceGroups, group.id] : audienceGroups.filter((g) => g !== group.id),
                    })
                  }
                />
              ))}
            </fieldset>
            {canPickMembers ? (
              <MemberPicker selected={value.audienceMembers ?? []} onChange={(audienceMembers) => onChange({ ...value, audienceMembers })} />
            ) : null}
          </>
        ) : null}
      </fieldset>
      <div className="form-grid">
        <div className="field">
          <label htmlFor="publicatiestatus">Status</label>
          <select
            id="publicatiestatus"
            value={value.status}
            onChange={(e) => onChange({ ...value, status: e.target.value as Publication['status'] })}
          >
            {(Object.keys(statusLabels) as Publication['status'][]).map((s) => (
              <option key={s} value={s}>
                {statusLabels[s]}
              </option>
            ))}
          </select>
        </div>
        {value.status === 'Scheduled' ? (
          <Field
            label="Publiceren op"
            type="datetime-local"
            required
            value={toLocalInput(value.publishAt)}
            onChange={(e) => onChange({ ...value, publishAt: fromLocalInput(e.target.value) })}
          />
        ) : null}
      </div>
    </>
  );
}

/** Individuele leden als doelgroep: zoeken en toevoegen; geselecteerde leden met naam en een verwijderknop. */
function MemberPicker({ selected, onChange }: { selected: string[]; onChange: (ids: string[]) => void }) {
  const [search, setSearch] = useState('');
  const results = useMembers({ search, status: 'Active', syncState: '' }, 1);
  const matches = search.trim().length >= 2 ? (results.data?.items ?? []).filter((m) => !selected.includes(m.id)).slice(0, 8) : [];
  return (
    <fieldset>
      <legend>Individuele leden</legend>
      {selected.length > 0 ? (
        <ul className="list">
          {selected.map((id) => (
            <SelectedMember key={id} id={id} onRemove={() => onChange(selected.filter((s) => s !== id))} />
          ))}
        </ul>
      ) : null}
      <Field label="Lid zoeken op naam of lidnummer" value={search} onChange={(e) => setSearch(e.target.value)} />
      {matches.length > 0 ? (
        <ul className="list" aria-label="Zoekresultaten">
          {matches.map((m) => (
            <li key={m.id}>
              {m.fullName} ({m.memberNumber}){' '}
              <button type="button" className="button secondary small" onClick={() => onChange([...selected, m.id])}>
                Toevoegen
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </fieldset>
  );
}

function SelectedMember({ id, onRemove }: { id: string; onRemove: () => void }) {
  const member = useMember(id);
  const name = member.data ? `${member.data.fullName} (${member.data.memberNumber})` : 'Lid';
  return (
    <li>
      {name}{' '}
      <button type="button" className="button secondary small" onClick={onRemove} aria-label={`${name} verwijderen als doelgroep`}>
        Verwijderen
      </button>
    </li>
  );
}
