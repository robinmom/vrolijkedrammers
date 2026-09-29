import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MockApi } from './mock-api';

/** Fase 17: ouders/verzorgers (kaart bij een lid, koppelverzoeken, voorstellen) en de dansgarde (overzicht, dansgroepen). */

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
  expect(
    serious.map(
      (v) =>
        `${v.id}: ${v.nodes.map((n) => `${n.target.join(' ')} ${n.failureSummary} ${n.html.slice(0, 120)}`).join(', ')}`,
    ),
  ).toEqual([]);
}

const request = {
  id: 'gr-1',
  requestedByName: 'Sanne Mom',
  requestedByEmail: 'sanne@example.com',
  childFirstName: 'Piet',
  childLastName: 'Lid',
  relationship: 'Parent',
  phone: '0698765432',
  status: 'Pending',
  createdAt: '2026-09-28T17:40:00Z',
  decidedAt: null,
  rejectionReason: null,
  memberId: null,
  memberName: null,
  candidates: [{ memberId: 'm-1', name: 'Piet Lid', memberNumber: '1001', age: 14, guardians: 1 }],
};

function child(api: MockApi, own: Partial<Record<string, unknown>> = {}) {
  api.memberGuardians['m-1'] = {
    applies: true,
    max: 2,
    guardians: [
      {
        id: 'rel-1',
        userId: 'u-robin',
        name: 'Robin Mom',
        email: 'fam.mom@example.com',
        relationship: 'Parent',
        isMember: true,
        phone: null,
        createdAt: '2026-09-12T08:02:00Z',
      },
    ],
    suggestions: [],
    requests: [request],
    ownAccount: {
      hasAccount: false,
      email: null,
      age: 14,
      canGetOwnAccount: false,
      availableFrom: '2027-03-10',
      guardiansUntil: '2030-03-10',
      pending: false,
      ...own,
    },
  };
}

test('fase 17: ouders bij een kind — koppelverzoek goedkeuren, ouder zoeken en koppelen', async ({ page }) => {
  const api = new MockApi();
  child(api);
  await open(page, api, 'leden/m-1');

  const card = page.getByRole('region', { name: 'Ouders/verzorgers' });
  await expect(card.getByText('1 van 2')).toBeVisible();
  await expect(card.getByText('Robin Mom')).toBeVisible();
  await expect(card.getByText('Sanne Mom vraagt koppeling aan')).toBeVisible();
  // Een kind zonder eigen account: kaart "Eigen account" in plaats van "App-account".
  const own = page.getByRole('region', { name: 'Eigen account' });
  await expect(own.getByText(/wordt 15 op 10 mrt 2027/)).toBeVisible();
  await expect(own.getByRole('button', { name: 'Eigen account geven' })).toBeDisabled();
  await expect(page.getByRole('heading', { name: 'App-account' })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);

  await card.getByRole('button', { name: 'Goedkeuren' }).click();
  await expect(page.getByText('Sanne Mom is gekoppeld aan Piet.')).toBeVisible();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    path: '/admin/guardian-requests/gr-1/approve',
    body: { memberId: 'm-1' },
  });

  await card.getByRole('button', { name: '+ Ouder koppelen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Ouder koppelen aan Piet' });
  await dialog.getByLabel('Relatie').selectOption('Caregiver');
  await dialog.getByLabel('Zoek op naam of e-mail').fill('San');
  await dialog.getByRole('button', { name: 'Koppelen' }).click();
  await expect(page.getByText('Sanne Mom is gekoppeld aan Piet.')).toBeVisible();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    method: 'POST',
    path: '/admin/members/m-1/guardians',
    body: { userId: 'u-sanne', relationship: 'Caregiver' },
  });

  await card.getByRole('button', { name: 'Ontkoppelen' }).click();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    method: 'DELETE',
    path: '/admin/members/m-1/guardians/rel-1',
  });
});

test('fase 17: eigen account geven vanaf 15', async ({ page }) => {
  const api = new MockApi();
  child(api, { age: 15, canGetOwnAccount: true });
  await open(page, api, 'leden/m-1');

  await page.getByRole('button', { name: 'Eigen account geven' }).click();
  const dialog = page.getByRole('dialog', { name: 'Eigen account geven aan Piet' });
  await expect(dialog.getByText(/Robin Mom blijft gekoppeld tot Piet 18 wordt \(10 mrt 2030\)/)).toBeVisible();
  await dialog.getByLabel('E-mailadres van Piet').fill('piet@example.com');
  await expectNoSeriousA11yIssues(page);
  await dialog.getByRole('button', { name: 'Uitnodiging versturen' }).click();
  await expect(page.getByText('Piet krijgt een uitnodiging op piet@example.com.')).toBeVisible();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    path: '/admin/members/m-1/own-account',
    body: { email: 'piet@example.com' },
  });
});

