import type { Page, Route } from '@playwright/test';

/** In-memory API voor de end-to-endtests; vorm gelijk aan het OpenAPI-contract. */
export class MockApi {
  permissions: string[];
  users = [
    {
      id: 'u-admin',
      email: 'bestuur@example.com',
      displayName: 'Test Bestuurder',
      accountStatus: 'Active',
      lastLoginAt: '2026-09-25T10:00:00Z',
    },
    {
      id: 'u-jan',
      email: 'jan@example.com',
      displayName: 'Jan Lid',
      accountStatus: 'Active',
      lastLoginAt: null as string | null,
    },
  ];
  roles = [
    {
      id: 1,
      code: 'lid',
      name: 'Carnavalist',
      description: null as string | null,
      isSystem: true,
      permissions: ['news.read'],
    },
    {
      id: 10,
      code: 'redactie',
      name: 'Redactie',
      description: null as string | null,
      isSystem: false,
      permissions: ['news.manage'],
    },
    {
      id: 11,
      code: 'bestuur',
      name: 'Bestuur',
      description: null as string | null,
      isSystem: true,
      permissions: ['role.manage'],
    },
  ];
  userRoles: Record<string, { code: string; name: string; validFrom: string | null; validTo: string | null }[]> = {
    'u-admin': [{ code: 'bestuur', name: 'Bestuur', validFrom: null, validTo: null }],
    'u-jan': [{ code: 'lid', name: 'Carnavalist', validFrom: null, validTo: null }],
  };
  audit: {
    id: number;
    occurredAt: string;
    actorType: string;
    actorUserId: string | null;
    actorName: string | null;
    action: string;
    entityType: string;
    entityId: string;
    oldValues: string | null;
    newValues: string | null;
  }[] = [];
  appConfig = {
    minAppVersionIos: '1.0.0',
    minAppVersionAndroid: '1.0.0',
    recommendedAppVersion: '1.0.0',
    maintenanceMode: false,
    maintenanceMessage: null as string | null,
    supportEmail: null as string | null,
  };
  years = [
    {
      id: 1,
      name: '2026/2027',
      startDate: '2026-11-11',
      endDate: '2027-02-10',
      carnivalStartDate: '2027-02-06',
      carnivalEndDate: '2027-02-09',
      active: true,
    },
  ];

  events: {
    id: string;
    categoryId: number;
    title: string;
    summary: string | null;
    description: string | null;
    startAt: string;
    endAt: string | null;
    allDay: boolean;
    locationName: string | null;
    locationAddress: string | null;
    latitude: null;
    longitude: null;
    isHighlight: boolean;
    badgeText: string | null;
    publication: { visibility: string; audienceRoles: string[]; status: string; publishAt: string | null };
  }[] = [];
  albums = [
    {
      id: 'a-1',
      title: 'Optocht 2027',
      albumDate: '2027-02-07',
      description: null as string | null,
      eventId: null,
      coverPhotoId: null,
      publication: {
        visibility: 'Public',
        audienceRoles: [] as string[],
        status: 'Published',
        publishAt: null as string | null,
      },
      photos: [
        {
          id: 'p-1',
          processingStatus: 'Ready',
          hidden: false,
          caption: null,
          photographer: null,
          thumbnailUrl: null as string | null,
        },
      ],
    },
  ];

  members = [
    {
      id: 'm-1',
      memberNumber: '001',
      fullName: 'Piet van der Berg',
      email: 'piet@example.com',
      city: 'Loil',
      status: 'Active',
      syncState: 'InSync',
      joinYear: 1995 as number | null,
      hasAccount: false,
      localStatusOverride: null as string | null,
    },
    {
      id: 'm-2',
      memberNumber: '002',
      fullName: 'Anna Jansen',
      email: 'anna@example.com',
      city: 'Didam',
      status: 'Active',
      syncState: 'Missing',
      joinYear: null as number | null,
      hasAccount: false,
      localStatusOverride: null as string | null,
    },
  ];
  syncJobs: Record<string, unknown>[] = [];

