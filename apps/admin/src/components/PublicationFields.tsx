import { useRoles, type Schemas } from '../api/hooks';
import { fromLocalInput, statusLabels, toLocalInput, visibilityLabels } from '../format';
import { Checkbox, Field } from './Field';

export type Publication = Schemas['PublicationRequest'];

export const defaultPublication: Publication = { visibility: 'Public', audienceRoles: [], status: 'Draft', publishAt: null };

/** Zichtbaarheid (Iedereen/Leden/Beperkt + rollen) en publicatie (status en moment); gedeeld door agenda, nieuws en foto's. */
export function PublicationFields({ value, onChange }: { value: Publication; onChange: (value: Publication) => void }) {
  const roles = useRoles();
  const audience = value.audienceRoles ?? [];
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
