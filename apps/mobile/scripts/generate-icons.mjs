// Genereert src/ui/icons.generated.ts uit de ongewijzigde Figma-SVG's in assets/icons.
// Kleuren worden niet in de bestanden aangepast; het Icon-component kleurt ze in bij het renderen.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const iconDir = join(root, 'assets', 'icons');
const files = readdirSync(iconDir).filter((f) => f.endsWith('.svg')).sort();

const entries = files.map((file) => {
  const name = basename(file, '.svg');
  const svg = readFileSync(join(iconDir, file), 'utf8').trim();
  return `  ${JSON.stringify(name)}: ${JSON.stringify(svg)},`;
});

const output = `// GEGENEREERD door scripts/generate-icons.mjs – niet handmatig wijzigen.
// Bron: Figma "Vrolijke Drammers – App Design" (docs/17), lijn-iconen 24 px, stroke 2.
export const icons = {
${entries.join('\n')}
} as const;

export type IconName = keyof typeof icons;
`;

writeFileSync(join(root, 'src', 'ui', 'icons.generated.ts'), output);
console.log(`icons.generated.ts: ${files.length} iconen`);
