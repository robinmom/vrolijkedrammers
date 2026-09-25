import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

async function open(page: Page, api: MockApi, path = '') {
  await api.install(page);
  await page.goto(`/beheer/${path}`);
}

async function openMenuIfMobile(page: Page) {
  const toggle = page.getByRole('button', { name: 'Menu' });
  if (await toggle.isVisible()) {
    await toggle.click();
  }
}

async function expectNoSeriousA11yIssues(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

test('bestuurder kent een rol toe en ziet die in de auditlog', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'gebruikers');
  await page.getByRole('link', { name: 'Jan Lid' }).click();
  await page.getByRole('checkbox', { name: 'Redactie' }).check();
  await page.getByRole('button', { name: 'Rollen opslaan' }).click();
  await expect(page.getByRole('status')).toHaveText('Rollen opgeslagen.');

  await openMenuIfMobile(page);
  await page.getByRole('link', { name: 'Auditlog' }).click();
  await expect(page.getByRole('cell', { name: 'user.roles.changed' })).toBeVisible();
  expect(api.userRoles['u-jan']!.map((r) => r.code)).toEqual(['lid', 'redactie']);
});

test('gebruiker zonder beheerrechten ziet "Geen beheerrechten"', async ({ page }) => {
  await open(page, new MockApi(['news.read']));
  await expect(page.getByRole('heading', { name: 'Geen beheerrechten' })).toBeVisible();
  await openMenuIfMobile(page);
  await expect(page.getByRole('navigation', { name: 'Navigatie' }).getByRole('link')).toHaveCount(0);
});

test('onderhoudsmodus aanzetten', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'configuratie');
  await page.getByRole('checkbox', { name: /Onderhoudsmodus/ }).check();
  await page.getByLabel('Onderhoudsmelding').fill('We zijn zo terug');
  await page.getByRole('region', { name: 'App en onderhoud' }).getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByText('App-instellingen opgeslagen; direct actief.')).toBeVisible();
  expect(api.appConfig.maintenanceMode).toBe(true);
});

test('carnavalsjaar toevoegen en activeren; precies één actief', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'carnavalsjaren');
  await page.getByRole('button', { name: 'Carnavalsjaar toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Naam').fill('2027/2028');
  await dialog.getByLabel('Seizoen vanaf').fill('2027-11-11');
  await dialog.getByLabel('Seizoen tot').fill('2028-03-01');
  await dialog.getByLabel('Carnaval vanaf').fill('2028-02-26');
  await dialog.getByLabel('Carnaval tot').fill('2028-02-29');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await page.getByRole('button', { name: 'Activeren 2027/2028' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Activeren' }).click();
  await expect(page.getByText('2027/2028 is nu actief.')).toBeVisible();
  expect(api.years.filter((y) => y.active).map((y) => y.name)).toEqual(['2027/2028']);
});

test('redacteur publiceert een event voor iedereen; het staat in de publieke agenda', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'agenda');
  await page.getByRole('link', { name: 'Event toevoegen' }).click();
  await page.getByLabel('Titel').fill('Pronkzitting');
  await page.getByLabel('Begint').fill('2027-01-16T20:00');
  await page.getByLabel('Status').selectOption('Published');
  await page.getByRole('button', { name: 'Opslaan', exact: true }).click();
  await expect(page.getByText('Event opgeslagen.')).toBeVisible();

  const agenda = await page.evaluate(async () => (await fetch('/api/v1/events')).json());
  expect(agenda.items.map((e: { title: string }) => e.title)).toEqual(['Pronkzitting']);
  expect(api.events[0]!.publication.visibility).toBe('Public');
});

for (const path of [
  '',
  'agenda',
  'agenda/nieuw',
  'nieuws',
  'nieuws/nieuw',
  'fotos',
  'fotos/a-1',
  'leden',
  'leden/m-2',
  'ledensync',
  'ledensync/j-1',
  'groepen',
  'groepen/g-1',
  'rapportage',
  'gebruikers',
  'gebruikers/u-jan',
  'rollen',
  'carnavalsjaren',
  'configuratie',
  'auditlog',
]) {
  for (const scheme of ['light', 'dark'] as const) {
    test(`toegankelijkheid (axe, ${scheme === 'light' ? 'licht' : 'donker'}) /${path}`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      const api = new MockApi();
      // Het rapport van een run bestaat pas na een start; voor de axe-check één run klaarzetten.
      api.syncJobs.push({
        id: 'j-1',
        status: 'Succeeded',
        dryRun: true,
        trigger: 'Manual',
        requestedAt: '2026-09-25T10:00:00Z',
        startedAt: null,
        completedAt: null,
        totalInSource: 3,
        created: 1,
        updated: 1,
        unchanged: 1,
        missing: 0,
        deactivated: 0,
        reactivated: 0,
        warnings: 0,
        errors: 0,
        conflicts: 0,
        errorMessage: null,
      });
      await open(page, api, path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expectNoSeriousA11yIssues(page);
      // Geen horizontaal scrollen van de hele pagina (op mobiel verschuiven taps anders naar het verkeerde element).
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      expect(overflow).toBeLessThanOrEqual(1);
    });
  }
}

