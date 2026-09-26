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
    const body = request.postData() ? (JSON.parse(request.postData()!) as Record<string, unknown>) : {};
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
        account: null,
        groups: [{ groupId: 'g-1', name: 'Jeugdcommissie', function: 'Lead', validTo: null }],
        provisioning: (() => {
          const p = [...this.provisioning].reverse().find((x) => x.memberId === member.id);
          return p
            ? { id: p.id, step: p.step, attempts: p.attempts, lastError: p.lastError, createdAt: p.createdAt }
            : null;
        })(),
      });
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
