/**
 * Verzamelt de zichtbare teksten uit de app-bron en schrijft de catalogus die het portal gebruikt
 * (`pnpm --filter @drammers/mobile texts`).
 *
 * Waarom een catalogus: de app vervangt teksten op de tekst zelf (zie `src/content/texts.ts`), dus er zijn geen
 * sleutels in de schermen nodig. Het portal moet alleen weten welke teksten er bestaan en waar ze staan. Die lijst
 * wordt gegenereerd, nooit met de hand bijgehouden: een nieuwe tekst in de app komt zo automatisch in het portal.
 * CI controleert of de catalogus bij de bron past (zie .github/workflows/ci.yml).
 *
 * De catalogus staat in de API, want die levert hem aan het portal; het is één bestand, geen kopie.
 */
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const here = dirname(fileURLToPath(import.meta.url));
const srcDir = resolve(here, '../src');
const catalogPath = resolve(here, '../../../src/Drammers.Api/Content/app-texts.catalog.json');

/** Eigenschappen en attributen die een zichtbare tekst dragen. */
const textProps = new Set([
  'action',
  'body',
  'buttonLabel',
  'cancelLabel',
  'caption',
  'confirmLabel',
  'description',
  'emptyBody',
  'emptyTitle',
  'heading',
  'help',
  'hint',
  'intro',
  'label',
  'message',
  'note',
  'placeholder',
  'subtitle',
  'text',
  'title',
]);

/** Mappen waar `title`/`body`/`text` over data gaan in plaats van over zichtbare tekst. */
const dataDirs = ['api/', 'lib/', 'theme/', 'auth/'];

/** Schermen die alleen in ontwikkeling bestaan; die teksten hoort het bestuur niet in het portal te zien. */
const skipFiles = new Set(['app/componenten.tsx']);

function walk(dir) {
  const files = [];
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      if (name === '__tests__' || name === 'test') continue;
      files.push(...walk(path));
    } else if ((name.endsWith('.tsx') || name.endsWith('.ts')) && !name.endsWith('.generated.ts')) {
      files.push(path);
    }
  }
  return files;
}

/**
 * De waarde die React van losse JSX-tekst maakt: lege regels verdwijnen, de rest wordt met één spatie aan elkaar
 * geplakt. Dat moet precies kloppen, want de app zoekt de vervanging op met de tekst zoals die gerenderd wordt.
 */
function jsxTextValue(raw) {
  const lines = raw.split('\n');
  let out = '';
  for (let i = 0; i < lines.length; i++) {
    let line = lines[i];
    if (i > 0) line = line.replace(/^[ \t]+/, '');
    if (i < lines.length - 1) line = line.replace(/[ \t]+$/, '');
    if (line === '') continue;
    out = out === '' ? line : `${out} ${line}`;
  }
  return out;
}

/** Houdt sleutels, paden en kleuren buiten de catalogus: zichtbare tekst heeft een hoofdletter of een spatie. */
function isVisibleText(value) {
  if (value.length < 2 || value.length > 2000) return false;
  if (!/\p{L}/u.test(value)) return false;
  if (value.includes('://') || value.startsWith('#')) return false;
  return /[A-Z\u00C0-\u00DE]/.test(value) || /\s/.test(value);
}

function literalValue(node) {
  if (!node) return null;
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return node.text;
  return null;
}

const entries = new Map();

function add(value, file, where) {
  if (!isVisibleText(value)) return;
  const key = createHash('sha256').update(value).digest('hex').slice(0, 16);
  const entry = entries.get(key) ?? { key, text: value, places: [] };
  const place = { file, where };
  if (!entry.places.some((p) => p.file === place.file && p.where === place.where)) entry.places.push(place);
  entries.set(key, entry);
}

for (const path of walk(srcDir)) {
  const file = relative(srcDir, path).split('\\').join('/');
  if (skipFiles.has(file)) continue;
  const inDataDir = dataDirs.some((d) => file.startsWith(d));
  const source = ts.createSourceFile(path, readFileSync(path, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

  const visit = (node) => {
    // Losse tekst tussen JSX-tags: <AppText>Lid worden</AppText>
    if (ts.isJsxText(node)) {
      add(jsxTextValue(node.text), file, 'tekst');
    }

    // Een tekst in accolades: <AppText>{'Lid worden'}</AppText>
    if (ts.isJsxExpression(node) && node.parent && ts.isJsxElement(node.parent)) {
      const value = literalValue(node.expression);
      if (value !== null) add(value, file, 'tekst');
    }

    // Een attribuut dat tekst draagt: label="Opslaan"
    if (ts.isJsxAttribute(node) && ts.isIdentifier(node.name) && textProps.has(node.name.text)) {
      const initializer = node.initializer;
      const value = initializer && ts.isJsxExpression(initializer) ? literalValue(initializer.expression) : literalValue(initializer);
      if (value !== null) add(value, file, node.name.text);
    }

    // Een eigenschap die tekst draagt: { title: 'Niet uitgeven' } — niet in de mappen waar die namen over data gaan.
    if (!inDataDir && ts.isPropertyAssignment(node) && (ts.isIdentifier(node.name) || ts.isStringLiteral(node.name)) && textProps.has(node.name.text)) {
      const value = literalValue(node.initializer);
      if (value !== null) add(value, file, node.name.text);
    }

    // Alert.alert('Titel', 'Bericht')
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression) && node.expression.name.text === 'alert') {
      for (const argument of node.arguments) {
        const value = literalValue(argument);
        if (value !== null) add(value, file, 'melding');
      }
    }

    // De vaste teksten van de Meer-pagina's staan in losse arrays en objecten.
    if (file === 'content/static.ts' && (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))) {
      add(node.text, file, 'vaste tekst');
    }

    ts.forEachChild(node, visit);
  };
  ts.forEachChild(source, visit);
}

const sorted = [...entries.values()].sort((a, b) => a.text.localeCompare(b.text, 'nl'));
const catalog = { version: 1, entries: sorted };
writeFileSync(catalogPath, `${JSON.stringify(catalog, null, 2)}\n`);
console.log(`${sorted.length} teksten -> ${relative(resolve(here, '../../..'), catalogPath)}`);
