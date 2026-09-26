import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useAppConfigSettings, useFeatureFlags, useRetention, type Schemas } from '../api/hooks';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

type AppConfig = Schemas['AppConfigSettingsRequest'];
type RetentionAction = Schemas['RetentionAction'];

const actionLabels: Record<RetentionAction, string> = { Delete: 'Verwijderen', Anonymize: 'Anonimiseren', Aggregate: 'Aggregeren' };

export function ConfigPage() {
  return (
    <>
      <h1>Configuratie</h1>
      <AppConfigSection />
      <FeatureFlagsSection />
      <RetentionSection />
    </>
  );
}

function AppConfigSection() {
  const api = useApi();
  const settings = useAppConfigSettings();
  const [form, setForm] = useState<AppConfig | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    if (settings.data) {
      setForm(settings.data);
    }
  }, [settings.data]);
  const save = useApiMutation((body: AppConfig) => api.PUT('/api/v1/admin/config/app-config', { body }), [['app-config']]);

  if (!form) {
    return <ProblemAlert error={settings.error} />;
  }
  const set = (change: Partial<AppConfig>) => setForm({ ...form, ...change });
  function submit(event: FormEvent) {
    event.preventDefault();
    if (form) {
      save.mutate(form, { onSuccess: () => setMessage('App-instellingen opgeslagen; direct actief.') });
    }
  }

  return (
    <section className="card" aria-labelledby="app">
      <h2 id="app">App en onderhoud</h2>
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Minimale versie iOS" pattern="^\d+\.\d+\.\d+$" required value={form.minAppVersionIos} onChange={(e) => set({ minAppVersionIos: e.target.value })} />
          <Field label="Minimale versie Android" pattern="^\d+\.\d+\.\d+$" required value={form.minAppVersionAndroid} onChange={(e) => set({ minAppVersionAndroid: e.target.value })} />
          <Field label="Aanbevolen versie" pattern="^\d+\.\d+\.\d+$" required value={form.recommendedAppVersion} onChange={(e) => set({ recommendedAppVersion: e.target.value })} />
          <Field label="E-mailadres support" type="email" value={form.supportEmail ?? ''} onChange={(e) => set({ supportEmail: e.target.value || null })} />
        </div>
        <Checkbox label="Onderhoudsmodus (app toont een melding)" checked={form.maintenanceMode} onChange={(e) => set({ maintenanceMode: e.target.checked })} />
        <Field label="Onderhoudsmelding" value={form.maintenanceMessage ?? ''} onChange={(e) => set({ maintenanceMessage: e.target.value || null })} />
        <SuccessMessage message={message} />
        <ProblemAlert error={save.error} />
        <button type="submit" className="button" disabled={save.isPending}>
          Opslaan
        </button>
      </form>
    </section>
  );
}

function FeatureFlagsSection() {
  const api = useApi();
  const flags = useFeatureFlags();
  const [newKey, setNewKey] = useState('');
  const setFlag = useApiMutation(
    (v: { key: string; enabled: boolean; description: string | null }) =>
      api.PUT('/api/v1/admin/config/feature-flags/{key}', { params: { path: { key: v.key } }, body: { enabled: v.enabled, description: v.description } }),
    [['feature-flags']],
  );

  return (
    <section className="card" aria-labelledby="flags">
      <h2 id="flags">Feature flags</h2>
      <ul className="plain">
        {(flags.data ?? []).map((flag) => (
          <li key={flag.key}>
            <Checkbox
              label={
                <>
                  <code>{flag.key}</code> {flag.description}
                </>
              }
              checked={flag.enabled}
              onChange={(e) => setFlag.mutate({ key: flag.key, enabled: e.target.checked, description: flag.description })}
            />
          </li>
        ))}
      </ul>
      <form
        className="toolbar"
        onSubmit={(e) => {
          e.preventDefault();
          setFlag.mutate({ key: newKey, enabled: false, description: null }, { onSuccess: () => setNewKey('') });
        }}
      >
        <Field label="Nieuwe flag" pattern="^[a-z][a-z0-9.\-]{1,98}$" hint="bijv. nieuws.push" value={newKey} onChange={(e) => setNewKey(e.target.value)} />
        <button type="submit" className="button secondary" disabled={!newKey}>
          Toevoegen
        </button>
      </form>
      <ProblemAlert error={flags.error ?? setFlag.error} />
    </section>
  );
}

function RetentionSection() {
  const api = useApi();
  const retention = useRetention();
  const [message, setMessage] = useState<string | null>(null);
  const update = useApiMutation(
    (v: { dataType: string; retentionDays: number; action: RetentionAction }) =>
      api.PUT('/api/v1/admin/config/retention/{dataType}', {
        params: { path: { dataType: v.dataType } },
        body: { retentionDays: v.retentionDays, action: v.action },
      }),
    [['retention']],
  );

  return (
    <section className="card" aria-labelledby="bewaar">
      <h2 id="bewaar">Bewaartermijnen</h2>
      <SuccessMessage message={message} />
      <ProblemAlert error={retention.error ?? update.error} />
      <div className="table-scroll" tabIndex={0} role="region" aria-label="Bewaartermijnen">
        <table className="table compact">
          <caption className="visually-hidden">Bewaartermijn per gegevenssoort</caption>
          <thead>
            <tr>
              <th scope="col">Gegevens</th>
              <th scope="col">Dagen</th>
              <th scope="col">Actie</th>
              <th scope="col">
                <span className="visually-hidden">Opslaan</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(retention.data ?? []).map((row) => (
              <RetentionRow
                key={row.dataType}
                dataType={row.dataType}
                days={row.retentionDays}
                action={row.action as RetentionAction}
                onSave={(retentionDays, action) =>
                  update.mutate({ dataType: row.dataType, retentionDays, action }, { onSuccess: () => setMessage(`Bewaartermijn ${row.dataType} opgeslagen.`) })
                }
              />
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function RetentionRow({ dataType, days, action, onSave }: { dataType: string; days: number; action: RetentionAction; onSave: (days: number, action: RetentionAction) => void }) {
  const [value, setValue] = useState(days);
  const [kind, setKind] = useState<RetentionAction>(action);
  return (
    <tr>
      <td>
        <code>{dataType}</code>
      </td>
      <td>
        <input type="number" min={1} max={3650} aria-label={`Dagen voor ${dataType}`} value={value} onChange={(e) => setValue(Number(e.target.value))} />
      </td>
      <td>
        <select aria-label={`Actie voor ${dataType}`} value={kind} onChange={(e) => setKind(e.target.value as RetentionAction)}>
          {Object.entries(actionLabels).map(([k, label]) => (
            <option key={k} value={k}>
              {label}
            </option>
          ))}
        </select>
      </td>
      <td>
        <button type="button" className="button secondary small" disabled={value === days && kind === action} onClick={() => onSave(value, kind)}>
          Opslaan <span className="visually-hidden">{dataType}</span>
        </button>
      </td>
    </tr>
  );
}
