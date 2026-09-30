import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  linkLabels,
  settingsRequest,
  useWebsiteSettings,
  WEBSITE_KEYS,
  type WebsiteLink,
  type WebsiteSettingsRequest,
} from '../api/website';
import { Checkbox, Field } from '../components/Field';
import { ImagePicker } from '../components/ImagePicker';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

const links = Object.keys(linkLabels) as WebsiteLink[];

function LinkSelect({ label, value, onChange }: { label: string; value: WebsiteLink | null | undefined; onChange: (v: WebsiteLink | null) => void }) {
  const id = `link-${label.replace(/\W+/g, '-').toLowerCase()}`;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value ?? ''} onChange={(e) => onChange((e.target.value || null) as WebsiteLink | null)}>
        <option value="">Kies…</option>
        {links.map((l) => (
          <option key={l} value={l}>
            {linkLabels[l]}
          </option>
        ))}
      </select>
    </div>
  );
}

function useSettingsForm() {
  const settings = useWebsiteSettings();
  const api = useApi();
  const [form, setForm] = useState<WebsiteSettingsRequest | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    if (settings.data) setForm(settingsRequest(settings.data));
  }, [settings.data]);
  const save = useApiMutation(
    async (body: WebsiteSettingsRequest) => api.PUT('/api/v1/admin/website/settings', { body }),
    WEBSITE_KEYS,
  );
  function submit(event: FormEvent) {
    event.preventDefault();
    if (form) save.mutate(form, { onSuccess: () => setMessage('Opgeslagen.') });
  }
  const set = (change: Partial<WebsiteSettingsRequest>) => form && setForm({ ...form, ...change });
  return { settings, form, set, save, submit, message };
}

/** Website → Homepage: de hero (foto, bovenregel, titel, ondertitel, twee knoppen); titel, ondertitel en foto ook in de app. */
export function WebsiteHomePage() {
  const { settings, form, set, save, submit, message } = useSettingsForm();
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Homepage</h1>
          <p className="muted">De grote foto en tekst bovenaan de website en het beginscherm van de app.</p>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={settings.error} />
      {form ? (
        <form className="grid-2 website-hero-form" onSubmit={submit}>
          <section className="card" aria-labelledby="hero-tekst">
            <h2 id="hero-tekst">Hero</h2>
            <Field label="Bovenregel" maxLength={80} value={form.heroEyebrow ?? ''} onChange={(e) => set({ heroEyebrow: e.target.value || null })} />
            <Field label="Titel" required maxLength={120} value={form.heroTitle} onChange={(e) => set({ heroTitle: e.target.value })} />
            <div className="field">
              <label htmlFor="hero-ondertitel">Ondertitel</label>
              <textarea id="hero-ondertitel" rows={3} maxLength={300} value={form.heroSubtitle ?? ''} onChange={(e) => set({ heroSubtitle: e.target.value || null })} />
            </div>
            <div className="form-grid">
              <Field label="Knop 1: tekst" maxLength={40} value={form.heroPrimaryLabel ?? ''} onChange={(e) => set({ heroPrimaryLabel: e.target.value || null })} />
              <LinkSelect label="Knop 1: gaat naar" value={form.heroPrimaryLink} onChange={(heroPrimaryLink) => set({ heroPrimaryLink })} />
              <Field label="Knop 2: tekst" maxLength={40} value={form.heroSecondaryLabel ?? ''} onChange={(e) => set({ heroSecondaryLabel: e.target.value || null })} />
              <LinkSelect label="Knop 2: gaat naar" value={form.heroSecondaryLink} onChange={(heroSecondaryLink) => set({ heroSecondaryLink })} />
            </div>
            <p className="muted">De app toont de titel, de ondertitel en de foto; de knoppen staan alleen op de website.</p>
          </section>
          <section className="card" aria-labelledby="hero-foto">
            <h2 id="hero-foto">Foto</h2>
            <ImagePicker
              label="Hero-foto"
              uploadPath="/api/v1/admin/website/images"
              currentUrl={settings.data?.heroImageUrl}
              onChange={(heroImage) => set({ heroImage })}
            />
            <p className="muted">Liggende foto, minstens 2000 px breed. De tekst staat links over de foto: kies een foto met een rustige linkerkant.</p>
          </section>
          <div className="actions span-2">
            <ProblemAlert error={save.error} />
            <button type="submit" className="button" disabled={save.isPending}>
              Opslaan
            </button>
          </div>
        </form>
      ) : null}
    </>
  );
}

/** Website → Instellingen: Facebook en Instagram, en de pagina Jeugdprinsen aan- of uitzetten. */
export function WebsiteSettingsPage() {
  const { settings, form, set, save, submit, message } = useSettingsForm();
  const youth = settings.data?.youthPrinceCount ?? 0;
  return (
    <>
      <h1>Instellingen website</h1>
      <SuccessMessage message={message} />
      <ProblemAlert error={settings.error} />
      {form ? (
        <form className="card" onSubmit={submit}>
          <fieldset>
            <legend>Social media</legend>
            <Field
              label="Facebookpagina"
              type="url"
              hint="De homepage toont de laatste berichten van deze pagina."
              value={form.facebookPageUrl ?? ''}
              onChange={(e) => set({ facebookPageUrl: e.target.value || null })}
            />
            <Field label="Instagram" type="url" value={form.instagramUrl ?? ''} onChange={(e) => set({ instagramUrl: e.target.value || null })} />
          </fieldset>
          <fieldset>
            <legend>Pagina's</legend>
            <Checkbox
              label={`Pagina Jeugdprinsen tonen op de website (${youth === 1 ? '1 jeugdprins(es)' : `${youth} jeugdprinsen`} ingevuld)`}
              checked={form.showYouthPrinces}
              onChange={(e) => set({ showYouthPrinces: e.target.checked })}
            />
            {youth === 0 ? <p className="muted">Vul eerst de jeugdprinsen in onder Website → Prinsen.</p> : null}
          </fieldset>
          <ProblemAlert error={save.error} />
          <div className="actions">
            <button type="submit" className="button" disabled={save.isPending}>
              Opslaan
            </button>
          </div>
        </form>
      ) : null}
    </>
  );
}