  // Fase 9b-2: AVG-verzoeken.
  privacyRequests: Record<string, unknown>[] = [
    {
      id: 'pr-1',
      type: 'Export',
      status: 'Completed',
      userId: 'u-jan',
      subjectName: 'Jan Lid',
      requestedByBoard: null,
      requestedAt: '2026-09-26T10:00:00Z',
      completedAt: '2026-09-26T10:00:01Z',
      downloadableUntil: '2026-09-27T10:00:00Z',
    },
  ];
  erased: string[] = [];
  // Fase 10: pushmeldingen.
  notifications: {
    id: string;
    title: string;
    body: string;
    category: string;
    status: string;
    createdAt: string;
    scheduledAt: string | null;
    sentAt: string | null;
    senderName: string | null;
    sourceType: string | null;
    recipientCount: number;
    pushCount: number;
    deliveredCount: number;
    failedCount: number;
    readCount: number;
    deepLink: string | null;
    audience: Record<string, unknown>;
  }[] = [];
  urgent = true;
  // Fase 11: optocht.
  parades: Record<string, unknown>[] = [];
  // Fase 12c: voorbeeld van een startnummerimport.
  importPreview: {
    version: number;
    rows: number;
    changes: {
      registrationNumber: number;
      groupName: string;
      oldStartNumber: number | null;
      newStartNumber: number | null;
      published: boolean;
    }[];
    errors: { row: number; message: string }[];
  } = { version: 4, rows: 2, changes: [], errors: [] };
  reviews: { action: string; reason: string | null }[] = [];
  lineupCalls: { path: string; body: unknown }[] = [];
  // Fase 17: ouders/verzorgers en dansgarde.
  guardianCalls: { method: string; path: string; body: unknown }[] = [];
  memberGuardians: Record<string, Record<string, unknown>> = {};
  guardianSuggestions: Record<string, unknown>[] = [];
  guardianRequests: Record<string, unknown>[] = [];
  dansgarde = {
    total: 3,
    withoutGroup: 1,
    withoutGuardian: 1,
    turningFifteenSoon: 1,
    groups: [
      { id: 'dg-1', name: 'Mini Drammers' },
      { id: 'dg-2', name: 'Drammerinekes' },
    ],
    members: [
      {
        memberId: 'm-1',
        fullName: 'Lot Mom',
        memberNumber: '1042',
        birthDate: '2012-03-10',
        age: 14,
        danceGroup: { id: 'dg-2', name: 'Drammerinekes' } as { id: string; name: string } | null,
        guardians: ['Robin Mom'],
        hasSuggestion: false,
        ownAccount: false,
        turnsFifteenOn: '2026-11-10',
      },
      {
        memberId: 'm-2',
        fullName: 'Fenna Mom',
        memberNumber: '1088',
        birthDate: '2017-05-01',
        age: 9,
        danceGroup: { id: 'dg-1', name: 'Mini Drammers' } as { id: string; name: string } | null,
        guardians: [] as string[],
        hasSuggestion: true,
        ownAccount: false,
        turnsFifteenOn: null,
      },
      {
        memberId: 'm-3',
        fullName: 'Noor Smit',
        memberNumber: '1101',
        birthDate: '2019-01-01',
        age: 7,
        danceGroup: null as { id: string; name: string } | null,
        guardians: ['Joep Smit'],
        hasSuggestion: false,
        ownAccount: false,
        turnsFifteenOn: null,
      },
    ],
  };
  // Fase 14: toegangscontrole.
  accessInside: string | null = null;
  checkIns: { memberId: string; force: boolean }[] = [];
  // Fase 13: ledentickets.
  ticketCalls: { path: string; body: unknown }[] = [];
  tickets = [
    {
      id: 't-1',
      memberId: 'm-1',
      memberName: 'Piet Lid',
      memberNumber: '1001',
      membershipActive: true,
      status: 'Active',
      blockedReason: null as string | null,
      credentialVersion: 1,
      boundDeviceName: 'iPhone 15',
      deviceSecurityLevel: 'SecureEnclave',
      boundAt: '2026-12-01T10:00:00Z',
      rebindCount: 1,
      createdAt: '2026-11-11T10:00:00Z',
    },
  ];
  // Fase 12b: samenstellen.
  compositionVersion = 3;
  compositionOrder: string[] = ['c-1'];
  compositionConflict = false;
  compositionCards = [
    {
      id: 'c-1',
      registrationNumber: 1,
      startNumber: 1,
      groupName: 'Groep A',
      categoryName: 'Praalwagens',
      youth: false,
      hasVehicle: true,
    },
    {
      id: 'c-2',
      registrationNumber: 2,
      startNumber: null,
      groupName: 'Groep B',
      categoryName: 'Loopgroep groot',
      youth: false,
      hasVehicle: false,
    },
    {
      id: 'c-3',
      registrationNumber: 3,
      startNumber: null,
      groupName: 'Groep C',
      categoryName: 'Jeugd',
      youth: true,
      hasVehicle: false,
    },
  ].map((c) => ({
    ...c,
    subject: null,
    participants: 12,
    lengthMeters: 10,
    lengthMeasured: false,
    additionalInformation: null as string | null,
    status: 'Approved',
    paradeOrder: null as number | null,
  }));
  /** Fase 12a: een ander groep heeft startnummer 17 (voor "bezet" en "Wisselen"). */
  takenStartNumber = 17;
  registration = {
    id: 'r-1',
    registrationNumber: 1,
    startNumber: null as number | null,
    measuredLengthMeters: null as number | null,
    status: 'Submitted',
    source: 'WebForm',
    groupName: 'De Bouwers',
    contactName: 'Piet Test',
    contactPhone: '+31612345678',
    contactEmail: 'piet@example.com',
    categoryName: 'Volwassenen Loopgroepen groot (10+)',
    subject: 'Zwerm bijen',
    subjectDescription: null,
    childrenCount: 2,
    adultCount: 12,
    hasMusic: true as boolean | null,
    buildAddress: { street: 'Dorpsstraat', houseNumber: '1', addition: null, postalCode: '6999 AA', city: 'Loil' },
    juryInspectionSameAsBuildAddress: true,
    juryAddress: { street: null, houseNumber: null, addition: null, postalCode: null, city: null },
    estimatedLengthMeters: 20,
    additionalInformation: null as string | null,
    submittedAt: '2026-12-02T10:00:00Z',
    managers: [] as string[],
    warnings: [] as string[],
    allowedActions: ['StartReview', 'Approve', 'Reject', 'RequestInformation'],
    statusHistory: [
      {
        fromStatus: 'Draft',
        toStatus: 'Submitted',
        occurredAt: '2026-12-02T10:00:00Z',
        actorName: null as string | null,
        reason: null as string | null,
      },
    ],
    changes: [] as unknown[],
    documents: [] as unknown[],
  };
  paradeCategories = [
    {
      id: 3,
      code: 'ADULT_WALK_L',
      name: 'Volwassenen Loopgroepen groot (10+)',
      ageGroup: 'Adult',
      type: 'WalkingGroupLarge',
      minimumParticipants: 10,
      maximumParticipants: null as number | null,
      participantCountBasis: 'AdultsOnly',
      validationMode: 'Block',
      hasVehicle: false,
      active: true,
      sortOrder: 30,
    },
    {
      id: 4,
      code: 'ADULT_WALK_S',
      name: 'Volwassenen Loopgroepen klein (3-9)',
      ageGroup: 'Adult',
      type: 'WalkingGroupSmall',
      minimumParticipants: 3,
      maximumParticipants: 9 as number | null,
      participantCountBasis: 'AdultsOnly',
      validationMode: 'Block',
      hasVehicle: false,
      active: true,
      sortOrder: 40,
    },
  ];
  testAccess: Record<string, string | null> = {};
  testAccessAvailable = true;
  excluded: { memberNumber: string; excludedAt: string; excludedBy: string | null }[] = [];

  // Fase 9b: aanmeldingen (lid worden).
  applications = [
    {
      id: 'ap-1',
      status: 'Submitted',
      source: 'App',
      firstName: 'Sanne',
      namePrefix: null as string | null,
      lastName: 'Jansen',
      fullName: 'Sanne Jansen',
      gender: 'v',
      birthDate: '2018-03-12',
      age: 8,
      minor: true,
      addressLine: 'Dorpsstraat 3',
      postalCode: '6999 AB',
      city: 'Loil',
      email: 'ouder@example.com',
      phone: null as string | null,
      guardianName: 'Anja Jansen' as string | null,
      guardianPhone: '0612345678' as string | null,
      ibanMasked: 'NL** **** **** 4300' as string | null,
      accountHolder: 'A. Jansen' as string | null,
      mandateReference: 'DVD-0123456789ABCDEF0123',
      mandateConsentAt: '2026-09-27T08:00:00Z',
      consentPrivacyAt: '2026-09-27T08:00:00Z',
      consentPhoto: true,
      submittedAt: '2026-09-27T08:05:00Z' as string | null,
      handledBy: null as string | null,
      handledAt: null as string | null,
      decisionAt: null as string | null,
      rejectionReason: null as string | null,
      internalNotes: null as string | null,
      resultingMemberId: null as string | null,
      provisioning: null as Record<string, unknown> | null,
      emailInUseBy: null as string | null,
    },
  ];