test('bestuur start een dry-run, bekijkt het rapport en handelt een conflict af', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'ledensync');
  await page.getByRole('button', { name: 'Dry-run starten' }).click();
  await expect(page.getByText('Dry-run gestart. Het rapport verschijnt hieronder.')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Dry-run' })).toBeVisible();

  await page.getByRole('button', { name: 'Afhandelen' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Accepteren' }).click();
  await expect(page.getByRole('heading', { name: /Openstaande conflicten/ })).toHaveCount(0);
  expect(api.conflicts[0]!.status).toBe('Accepted');

  await page
    .getByRole('link', { name: /2026|2027/ })
    .first()
    .click();
  await expect(page.getByRole('cell', { name: 'e-mail, plaats' })).toBeVisible();
});

test('lid-detail: status-override opslaan en ontbrekend lid op inactief zetten', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'leden');
  await page.getByRole('link', { name: 'Anna Jansen' }).click();
  await expect(page.getByText(/niet meer in e-Boekhouden/)).toBeVisible();
  await page.getByRole('button', { name: 'Nu op inactief zetten' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Op inactief zetten' }).click();
  await expect(page.getByText('Het lid is op inactief gezet.')).toBeVisible();

  await page.getByLabel('Status-override').selectOption('Suspended');
  await page.getByRole('button', { name: 'Gegevens van de app opslaan' }).click();
  await expect(page.getByText('Opgeslagen.')).toBeVisible();
  expect(api.members[1]!.localStatusOverride).toBe('Suspended');
});

test('alle leden verwijderen vraagt een getypte bevestiging', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'leden');
  await page.getByRole('button', { name: 'Alle leden verwijderen' }).click();
  const dialog = page.getByRole('dialog');
  const confirm = dialog.getByRole('button', { name: 'Alles verwijderen' });
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel(/ter bevestiging/).fill('LEDEN VERWIJDEREN');
  await confirm.click();
  await expect(page.getByText(/2 leden en 0 syncruns verwijderd/)).toBeVisible();
  expect(api.members).toHaveLength(0);
});

test('zonder member.purge geen knop om leden te verwijderen', async ({ page }) => {
  await open(page, new MockApi(['member.read']), 'leden');
  await expect(page.getByRole('heading', { name: 'Leden' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Alle leden verwijderen' })).toHaveCount(0);
});

test('bestuur maakt een groep en voegt een lid toe', async ({ page }) => {
  const api = new MockApi();
  await open(page, api, 'groepen');
  await page.getByRole('button', { name: 'Groep toevoegen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Naam').fill('Dansgarde meisjes');
  await dialog.getByLabel('Soort').selectOption('DanceGuard');
  await dialog.getByRole('button', { name: 'Opslaan' }).click();
  await expect(page.getByRole('heading', { name: 'Dansgarde meisjes' })).toBeVisible();

  await page.getByLabel('Zoek op naam of lidnummer').fill('Anna');
  await page.getByRole('list', { name: 'Zoekresultaten' }).getByRole('button', { name: 'Toevoegen' }).click();
  await expect(page.getByText('Anna Jansen is toegevoegd.')).toBeVisible();
  expect(api.groups.find((g) => g.name === 'Dansgarde meisjes')!.members.map((m) => m.fullName)).toEqual([
    'Anna Jansen',
  ]);
});

test('redacteur kiest een groep als doelgroep; ledenkeuze alleen met ledenrechten', async ({ page }) => {
  const api = new MockApi(['event.manage', 'news.manage', 'photo.manage']);
  let sent: Record<string, unknown> | null = null;
  page.on('request', (request) => {
    if (request.url().endsWith('/api/v1/admin/news') && request.method() === 'POST') {
      sent = JSON.parse(request.postData()!) as Record<string, unknown>;
    }
  });
  await open(page, api, 'nieuws/nieuw');
  await page.getByLabel('Beperkt (rollen)').check();
  await page.getByRole('checkbox', { name: 'Jeugdcommissie' }).check();
  await expect(page.getByRole('group', { name: 'Individuele leden' })).toHaveCount(0);
  await page.getByLabel('Titel').fill('Vergadering');
  await page.getByLabel('Bericht (Markdown)').fill('Donderdag om acht uur.');
  await page
    .getByRole('button', { name: /Opslaan/ })
    .first()
    .click();
  await expect
    .poll(() => (sent?.publication as { audienceGroups?: string[] } | undefined)?.audienceGroups)
    .toEqual(['g-1']);
});

test('rapportage toont aantallen per categorie', async ({ page }) => {
  await open(page, new MockApi(), 'rapportage');
  await expect(page.getByText('3 leden in de app, waarvan 2 actief.', { exact: false })).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Per groep' }).getByRole('cell', { name: 'Jeugdcommissie' }),
  ).toBeVisible();
});