test('fase 17: koppelverzoeken en voorstellen (zelfde e-mail) — niets gebeurt vanzelf', async ({ page }) => {
  const api = new MockApi();
  api.guardianRequests = [
    request,
    {
      ...request,
      id: 'gr-2',
      requestedByName: 'Mark Janssen',
      childFirstName: 'Fenna',
      childLastName: 'Janssen',
      candidates: [
        { memberId: 'm-7', name: 'Fenna Janssen', memberNumber: '1077', age: 9, guardians: 0 },
        { memberId: 'm-8', name: 'Fenna Janssen', memberNumber: '1102', age: 12, guardians: 0 },
      ],
    },
  ];
  api.guardianSuggestions = [
    {
      childMemberId: 'm-2',
      childName: 'Fenna Mom',
      childNumber: '1088',
      childAge: 9,
      childDansgarde: true,
      parentMemberId: 'm-9',
      parentName: 'Robin Mom',
      parentNumber: '1003',
      parentAge: 44,
      email: 'fam.mom@example.com',
      parentUserId: 'u-robin',
    },
    {
      childMemberId: 'm-3',
      childName: 'Tess Bakker',
      childNumber: '1090',
      childAge: 11,
      childDansgarde: true,
      parentMemberId: 'm-10',
      parentName: 'Anouk Bakker',
      parentNumber: '1012',
      parentAge: 39,
      email: 'bakker@example.com',
      parentUserId: null,
    },
  ];

  // Het dashboard wijst erop.
  await open(page, api);
  await expect(
    page.getByText('2 koppelverzoeken uit de app en 2 voorstellen (zelfde e-mail) wachten op beoordeling.'),
  ).toBeVisible();

  await page.goto('/beheer/koppelverzoeken');
  await expect(page.getByRole('heading', { name: 'Koppelverzoeken', level: 1 })).toBeVisible();
  const fenna = page.getByRole('row', { name: /Mark Janssen/ });
  await expect(fenna.getByRole('button', { name: 'Goedkeuren' })).toBeDisabled();
  await fenna.getByLabel('Kies het lid voor Fenna Janssen').selectOption('m-8');
  await fenna.getByRole('button', { name: 'Goedkeuren' }).click();
  await expect(page.getByText('Mark Janssen is gekoppeld aan Fenna Janssen.')).toBeVisible();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    path: '/admin/guardian-requests/gr-2/approve',
    body: { memberId: 'm-8' },
  });
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('tab', { name: /Voorstellen: zelfde e-mail/ }).click();
  await expect(page.getByRole('button', { name: 'Koppelen + uitnodigen' })).toBeVisible();
  await page
    .getByRole('row', { name: /Fenna Mom/ })
    .getByRole('button', { name: 'Geen relatie' })
    .click();
  expect(api.guardianCalls.at(-1)).toMatchObject({
    path: '/admin/guardian-suggestions/dismiss',
    body: { childMemberId: 'm-2', parentMemberId: 'm-9' },
  });
  await expect(page.getByRole('row', { name: /Fenna Mom/ })).toHaveCount(0);
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('tab', { name: 'Afgehandeld' }).click();
  await expect(page.getByText('Mark Janssen → Fenna Janssen')).toBeVisible();
});

test('fase 17: dansgarde — overzicht, indelen, melding aan dansgarde en dansgroepen', async ({ page }) => {
  const api = new MockApi([...new MockApi().permissions, 'notification.send']);
  await open(page, api);
  await openMenuIfMobile(page);
  await expect(page.getByRole('navigation').getByText('Dansgarde', { exact: true })).toBeVisible();
  await page.getByRole('navigation').getByRole('link', { name: 'Overzicht' }).click();

  await expect(page.getByRole('heading', { name: 'Dansgarde', level: 1 })).toBeVisible();
  await expect(page.getByText('Wordt 15 op 10 nov 2026')).toBeVisible();
  await page.getByRole('button', { name: 'Zonder groep (1)' }).click();
  await expect(page.getByRole('link', { name: 'Noor Smit' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Lot Mom' })).toHaveCount(0);
  await page.getByLabel('Dansgroep van Noor Smit').selectOption('dg-1');
  await expect
    .poll(() => api.guardianCalls.at(-1))
    .toMatchObject({ method: 'PUT', path: '/admin/dansgarde/m-3/group', body: { groupId: 'dg-1' } });
  await page.getByRole('button', { name: 'Alle (3)' }).click();
  await expectNoSeriousA11yIssues(page);

  await page.getByRole('link', { name: 'Melding aan dansgarde' }).click();
  await expect(page.getByRole('checkbox', { name: /Dansgarde \(iedereen met groep Dansgarde/ })).toBeChecked();

  await page.goto('/beheer/dansgarde/groepen');
  const mini = page.getByRole('region', { name: 'Mini Drammers' });
  await expect(mini.getByText('5 – 9 jaar · Leiding: Anja Smit')).toBeVisible();
  await expect(mini.getByText('Fenna Mom')).toBeVisible();
  await page.getByLabel('Naam').fill('Garde');
  await page.getByRole('button', { name: '+ Dansgroep toevoegen' }).click();
  await expect(page.getByText('Dansgroep Garde is aangemaakt.')).toBeVisible();
  expect(api.groups.at(-1)).toMatchObject({ name: 'Garde', type: 'DanceGuard' });
  await expectNoSeriousA11yIssues(page);
});
