import type { Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Fase 21d: lid worden, optocht inschrijven, aanrijtijden en kaarten zijn pagina's van de website (Razor). Het formulier
 * staat als pure HTML in src/Drammers.Website/Pages/Shared/Forms; deze helper zet precies die HTML met de echte scripts
 * en stylesheet in een eenvoudige paginakop, met dezelfde CSP als de website. Kop, menu en voet test de .NET-integratietest.
 */
const website = join(dirname(fileURLToPath(import.meta.url)), '../../../src/Drammers.Website');
const types: Record<string, string> = {
  css: 'text/css',
  js: 'text/javascript',
  png: 'image/png',
  woff2: 'font/woff2',
  jpg: 'image/jpeg',
};

export const websiteCsp =
  "default-src 'self'; img-src 'self' data: https://*.fbcdn.net; connect-src 'self' https://*.ciamlogin.com; " +
  "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

export interface WebsiteForm {
  /** Kop van de pagina (de scripts kunnen hem aanpassen). */
  title: string;
  /** Bestandsnaam in Pages/Shared/Forms, bijvoorbeeld `_LidWorden`. */
  form: string;
  /** Scripts onder wwwroot/js, in volgorde. */
  scripts: string[];
}

/** Serveert de opgegeven paden (bijv. `/lid-worden/`) en de bestanden onder `/_content/Drammers.Website/`. */
export async function serveWebsitePages(
  page: Page,
  pages: Record<string, WebsiteForm>,
  overrides: Record<string, string> = {},
) {
  await page.route(
    (url) => url.pathname.startsWith('/_content/Drammers.Website/'),
    (route) => {
      const file = new URL(route.request().url()).pathname.replace('/_content/Drammers.Website/', '');
      return route.fulfill({
        status: 200,
        contentType: types[file.split('.').pop()!] ?? 'application/octet-stream',
        body: overrides[file] ?? readFileSync(join(website, 'wwwroot', file)),
      });
    },
  );
  await page.route(
    (url) => url.pathname in pages,
    (route) => {
      const p = pages[new URL(route.request().url()).pathname]!;
      const form = readFileSync(join(website, 'Pages/Shared/Forms', `${p.form}.cshtml`), 'utf8');
      const scripts = p.scripts.map((s) => `<script src="/_content/Drammers.Website/js/${s}" defer></script>`).join('');
      const html = `<!doctype html><html lang="nl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>${p.title}</title><link rel="stylesheet" href="/_content/Drammers.Website/css/site.css">
<script src="/_content/Drammers.Website/js/site.js" defer></script>${scripts}</head>
<body><main id="inhoud"><section class="page-band"><div class="wrap"><h1>${p.title}</h1><p class="lead">Testpagina</p></div></section>
<div class="wrap content form-page">${form}</div></main></body></html>`;
      return route.fulfill({
        status: 200,
        contentType: 'text/html',
        headers: { 'content-security-policy': websiteCsp },
        body: html,
      });
    },
  );
}

export const forms = {
  lidWorden: {
    '/lid-worden/': { title: 'Word ook een Drammer!', form: '_LidWorden', scripts: ['forms/lid-worden.js'] },
  },
  optocht: {
    '/optocht-inschrijven/': {
      title: 'Optocht inschrijven',
      form: '_OptochtInschrijven',
      scripts: ['vendor/msal-browser.min.js', 'login.js', 'forms/optocht-inschrijven.js'],
    },
  },
  aanrijtijden: {
    '/aanrijtijden/': { title: 'Aanrijtijden optocht', form: '_Aanrijtijden', scripts: ['forms/aanrijtijden.js'] },
  },
  kaarten: {
    '/kaarten/': { title: 'Kaarten', form: '_Kaarten', scripts: ['forms/kaarten.js'] },
    '/kaarten/bestelling/': {
      title: 'Je bestelling',
      form: '_KaartenBestelling',
      scripts: ['forms/kaarten-bestelling.js'],
    },
  },
} satisfies Record<string, Record<string, WebsiteForm>>;