  // Fase 9: accountverzoeken, provisioning en apparaten.
  accountRequests = [
    {
      id: 'r-1',
      memberNumber: '001',
      email: 'piet.oud@example.com',
      status: 'Pending',
      mismatchReason: 'email-mismatch' as string | null,
      rejectionReason: null as string | null,
      requestedAt: '2026-09-26T08:00:00Z',
      decidedAt: null as string | null,
      member: {
        id: 'm-1',
        memberNumber: '001',
        fullName: 'Piet van der Berg',
        email: 'piet@example.com',
        status: 'Active',
      } as Record<string, unknown> | null,
    },
    {
      id: 'r-2',
      memberNumber: '999',
      email: 'onbekend@example.com',
      status: 'Pending',
      mismatchReason: 'unknown-member-number' as string | null,
      rejectionReason: null as string | null,
      requestedAt: '2026-09-26T07:00:00Z',
      decidedAt: null as string | null,
      member: null as Record<string, unknown> | null,
    },
  ];
  provisioning: Record<string, unknown>[] = [
    {
      id: 'p-9',
      sourceType: 'Manual',
      step: 'Pending',
      memberId: 'm-2',
      memberName: 'Anna Jansen',
      memberNumber: '002',
      attempts: 1,
      lastError: 'Graph-aanroep mislukt (503)',
      createdAt: '2026-09-26T06:00:00Z',
      completedAt: null,
    },
  ];
  devices = [
    {
      id: 'd-1',
      name: 'iPhone 15',
      platform: 'Ios',
      model: 'iPhone 15',
      appVersion: '1.0.0',
      status: 'Active',
      createdAt: '2026-09-20T10:00:00Z',
      lastSeenAt: '2026-09-26T09:00:00Z',
      current: false,
    },
  ];
  conflicts = [
    {
      id: 'c-1',
      syncJobId: 'j-0',
      type: 'EmailChangedForActiveAccount',
      memberNumber: '001',
      memberId: 'm-1',
      details: 'Het e-mailadres is gewijzigd in e-Boekhouden, terwijl dit lid een actief app-account heeft.',
      status: 'Open',
      createdAt: '2026-09-25T10:00:00Z',
      resolvedAt: null as string | null,
      resolutionNote: null as string | null,
    },
  ];
  groups = [
    {
      id: 'g-1',
      name: 'Jeugdcommissie',
      description: null as string | null,
      type: 'Committee',
      carnivalYearId: null as number | null,
      active: true,
      members: [] as {
        memberId: string;
        memberNumber: string;
        fullName: string;
        function: string;
        validFrom: string | null;
        validTo: string | null;
      }[],
    },
  ];
  mapping = {
    birthDate: null as string | null,
    joinYear: 'freeText2' as string | null,
    status: null as string | null,
    category: null as string | null,
    inactiveStatusValues: ['opgezegd'],
  };

  constructor(
    permissions: string[] = [
      'report.view',
      'role.manage',
      'config.manage',
      'audit.read',
      'event.manage',
      'news.manage',
      'photo.manage',
      'member.read',
      'member.update',
      'member.export',
      'member.purge',
      'member.approve',
      'member.block',
      'member.privacy',
      'import.run',
    ],
  ) {
    this.permissions = permissions;
  }

  private record(action: string, entityType: string, entityId: string, newValues: unknown) {
    this.audit.unshift({
      id: this.audit.length + 1,
      occurredAt: new Date().toISOString(),
      actorType: 'User',
      actorUserId: 'u-admin',
      actorName: 'Test Bestuurder',
      action,
      entityType,
      entityId,
      oldValues: null,
      newValues: JSON.stringify(newValues),
    });
  }

  async install(page: Page) {
    await page.route('**/api/v1/**', (route) => this.handle(route));
  }

