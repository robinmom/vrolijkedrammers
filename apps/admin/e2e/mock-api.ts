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

  constructor(
    permissions: string[] = [
      'report.view',
      'role.manage',
      'config.manage',
      'audit.read',
      'event.manage',
      'news.manage',
      'photo.manage',
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
    return json({ status: 404, title: 'Not Found', code: 'NOT_FOUND' }, 404);
  }
}
