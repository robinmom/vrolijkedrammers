import { useId, useMemo, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useAppTexts, type Schemas } from '../api/hooks';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

type AppText = Schemas['AppTextResponse'];

/** Bij zoeken één platte lijst; meer treffers dan dit vraagt om een preciezere zoekopdracht. */
const maxResults = 50;

/**
 * "app/meer/vereniging.tsx" → "Meer → Vereniging". Teksten buiten `app/` staan in onderdelen die op meerdere
 * schermen terugkomen (knoppen, kaarten), dus die krijgen één eigen groep.
 */
function screenOf(file: string): string {
  if (!file.startsWith('app/')) {
    return 'Onderdelen die op meer schermen terugkomen';
  }
  const parts = file
    .slice('app/'.length)
    .replace(/\.tsx?$/, '')
    .replace('(tabs)/', '')
    .split('/')
    .filter((part) => part !== 'index' && part !== '_layout');
  if (parts.length === 0) {
    return 'Beginscherm';
  }
  return parts.map((part) => part.replace(/[-[\]]/g, ' ').trim()).map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(' → ');
}

export function AppTextsPage() {
  const texts = useAppTexts();
  const [search, setSearch] = useState('');
  const [onlyChanged, setOnlyChanged] = useState(false);
  const [open, setOpen] = useState<string[]>([]);

  const rows = useMemo(() => texts.data ?? [], [texts.data]);
  const changed = rows.filter((row) => row.value !== null).length;

  const matching = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return rows.filter(
      (row) =>
        (!onlyChanged || row.value !== null) &&
        (needle === '' || row.default.toLowerCase().includes(needle) || (row.value ?? '').toLowerCase().includes(needle)),
    );
  }, [rows, search, onlyChanged]);

  const groups = useMemo(() => {
    const byScreen = new Map<string, AppText[]>();
    for (const row of matching) {
      const screen = screenOf(row.places[0]?.file ?? '');
      byScreen.set(screen, [...(byScreen.get(screen) ?? []), row]);
    }
    return [...byScreen.entries()].sort((a, b) => a[0].localeCompare(b[0], 'nl'));
  }, [matching]);

  const flat = search.trim() !== '' || onlyChanged;

  return (
    <>
      <h1>App-teksten</h1>
      <p>
        De teksten die in de app te zien zijn. Een aanpassing hier staat binnen een halve minuut op de telefoons; er is
        geen nieuwe app in de App Store of Play Store voor nodig. Laat je een tekst leeg of zet je hem terug, dan toont
        de app weer de tekst uit de app zelf.
      </p>
      <p className="muted">
        De lijst hoort bij de app-versie die samen met dit portal is uitgerold. Teksten die de app ter plekke
        samenstelt (bijvoorbeeld &quot;3 dansen&quot;) staan er niet in: die veranderen alleen met een nieuwe app.
      </p>

      <section className="card" aria-labelledby="zoeken">
        <h2 id="zoeken">Zoeken</h2>
        <Field
          label="Tekst zoeken"
          hint="Zoekt in de tekst zoals die in de app staat en in je aanpassing."
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <Checkbox label="Alleen aangepaste teksten" checked={onlyChanged} onChange={(e) => setOnlyChanged(e.target.checked)} />
        <p aria-live="polite">
          {rows.length} teksten, {changed} aangepast
          {flat ? ` · ${matching.length} gevonden` : ''}
        </p>
        <ProblemAlert error={texts.error} />
      </section>

      {flat ? (
        <section className="card" aria-labelledby="gevonden">
          <h2 id="gevonden">Gevonden teksten</h2>
          {matching.length === 0 ? <p>Geen tekst gevonden.</p> : null}
          {matching.length > maxResults ? <p className="muted">De eerste {maxResults} staan hieronder; zoek preciezer om de rest te zien.</p> : null}
          <ul className="plain">
            {matching.slice(0, maxResults).map((row) => (
              <TextRow key={row.key} row={row} screen={screenOf(row.places[0]?.file ?? '')} />
            ))}
          </ul>
        </section>
      ) : (
        groups.map(([screen, items]) => (
          <ScreenGroup
            key={screen}
            screen={screen}
            items={items}
            open={open.includes(screen)}
            onToggle={() => setOpen(open.includes(screen) ? open.filter((s) => s !== screen) : [...open, screen])}
          />
        ))
      )}
    </>
  );
}

/** Eén scherm uit de app; de teksten komen pas in de pagina als je de groep opent (het zijn er honderden). */
function ScreenGroup({ screen, items, open, onToggle }: { screen: string; items: AppText[]; open: boolean; onToggle: () => void }) {
  const id = useId();
  return (
    <section className="card" aria-labelledby={id}>
      <h2 id={id}>{screen}</h2>
      <button type="button" className="button secondary" aria-expanded={open} onClick={onToggle}>
        {open ? 'Teksten verbergen' : `${items.length} ${items.length === 1 ? 'tekst' : 'teksten'} tonen`}
      </button>
      {open ? (
        <ul className="plain">
          {items.map((row) => (
            <TextRow key={row.key} row={row} />
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function TextRow({ row, screen }: { row: AppText; screen?: string }) {
  const api = useApi();
  const id = useId();
  const [value, setValue] = useState(row.value ?? row.default);
  const [message, setMessage] = useState<string | null>(null);
  const save = useApiMutation(
    (text: string) => api.PUT('/api/v1/admin/config/app-texts/{key}', { params: { path: { key: row.key } }, body: { value: text } }),
    [['app-texts']],
  );
  const reset = useApiMutation(() => api.DELETE('/api/v1/admin/config/app-texts/{key}', { params: { path: { key: row.key } } }), [['app-texts']]);

  const unchanged = value.trim() === (row.value ?? row.default);
  return (
    <li className="app-text">
      <div className="field">
        <label htmlFor={id}>
          {screen ? <span className="muted">{screen} · </span> : null}
          {row.default}
          {row.value !== null ? <span className="badge"> aangepast</span> : null}
        </label>
        <textarea id={id} rows={row.default.length > 120 ? 4 : 2} maxLength={2000} value={value} onChange={(e) => setValue(e.target.value)} />
      </div>
      <div className="actions">
        <button
          type="button"
          className="button"
          disabled={save.isPending || unchanged || value.trim() === ''}
          onClick={() => save.mutate(value.trim(), { onSuccess: () => setMessage('Opgeslagen.') })}
        >
          Opslaan
        </button>
        {row.value !== null ? (
          <button
            type="button"
            className="button secondary"
            disabled={reset.isPending}
            onClick={() =>
              reset.mutate(undefined, {
                onSuccess: () => {
                  setValue(row.default);
                  setMessage('Teruggezet op de tekst uit de app.');
                },
              })
            }
          >
            Terugzetten
          </button>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={save.error ?? reset.error} />
    </li>
  );
}