  private async handle(route: Route) {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace('/api/v1', '');
    const method = request.method();
    // Multipart-uploads (bijv. een import) zijn geen JSON.
    const isJson = (request.headers()['content-type'] ?? '').includes('json');
    const body = isJson && request.postData() ? (JSON.parse(request.postData()!) as Record<string, unknown>) : {};
    const json = (data: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
    const noContent = () => route.fulfill({ status: 204 });
    let m: RegExpMatchArray | null;

    if (path === '/me') {
      return json({
        id: 'u-admin',
        email: 'bestuur@example.com',
        displayName: 'Test Bestuurder',
        memberId: null,
        roles: [{ code: 'bestuur', name: 'Bestuur' }],
        permissions: this.permissions,
        features: {},
      });
    }
    if (path === '/admin/dashboard') {
      return json({
        activeUsers: this.users.length,
        blockedUsers: 0,
        carnivalYear: this.years.find((y) => y.active)?.name ?? null,
        daysUntilCarnival: 134,
        outboxBacklog: 0,
        lastHeartbeat: '2026-09-25T10:00:00Z',
        systemStatus: 'Healthy',
        checks: [
          { name: 'sql', status: 'Healthy' },
          { name: 'worker', status: 'Healthy' },
        ],
      });
    }
    if (path === '/admin/users' && method === 'GET') {
      return json({ items: this.users, page: 1, pageSize: 25, totalCount: this.users.length });
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)$/))) {
      return json(this.users.find((u) => u.id === m![1]));
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)\/roles$/))) {
      if (method === 'PUT') {
        const roles = (body.roles as { roleCode: string; validFrom: string | null; validTo: string | null }[]).map(
          (r) => ({
            code: r.roleCode,
            name: this.roles.find((x) => x.code === r.roleCode)!.name,
            validFrom: r.validFrom,
            validTo: r.validTo,
          }),
        );
        this.userRoles[m[1]!] = roles;
        this.record('user.roles.changed', 'User', m[1]!, body.roles);
        return noContent();
      }
      return json(this.userRoles[m[1]!] ?? []);
    }
    if (path === '/admin/roles') {
      return json(this.roles);
    }
    if (path === '/admin/permissions') {
      return json([
        { code: 'news.read', description: 'Ledennieuws bekijken', category: 'Content' },
        { code: 'news.manage', description: 'Nieuws beheren en publiceren', category: 'Content' },
        { code: 'role.manage', description: 'Rollen beheren', category: 'Beheer' },
      ]);
    }
    if (path === '/admin/audit-log') {
      const action = url.searchParams.get('action') ?? '';
      const items = this.audit.filter((a) => a.action.startsWith(action));
      return json({ items, page: 1, pageSize: 25, totalCount: items.length });
    }
    if (path === '/admin/config/app-config') {
      if (method === 'PUT') {
        this.appConfig = body as typeof this.appConfig;
        this.record('config.app-config.changed', 'AppConfiguration', 'app-config', body);
        return noContent();
      }
      return json(this.appConfig);
    }
    if (path === '/admin/config/feature-flags') {
      return json([{ key: 'nieuws.push', enabled: false, description: 'Push bij nieuws', hasAudience: false }]);
    }
    if (path === '/admin/config/retention') {
      return json([{ dataType: 'login_history', retentionDays: 365, action: 'Delete' }]);
    }
    if (path === '/admin/parades') {
      if (method === 'POST') {
        const id = `p-${this.parades.length + 1}`;
        this.parades.push({ ...(body as Record<string, unknown>), id, fixedEntries: [] });
        return json({ id }, 201);
      }
      return json(this.parades);
    }
    if ((m = path.match(/^\/admin\/parades\/([^/]+)\/fixed-entries$/)) && method === 'PUT') {
      const parade = this.parades.find((p) => p.id === m![1])!;
      parade.fixedEntries = (body as { entries: unknown[] }).entries;
      this.lineupCalls.push({ path, body });
      return noContent();
    }
    if (path === '/admin/parade/export') {
      this.lineupCalls.push({ path, body: null });
      return route.fulfill({
        status: 200,
        headers: {
          'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          'content-disposition': "attachment; filename*=UTF-8''Opgaven%20optocht%202027%2029-9-2026.xlsx",
        },
        body: 'xlsx',
      });
    }
    if (path === '/admin/parade/start-numbers/import/preview') {
      this.lineupCalls.push({ path, body: null });
      return json(this.importPreview);
    }
    if (path === '/admin/parade/start-numbers/import') {
      this.lineupCalls.push({ path, body: null });
      return json({ changed: this.importPreview.changes.length });
    }
    if ((m = path.match(/^\/admin\/parades\/([^/]+)$/)) && method === 'PUT') {
      const index = this.parades.findIndex((p) => p.id === m![1]);
      this.parades[index] = { ...(body as Record<string, unknown>), id: m[1] };
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/access$/))) {
      return json({
        current: {
          id: 'ev-1',
          title: 'Carnavalsavond',
          startAt: '2027-02-13T19:00:00Z',
          endAt: '2027-02-14T01:00:00Z',
        },
        inside: this.accessInside !== null,
        insideSince: this.accessInside,
        ticketProblem: null,
        history: this.accessInside
          ? [
              {
                at: this.accessInside,
                method: 'Manual',
                outcome: 'Admitted',
                decision: null,
                eventTitle: 'Carnavalsavond',
                operator: 'Test Bestuurder',
              },
            ]
          : [],
      });
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/check-in$/)) && method === 'POST') {
      const force = (body as { force: boolean }).force;
      this.checkIns.push({ memberId: m[1]!, force });
      if (this.accessInside && !force) {
        return json({
          scanId: null,
          outcome: 'Warning',
          title: 'Al binnen',
          message: 'Al binnen.',
          holderName: null,
          previousAt: this.accessInside,
          needsDecision: true,
          counts: { inside: 1, scans: 1, refused: 0 },
        });
      }
      this.accessInside ??= '2027-02-13T19:58:00Z';
      return json({
        scanId: 's-1',
        outcome: this.checkIns.length > 1 ? 'AdmittedAgain' : 'Admitted',
        title: 'Ingecheckt',
        message: 'Piet van der Berg is ingecheckt om 20:58.',
        holderName: 'Piet van der Berg',
        previousAt: null,
        needsDecision: false,
        counts: { inside: 1, scans: this.checkIns.length, refused: 0 },
      });
    }
    if (path.startsWith('/admin/access-stats')) {
      const stats = (key: string, title: string, startAt: string, inside: number) => ({
        moment: { key, eventId: null, carnivalDay: null, title, startAt, endAt: null },
        activeMembers: 120,
        inside,
        scans: inside + 7,
        repeatsSameDevice: 4,
        repeatsOtherDevice: 1,
        refused: 2,
        viaQr: inside - 5,
        viaCheckIn: 5,
        offlineScans: 3,
        offlineConflicts: 1,
        perHour: [
          { hourStart: '2027-02-13T19:00:00Z', arrivals: 30, scans: 32 },
          { hourStart: '2027-02-13T20:00:00Z', arrivals: inside - 30, scans: inside - 25 },
        ],
        refusalReasons: [
          { reason: 'Expired', count: 1 },
          { reason: 'MembershipInactive', count: 1 },
        ],
      });
      const overview = [
        stats('dag-2027-02-13', 'Carnaval · zaterdag 13 februari', '2027-02-12T23:00:00Z', 90),
        stats('ev-1', 'Pronkzitting', '2027-01-16T19:00:00Z', 60),
      ];
      if (path === '/admin/access-stats/dashboard') {
        return json({
          live: true,
          stats: overview[0],
          readiness: { activeMembers: 120, bound: 97, boundWithHardwareKey: 90, notBound: 23 },
        });
      }
      if (path === '/admin/access-stats/overview') return json(overview);
      return json(overview.find((o) => o.moment.key === url.searchParams.get('key')) ?? overview[0]);
    }
    if (path === '/admin/access-scans/events') {
      return json([
        {
          key: 'ev-1',
          title: 'Carnavalsavond',
          startAt: '2027-02-13T19:00:00Z',
          endAt: '2027-02-14T01:00:00Z',
          counts: { inside: 1, scans: 2, refused: 1 },
        },
      ]);
    }
    if (path === '/admin/access-scans') {
      return json({
        items: [
          {
            id: 's-2',
            scannedAt: '2027-02-13T20:10:00Z',
            memberName: null,
            method: 'Qr',
            outcome: 'Refused',
            reason: 'Expired',
            decision: null,
            operatorName: 'Marieke',
            deviceName: 'Pixel 8',
            offline: true,
            offlineOutcome: 'Admitted',
          },
          {
            id: 's-1',
            scannedAt: '2027-02-13T19:58:00Z',
            memberName: 'Piet van der Berg',
            method: 'Manual',
            outcome: 'Admitted',
            reason: null,
            decision: null,
            operatorName: 'Jan',
            deviceName: null,
          },
        ],
        page: 1,
        pageSize: 25,
        totalCount: 2,
      });
    }
    if (path === '/admin/tickets') {
      return json({ items: this.tickets, page: 1, pageSize: 25, totalCount: this.tickets.length });
    }
    if (path === '/admin/tickets/issue' && method === 'POST') {
      this.ticketCalls.push({ path, body: null });
      return json({ issued: 12 });
    }
    if ((m = path.match(/^\/admin\/tickets\/([^/]+)\/action$/)) && method === 'POST') {
      const request = body as { action: string; reason: string | null };
      this.ticketCalls.push({ path, body: request });
      const ticket = this.tickets.find((t) => t.id === m![1])!;
      if (request.action === 'Block') {
        ticket.status = 'Blocked';
        ticket.blockedReason = request.reason;
      }
      if (request.action === 'Unblock') ticket.status = 'Active';
      if (request.action === 'ResetRebinds') ticket.rebindCount = 0;
      if (request.action === 'Reissue') ticket.credentialVersion += 1;
      return noContent();
    }
    if (path === '/admin/parade-composition') {
      const ordered = this.compositionOrder.map((id) => this.compositionCards.find((c) => c.id === id)!);
      return json({
        version: this.compositionVersion,
        ordered,
        unassigned: this.compositionCards.filter((c) => !this.compositionOrder.includes(c.id)),
        participants: ordered.length * 12,
        lengthMeters: ordered.length * 15,
        defaultSpacingMeters: 5,
        categories: [],
        warnings: [],
        fixedEntries: ['Geluidswagen', 'Verenigingswagen'],
        firstStartNumber: 3,
      });
    }
    if (path === '/admin/parade-composition/order' && method === 'PUT') {
      const request = body as { version: number; orderedIds: string[] };
      this.lineupCalls.push({ path, body: request });
      if (this.compositionConflict || request.version !== this.compositionVersion) {
        return json(
          {
            status: 412,
            code: 'REGISTRATION_CHANGED',
            detail: 'De volgorde is intussen door iemand anders gewijzigd.',
          },
          412,
        );
      }
      this.compositionOrder = request.orderedIds;
      this.compositionVersion += 1;
      return json({ version: this.compositionVersion });
    }
    if (path === '/admin/parade-composition/start-numbers/preview' && method === 'POST') {
      this.lineupCalls.push({ path, body });
      return json({
        version: this.compositionVersion,
        affectsPublished: (body as { mode: string }).mode === 'Renumber',
        changes: [
          {
            id: 'c-2',
            groupName: 'Groep B',
            registrationNumber: 2,
            oldStartNumber: null,
            newStartNumber: 2,
            published: false,
          },
        ],
      });
    }
    if (path === '/admin/parade-composition/start-numbers/apply' && method === 'POST') {
      this.lineupCalls.push({ path, body });
      this.compositionCards[1]!.startNumber = 2 as never;
      return json({ changed: 1 });
    }
    if (path.startsWith('/admin/parade-registrations')) {
      const r = this.registration;
      if (path === '/admin/parade-registrations/summary') {
        const approved = ['Approved', 'StartNumberAssigned'].includes(r.status) ? 1 : 0;
        return json({
          active: 1,
          approved,
          withStartNumber: approved && r.startNumber ? 1 : 0,
          published: r.status === 'StartNumberAssigned' ? 1 : 0,
          participants: r.adultCount + r.childrenCount,
          lineupLengthMeters: approved ? (r.measuredLengthMeters ?? r.estimatedLengthMeters) + 5 : 0,
          perStatus: { [r.status]: 1 },
          categories: [{ name: r.categoryName, registrations: 1, participants: 14, lengthMeters: 20 }],
        });
      }
      if (path === '/admin/parade-registrations/publish-start-numbers' && method === 'POST') {
        this.lineupCalls.push({ path, body: null });
        const published = r.status === 'Approved' && r.startNumber ? 1 : 0;
        if (published) r.status = 'StartNumberAssigned';
        return json({ published, withoutStartNumber: 0 });
      }
      if (path.endsWith('/start-number') && method === 'PUT') {
        const request = body as { startNumber: number | null; swap: boolean };
        this.lineupCalls.push({ path, body: request });
        if (request.startNumber === this.takenStartNumber && !request.swap) {
          return json(
            {
              status: 409,
              code: 'START_NUMBER_TAKEN',
              detail:
                'Startnummer 17 is al toegekend aan De Knotwilgen (opgavenummer 2). Kies Wisselen om de nummers om te ruilen.',
            },
            409,
          );
        }
        r.startNumber = request.startNumber;
        return noContent();
      }
      if (path.endsWith('/measured-length') && method === 'PUT') {
        const request = body as { measuredLengthMeters: number | null };
        this.lineupCalls.push({ path, body: request });
        r.measuredLengthMeters = request.measuredLengthMeters;
        return noContent();
      }
      if (path === '/admin/parade-registrations') {
        return json({
          items: [
            {
              ...r,
              hasWarnings: false,
              supplementReceived: r.statusHistory.at(-1)?.reason?.startsWith('Aanvulling ingediend') ?? false,
            },
          ],
          page: 1,
          pageSize: 25,
          totalCount: 1,
        });
      }
      if (path.endsWith('/review') && method === 'POST') {
        const review = body as { action: string; reason: string | null };
        this.reviews.push(review);
        const next: Record<string, string> = {
          StartReview: 'UnderReview',
          Approve: 'Approved',
          Reject: 'Rejected',
          RequestInformation: 'AdditionalInformationRequired',
          Reopen: 'UnderReview',
        };
        const from = r.status;
        r.status = next[review.action]!;
        r.statusHistory.push({
          fromStatus: from,
          toStatus: r.status,
          occurredAt: new Date().toISOString(),
          actorName: 'Commissie',
          reason: review.reason,
        });
        r.allowedActions =
          r.status === 'UnderReview'
            ? ['Approve', 'Reject', 'RequestInformation']
            : r.status === 'Approved'
              ? ['Reopen']
              : ['Reopen'];
        return json(r);
      }
      return json(r);
    }
    if (path === '/admin/parade-categories') {
      return json(this.paradeCategories);
    }
    if ((m = path.match(/^\/admin\/parade-categories\/(\d+)$/)) && method === 'PUT') {
      const index = this.paradeCategories.findIndex((c) => c.id === Number(m![1]));
      this.paradeCategories[index] = { ...(body as (typeof this.paradeCategories)[number]), id: Number(m[1]) };
      return json(this.paradeCategories[index]);
    }
    if (path === '/admin/carnival-years') {
      if (method === 'POST') {
        const year = {
          ...(body as Omit<(typeof this.years)[number], 'id' | 'active'>),
          id: this.years.length + 1,
          active: false,
        };
        this.years.push(year);
        return json(year, 201);
      }
      return json(this.years);
    }
    if ((m = path.match(/^\/admin\/carnival-years\/(\d+)\/activate$/))) {
      this.years.forEach((y) => (y.active = y.id === Number(m![1])));
      return noContent();
    }
    if (path === '/event-categories') {
      return json([
        { id: 1, code: 'carnaval', name: 'Carnaval' },
        { id: 2, code: 'jeugd', name: 'Jeugd' },
      ]);
    }
    if (path === '/admin/events') {
      if (method === 'POST') {
        const created = { ...(body as unknown as (typeof this.events)[number]), id: `e-${this.events.length + 1}` };
        this.events.push(created);
        this.record('event.created', 'Event', created.id, body);
        return json({ id: created.id }, 201);
      }
      return json(
        this.events.map((e) => ({
          id: e.id,
          title: e.title,
          startAt: e.startAt,
          visibility: e.publication.visibility,
          status: e.publication.status,
          publishAt: e.publication.publishAt,
          isHighlight: e.isHighlight,
        })),
      );
    }
    if ((m = path.match(/^\/admin\/events\/([^/]+)$/))) {
      const e = this.events.find((x) => x.id === m![1]);
      return e ? json({ ...e, imageUrl: null, attachments: [] }) : json({ status: 404 }, 404);
    }
    if (path === '/events') {
      const items = this.events.filter(
        (e) => e.publication.status === 'Published' && e.publication.visibility === 'Public',
      );
      return json({
        items: items.map((e) => ({ id: e.id, title: e.title })),
        page: 1,
        pageSize: 25,
        totalCount: items.length,
      });
    }
    if (path === '/admin/notifications/audience-options') {
      return json({
        anyAudience: true,
        urgent: this.urgent,
        roles: [
          { code: 'lid', name: 'Carnavalist' },
          { code: 'kaderlid', name: 'Kaderlid' },
        ],
        groups: [{ id: 'g-1', name: 'Dansgarde' }],
      });
    }
    if (path === '/admin/notifications/preview-audience' && method === 'POST') {
      const audience = body.audience as { everyone: boolean; members: boolean; roles: string[] };
      const accounts = audience.everyone ? 120 : audience.members ? 100 : 8;
      return json({ accounts, guests: audience.everyone ? 15 : 0, pushDevices: accounts - 20, optedOut: 3 });
    }
    if (path === '/admin/notifications' && method === 'POST') {
      const id = `n-${this.notifications.length + 1}`;
      const scheduled = (body.scheduledAt as string | null) ?? null;
      this.notifications.unshift({
        id,
        title: body.title as string,
        body: body.body as string,
        category: body.category as string,
        status: scheduled ? 'Scheduled' : 'Sent',
        createdAt: new Date().toISOString(),
        scheduledAt: scheduled,
        sentAt: scheduled ? null : new Date().toISOString(),
        senderName: 'Test Bestuurder',
        sourceType: null,
        recipientCount: scheduled ? 0 : 100,
        pushCount: scheduled ? 0 : 80,
        deliveredCount: 0,
        failedCount: 0,
        readCount: 0,
        deepLink: (body.deepLink as string | null) ?? null,
        audience: body.audience as Record<string, unknown>,
      });
      return json({ id }, 201);
    }
    if (path === '/admin/notifications') {
      const items = this.notifications.map((n) => {
        const summary: Partial<typeof n> = { ...n };
        delete summary.body;
        delete summary.deepLink;
        delete summary.audience;
        return summary;
      });
      return json({ items, page: 1, pageSize: 25, totalCount: items.length });
    }
    if ((m = path.match(/^\/admin\/notifications\/([^/]+)\/cancel$/))) {
      const n = this.notifications.find((x) => x.id === m![1])!;
      n.status = 'Canceled';
      return noContent();
    }
    if ((m = path.match(/^\/admin\/notifications\/([^/]+)$/))) {
      const n = this.notifications.find((x) => x.id === m![1]);
      if (!n) {
        return json({ status: 404, detail: 'Melding niet gevonden.', code: 'NOTIFICATION_NOT_FOUND' }, 404);
      }
      const { body: text, deepLink, audience, ...summary } = n;
      return json({
        summary,
        body: text,
        deepLink,
        audience,
        audienceLabels: audience.everyone
          ? ['Iedereen (ook gasten)']
          : audience.members
            ? ['Alle leden']
            : ['Rol: Kaderlid'],
        sourceType: null,
        sourceId: null,
        optedOut: 3,
        noDevice: 17,
      });
    }
    if (path === '/admin/news') {
      return json([]);
    }
    if (path === '/admin/photo-albums') {
      return json(
        this.albums.map((a) => ({
          id: a.id,
          title: a.title,
          albumDate: a.albumDate,
          visibility: a.publication.visibility,
          status: a.publication.status,
          photoCount: a.photos.length,
        })),
      );
    }
    if ((m = path.match(/^\/admin\/photo-albums\/([^/]+)$/))) {
      return json(this.albums.find((a) => a.id === m![1]));
    }
    if (path === '/admin/members/summary') {
      const active = this.members.filter((x) => (x.localStatusOverride ?? x.status) === 'Active').length;
      return json({
        active,
        inactive: this.members.length - active,
        suspended: 0,
        deceased: 0,
        missingInEBoekhouden: this.members.filter((x) => x.syncState === 'Missing').length,
        activeWithAccount: 0,
        lastSyncAt: '2026-09-26T01:00:00Z',
      });
    }
    if (path === '/carnival-years/current') {
      return json({
        id: 1,
        name: '2026/2027',
        startDate: '2026-11-11',
        endDate: '2027-02-10',
        carnivalStartDate: '2027-02-06',
        carnivalEndDate: '2027-02-09',
      });
    }
    if (path === '/admin/members' && method === 'GET') {
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const items = this.members
        .filter((x) => !search || x.fullName.toLowerCase().includes(search) || x.memberNumber === search)
        .map((x) => ({ ...x, status: x.localStatusOverride ?? x.status }));
      return json({ items, page: 1, pageSize: 50, totalCount: items.length });
    }
    if (path === '/admin/members/purge' && method === 'POST') {
      if (body.confirmation !== 'LEDEN VERWIJDEREN') {
        return json(
          {
            status: 422,
            title: 'Ongeldig',
            detail: 'Typ ter bevestiging "LEDEN VERWIJDEREN".',
            code: 'VALIDATION_FAILED',
          },
          422,
        );
      }
      const result = { members: this.members.length, syncJobs: this.syncJobs.length, unlinkedAccounts: 0 };
      this.members = [];
      this.syncJobs = [];
      this.conflicts = [];
      this.record('member.purged', 'Member', '*', result);
      return json(result);
    }
    if (path === '/admin/members/import' && method === 'POST') {
      const dryRun = url.searchParams.get('dryRun') !== 'false';
      const id = `j-${this.syncJobs.length + 1}`;
      this.syncJobs.unshift({
        id,
        status: 'Succeeded',
        dryRun,
        trigger: 'Manual',
        requestedAt: new Date().toISOString(),
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
      return json({ id }, 202);
    }
    if (path === '/admin/privacy-requests') {
      return json({ items: this.privacyRequests, page: 1, pageSize: 25, totalCount: this.privacyRequests.length });
    }
    if ((m = path.match(/^\/admin\/privacy-requests\/([^/]+)\/download$/))) {
      return json({ id: m[1], expiresAt: '2026-09-27T10:00:00Z', downloadUrl: 'about:blank' });
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)\/privacy-export$/))) {
      this.privacyRequests.unshift({
        id: 'pr-new',
        type: 'Export',
        status: 'Completed',
        userId: m[1],
        subjectName: 'Jan Lid',
        requestedByBoard: 'Test Bestuurder',
        requestedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        downloadableUntil: new Date(Date.now() + 86_400_000).toISOString(),
      });
      return json({
        id: 'pr-new',
        expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
        downloadUrl: 'about:blank',
      });
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)\/erase$/))) {
      if (body.confirmation !== 'WISSEN') {
        return json({ status: 422, detail: "Typ 'WISSEN' om de gegevens te wissen.", code: 'VALIDATION_FAILED' }, 422);
      }
      this.erased.push(m[1]!);
      const user = this.users.find((u) => u.id === m![1])!;
      user.accountStatus = 'Deleted';
      return json({ applications: 0, accountRequests: 1, logins: 3, guardianRelations: 0, devices: 1 });
    }
    if (path === '/admin/membership-applications') {
      const status = url.searchParams.get('status');
      const items = this.applications
        .filter((a) => !status || a.status === status)
        .map((a) => ({
          id: a.id,
          fullName: a.fullName,
          city: a.city,
          age: a.age,
          minor: a.minor,
          status: a.status,
          source: a.source,
          submittedAt: a.submittedAt,
        }));
      return json({ items, page: 1, pageSize: 25, totalCount: items.length });
    }
    if (
      (m = path.match(/^\/admin\/membership-applications\/([^/]+)(?:\/(start-review|approve|reject|notes|retry))?$/))
    ) {
      const application = this.applications.find((a) => a.id === m![1]);
      if (!application) {
        return json({ status: 404, title: 'Niet gevonden', code: 'APPLICATION_NOT_FOUND' }, 404);
      }
      switch (m[2]) {
        case undefined:
          return json(application);
        case 'start-review':
          application.status = 'InReview';
          application.handledBy = 'Test Bestuurder';
          return noContent();
        case 'approve':
          application.status = 'Activated';
          application.ibanMasked = null;
          application.accountHolder = null;
          application.resultingMemberId = 'm-1';
          application.provisioning = {
            id: 'p-a',
            step: 'Completed',
            memberNumber: 'SIM123456',
            attempts: 1,
            lastError: null,
          };
          return noContent();
        case 'reject':
          application.status = 'Rejected';
          application.rejectionReason = body.reason as string;
          application.ibanMasked = null;
          return noContent();
        case 'notes':
          application.internalNotes = (body.notes as string | null) ?? null;
          return noContent();
        default:
          return noContent();
      }
    }
    if (path === '/admin/account-requests') {
      const status = url.searchParams.get('status');
      const items = this.accountRequests.filter((r) => !status || r.status === status);
      return json({ items, page: 1, pageSize: 25, totalCount: items.length });
    }
    if ((m = path.match(/^\/admin\/account-requests\/([^/]+)\/(approve|reject)$/))) {
      const request = this.accountRequests.find((r) => r.id === m![1])!;
      request.status = m[2] === 'approve' ? 'Approved' : 'Rejected';
      request.decidedAt = new Date().toISOString();
      if (m[2] === 'approve') {
        this.provisioning.push({
          id: 'p-new',
          sourceType: 'AccountRequest',
          step: 'Pending',
          memberId: body.memberId,
          memberName: 'Piet van der Berg',
          memberNumber: '001',
          attempts: 0,
          lastError: null,
          createdAt: request.decidedAt,
          completedAt: null,
        });
      }
      this.record(
        `account-request.${m[2] === 'approve' ? 'approved' : 'rejected'}`,
        'AccountRequest',
        request.id,
        body,
      );
      return noContent();
    }
    if (path === '/admin/account-provisioning') {
      return json(this.provisioning);
    }
    if ((m = path.match(/^\/admin\/account-provisioning\/([^/]+)\/retry$/))) {
      this.provisioning = this.provisioning.filter((p) => p.id !== m![1]);
      return noContent();
    }
    if (path === '/admin/members/excluded' && method === 'GET') {
      return json(this.excluded);
    }
    if ((m = path.match(/^\/admin\/members\/excluded\/([^/]+)$/)) && method === 'DELETE') {
      this.excluded = this.excluded.filter((e) => e.memberNumber !== m![1]);
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/remove$/)) && method === 'POST') {
      if (body.confirmation !== 'VERWIJDEREN') {
        return json({ status: 422, detail: 'Typ ter bevestiging "VERWIJDEREN".', code: 'VALIDATION_FAILED' }, 422);
      }
      const member = this.members.find((x) => x.id === m![1])!;
      this.members = this.members.filter((x) => x !== member);
      this.excluded.unshift({
        memberNumber: member.memberNumber,
        excludedAt: new Date().toISOString(),
        excludedBy: 'Test Bestuurder',
      });
      return json({ memberNumber: member.memberNumber, accounts: member.hasAccount ? 1 : 0, applications: 0 });
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/account$/)) && method === 'DELETE') {
      const member = this.members.find((x) => x.id === m![1])!;
      member.hasAccount = false;
      this.record('member.account-removed', 'Member', member.id, {});
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/provision-account$/))) {
      const member = this.members.find((x) => x.id === m![1])!;
      this.provisioning.push({
        id: `p-${member.id}`,
        sourceType: 'Manual',
        step: 'Pending',
        memberId: member.id,
        memberName: member.fullName,
        memberNumber: member.memberNumber,
        attempts: 0,
        lastError: null,
        createdAt: new Date().toISOString(),
        completedAt: null,
      });
      return json({ provisioningId: `p-${member.id}` }, 202);
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)\/test-access$/))) {
      if (method === 'PUT') {
        this.testAccess[m[1]!] = body.granted ? 'dev,acc' : null;
      }
      const environments = this.testAccess[m[1]!] ?? null;
      return json({
        available: this.testAccessAvailable,
        environment: 'dev',
        grant: 'dev,acc',
        hasSignIn: true,
        inTestersGroup: environments !== null,
        environments,
        hasAccessHere: environments !== null,
      });
    }
    if ((m = path.match(/^\/admin\/users\/([^/]+)\/devices$/))) {
      return json(m[1] === 'u-jan' ? this.devices : []);
    }
    if ((m = path.match(/^\/admin\/devices\/([^/]+)\/revoke$/))) {
      const device = this.devices.find((d) => d.id === m![1])!;
      device.status = 'Revoked';
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/confirm-inactive$/))) {
      const member = this.members.find((x) => x.id === m![1])!;
      member.status = 'Inactive';
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)$/))) {
      const member = this.members.find((x) => x.id === m![1]);
      if (!member) {
        return json({ status: 404, title: 'Niet gevonden', code: 'MEMBER_NOT_FOUND' }, 404);
      }
      if (method === 'PATCH') {
        member.localStatusOverride = (body.localStatusOverride as string | null) ?? null;
        this.record('member.updated', 'Member', member.id, body);
        return noContent();
      }
      return json({
        id: member.id,
        memberNumber: member.memberNumber,
        ebMemberId: 1,
        fullName: member.fullName,
        firstName: member.fullName.split(' ')[0],
        namePrefix: null,
        lastName: member.fullName.split(' ').slice(1).join(' '),
        nameCorrectedManually: false,
        salutation: null,
        gender: 'm',
        addressLine: 'Dorpsstraat 1',
        postalCode: '6999 AA',
        city: member.city,
        country: 'NL',
        email: member.email,
        phone: null,
        mobilePhone: null,
        birthDate: null,
        joinYear: member.joinYear,
        ebStatusRaw: null,
        memberCategory: null,
        syncedStatus: member.status,
        localStatusOverride: member.localStatusOverride,
        effectiveStatus: member.localStatusOverride ?? member.status,
        membershipValidFrom: null,
        membershipValidTo: null,
        syncState: member.syncState,
        ebLastSeenAt: '2026-09-25T03:00:00Z',
        ebMissingSince: member.syncState === 'Missing' ? '2026-09-24T03:00:00Z' : null,
        fieldSources: {
          birthDateFromEBoekhouden: false,
          joinYearFromEBoekhouden: true,
          statusFromEBoekhouden: false,
          categoryFromEBoekhouden: false,
        },
        account: member.hasAccount
          ? {
              userId: 'u-jan',
              email: member.email,
              accountStatus: 'Active',
              lastLoginAt: '2026-09-26T09:00:00Z',
              awaitingFirstSignIn: false,
            }
          : null,
        groups: [{ groupId: 'g-1', name: 'Jeugdcommissie', function: 'Lead', validTo: null }],
        provisioning: (() => {
          const p = [...this.provisioning].reverse().find((x) => x.memberId === member.id);
          return p
            ? { id: p.id, step: p.step, attempts: p.attempts, lastError: p.lastError, createdAt: p.createdAt }
            : null;
        })(),
      });
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/(guardians|own-account)(\/.*)?$/))) {
      if (method !== 'GET') {
        this.guardianCalls.push({ method, path, body });
        return method === 'POST' && m[2] === 'own-account' ? json(null, 202) : noContent();
      }
      return json(
        this.memberGuardians[m[1]!] ?? {
          applies: false,
          max: 2,
          guardians: [],
          suggestions: [],
          requests: [],
          ownAccount: {
            hasAccount: true,
            email: null,
            age: 40,
            canGetOwnAccount: false,
            availableFrom: null,
            guardiansUntil: null,
            pending: false,
          },
        },
      );
    }
    if (path === '/admin/guardian-candidates') {
      return json([{ userId: 'u-sanne', name: 'Sanne Mom', email: 'sanne@example.com', isMember: false }]);
    }
    if (path === '/admin/guardian-requests') {
      const status = url.searchParams.get('status');
      return json(this.guardianRequests.filter((r) => !status || r.status === status));
    }
    if ((m = path.match(/^\/admin\/guardian-requests\/([^/]+)\/(approve|reject)$/))) {
      this.guardianCalls.push({ method, path, body });
      const request = this.guardianRequests.find((r) => r.id === m![1]);
      if (request) {
        request.status = m[2] === 'approve' ? 'Approved' : 'Rejected';
        request.decidedAt = '2026-09-29T12:00:00Z';
      }
      return noContent();
    }
    if (path === '/admin/guardian-suggestions') {
      return json(this.guardianSuggestions);
    }
    if (path === '/admin/guardian-suggestions/link' || path === '/admin/guardian-suggestions/dismiss') {
      this.guardianCalls.push({ method, path, body });
      const b = body as { childMemberId: string; parentMemberId: string };
      this.guardianSuggestions = this.guardianSuggestions.filter(
        (x) => !(x.childMemberId === b.childMemberId && x.parentMemberId === b.parentMemberId),
      );
      return noContent();
    }
    if (path === '/admin/dansgarde') {
      return json(this.dansgarde);
    }
    if (path === '/admin/dansgarde/groups') {
      return json({
        groups: this.dansgarde.groups.map((g) => ({
          id: g.id,
          name: g.name,
          description: g.id === 'dg-1' ? '5 – 9 jaar' : '10 – 14 jaar',
          active: true,
          leaders: g.id === 'dg-1' ? ['Anja Smit'] : [],
          members: this.dansgarde.members
            .filter((x) => x.danceGroup?.id === g.id)
            .map((x) => ({ memberId: x.memberId, fullName: x.fullName, age: x.age })),
        })),
        unassigned: this.dansgarde.members
          .filter((x) => !x.danceGroup)
          .map((x) => ({ memberId: x.memberId, fullName: x.fullName, age: x.age })),
      });
    }
    if ((m = path.match(/^\/admin\/dansgarde\/([^/]+)\/group$/)) && method === 'PUT') {
      this.guardianCalls.push({ method, path, body });
      const member = this.dansgarde.members.find((x) => x.memberId === m![1])!;
      const groupId = (body as { groupId: string | null }).groupId;
      member.danceGroup = this.dansgarde.groups.find((g) => g.id === groupId) ?? null;
      return noContent();
    }
    if (path === '/admin/sync-jobs') {
      return json({ items: this.syncJobs, page: 1, pageSize: 20, totalCount: this.syncJobs.length });
    }
    if ((m = path.match(/^\/admin\/sync-jobs\/([^/]+)\/items$/))) {
      const items = [
        { id: 1, memberNumber: '001', memberId: 'm-1', action: 'Updated', changedFields: 'email,city', message: null },
        { id: 2, memberNumber: '003', memberId: null, action: 'Created', changedFields: null, message: null },
      ];
      return json({ items, page: 1, pageSize: 50, totalCount: items.length });
    }
    if ((m = path.match(/^\/admin\/sync-jobs\/([^/]+)$/))) {
      return json(this.syncJobs.find((j) => j.id === m![1]));
    }
    if (path === '/admin/sync-conflicts') {
      return json(this.conflicts.filter((c) => c.status === 'Open'));
    }
    if ((m = path.match(/^\/admin\/sync-conflicts\/([^/]+)\/resolve$/))) {
      const conflict = this.conflicts.find((c) => c.id === m![1])!;
      conflict.status = body.resolution as string;
      return noContent();
    }
    if (path === '/admin/config/member-mapping') {
      if (method === 'PUT') {
        this.mapping = body as typeof this.mapping;
        return noContent();
      }
      return json(this.mapping);
    }
    if (path === '/admin/content-audiences/groups') {
      return json(this.groups.filter((g) => g.active).map((g) => ({ id: g.id, name: g.name })));
    }
    if (path === '/admin/groups' && method === 'GET') {
      return json(this.groups.map(({ members, ...g }) => ({ ...g, memberCount: members.length })));
    }
    if (path === '/admin/groups' && method === 'POST') {
      const group = {
        id: `g-${this.groups.length + 1}`,
        name: body.name as string,
        description: (body.description as string | null) ?? null,
        type: body.type as string,
        carnivalYearId: null,
        active: body.active !== false,
        members: [] as (typeof this.groups)[number]['members'],
      };
      this.groups.push(group);
      this.record('group.created', 'Group', group.id, body);
      return json({ id: group.id }, 201);
    }
    if ((m = path.match(/^\/admin\/groups\/([^/]+)\/members\/([^/]+)$/))) {
      const group = this.groups.find((g) => g.id === m![1])!;
      const member = this.members.find((x) => x.id === m![2])!;
      group.members = group.members.filter((x) => x.memberId !== member.id);
      if (method === 'PUT') {
        group.members.push({
          memberId: member.id,
          memberNumber: member.memberNumber,
          fullName: member.fullName,
          function: body.function as string,
          validFrom: null,
          validTo: (body.validTo as string | null) ?? null,
        });
      }
      return noContent();
    }
    if ((m = path.match(/^\/admin\/groups\/([^/]+)$/))) {
      const group = this.groups.find((g) => g.id === m![1]);
      if (!group) {
        return json({ status: 404, title: 'Niet gevonden', code: 'GROUP_NOT_FOUND' }, 404);
      }
      if (method === 'DELETE') {
        this.groups = this.groups.filter((g) => g.id !== group.id);
        return noContent();
      }
      return json(group);
    }
    if (path === '/admin/reports/members') {
      return json({
        total: 3,
        active: 2,
        byStatus: [
          { label: 'Actief', count: 2 },
          { label: 'Inactief', count: 1 },
        ],
        byRole: [{ label: 'Carnavalist', count: 1 }],
        byAgeClass: [
          { label: '0–11', count: 1 },
          { label: 'Onbekend', count: 1 },
        ],
        byJoinYear: [
          { label: '1995', count: 1 },
          { label: 'Onbekend', count: 1 },
        ],
        byGroup: [{ label: 'Jeugdcommissie', count: 1 }],
      });
    }
    return json({ status: 404, title: 'Not Found', code: 'NOT_FOUND' }, 404);
  }
}
