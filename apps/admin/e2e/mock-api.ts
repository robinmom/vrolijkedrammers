import type { Page, Route } from '@playwright/test';

/** 1×1 PNG als voorbeeldafbeelding. */
/** Adverteerder in de mock (fase 27b), met de stand van campagne 2027 en het bedrag van 2026. */
interface MockAdvertiser {
  id: string;
  number: number;
  companyName: string;
  contactName: string | null;
  phone: string | null;
  mobile: string | null;
  email: string | null;
  addressLine: string | null;
  postalCode: string | null;
  city: string | null;
  website: string | null;
  page: string | null;
  kind: string;
  payment: string;
  maskedIban: string | null;
  mandateReference: string | null;
  collectorMemberId: string | null;
  importedCollectorName: string | null;
  notes: string | null;
  active: boolean;
  addedViaApp: boolean;
  status2027: string;
  amount2026: number;
}

const PIXEL =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

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
      category: 'Parade',
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
      jubileeJoinYearOverride: null as number | null,
      jubileeNote: null as string | null,
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
      jubileeJoinYearOverride: null as number | null,
      jubileeNote: null as string | null,
    },
  ];
  syncJobs: Record<string, unknown>[] = [];
  jubileeMilestones = [11, 22, 33, 44, 55, 66, 77];
  // Fase 23c: SEPA-incasso.
  sepaCreditor = { name: null as string | null, iban: null as string | null, creditorId: null as string | null };
  collectionRuns: Record<string, unknown>[] = [];
  // Fase 26: wijzigingsverzoeken en verbreken.
  memberRequests = {
    changes: [
      {
        id: 'c-1',
        memberId: 'm-1',
        memberNumber: '001',
        fullName: 'Piet van der Berg',
        requestedAt: '2026-10-03T09:00:00Z',
        fields: [
          {
            field: 'address',
            label: 'Adres',
            current: 'Dorpsstraat 1' as string | null,
            requested: 'Kerkstraat 5' as string | null,
          },
          {
            field: 'iban',
            label: 'IBAN',
            current: null as string | null,
            requested: '**** 4300 (P. Berg), machtiging gegeven' as string | null,
          },
        ],
      },
    ],
    breaks: [
      {
        id: 'b-1',
        payerMemberId: 'm-1',
        payerName: 'Piet van der Berg',
        partnerMemberId: 'm-2',
        partnerName: 'Anna Jansen',
        initiatedBy: 'Anna Jansen',
        initiatedAt: '2026-10-02T09:00:00Z',
        status: 'AwaitingApproval',
        payerAgreedAt: '2026-10-02T10:00:00Z' as string | null,
        partnerAgreedAt: '2026-10-02T09:00:00Z' as string | null,
        partnerIbanMasked: '**** 1234' as string | null,
        partnerAccountHolder: 'A. Jansen' as string | null,
      },
    ],
  };
  // Fase 25: tweepersoonsleden splitsen.
  splitCandidates = [
    {
      memberId: 'm-1',
      memberNumber: '001',
      fullName: 'Piet van der Berg',
      email: 'piet@example.com' as string | null,
      secondMemberName: 'Marie van der Berg' as string | null,
      state: 'NotInvited',
      invitedAt: null as string | null,
      timesInvited: 0,
      applicationId: null as string | null,
    },
    {
      memberId: 'm-2',
      memberNumber: '002',
      fullName: 'Anna Jansen',
      email: null as string | null,
      secondMemberName: null as string | null,
      state: 'NotInvited',
      invitedAt: null as string | null,
      timesInvited: 0,
      applicationId: null as string | null,
    },
  ];
  // Fase 24: handmatig aangepaste velden per lid.
  memberLocalFields: Record<string, string[]> = {};
  jubileeInvitations: Record<string, string> = {};
  // Fase 23a: lidmaatschappen en contributie.
  contributionRates = [
    {
      id: 1,
      validFrom: '2026-01-01',
      onePerson: 32.5,
      twoPersons: 57.5,
      onePersonSenior: 22,
      twoPersonsSenior: 44,
      dansgarde: 85,
    },
  ];
  membershipSettings: Record<
    string,
    { kind: string | null; payerMemberId: string | null; exempt: boolean; exemptReason: string | null }
  > = {};
  jubileeTemplate = {
    subject: 'Uitnodiging: huldiging jubilarissen {carnavalsjaar}',
    body: 'Beste {voornaam},\n\nDit jaar ben je {jaren} jaar lid. Dit willen we niet zomaar voorbij laten gaan.\n\nMet vriendelijke groet,\n\nHet bestuur',
    replyTo: 'secretaris@vrolijkedrammers.nl',
  };

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
  compositionWarnings: string[] = [];
  // Fase 16: aanrijtijden.
  arrivals = {
    location: 'Rotonde Holthuizen' as string | null,
    publishedAt: null as string | null,
    paradeDate: '2027-02-07',
    paradeStartTime: '13:30:00',
    version: 1,
    rows: [
      {
        id: 'r-5',
        startNumber: 5,
        registrationNumber: 3,
        category: 'Getrokken wagens',
        groupName: 'De Snotapen',
        arrivalTime: null as string | null,
      },
      {
        id: 'r-7',
        startNumber: 7,
        registrationNumber: 8,
        category: 'Zelfrijdend voertuigen',
        groupName: 'De Sökkels',
        arrivalTime: null as string | null,
      },
    ],
  };
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
    errors: {
      row: number;
      message: string;
      advice?: string | null;
      registrationNumber?: string | null;
      startNumber?: string | null;
      groupName?: string | null;
    }[];
  } = { version: 4, rows: 2, changes: [], errors: [] };
  reviews: { action: string; reason: string | null }[] = [];
  lineupCalls: { path: string; body: unknown }[] = [];
  // Fase 19: kaartverkoop.
  salesCalls: { method: string; path: string; body: unknown }[] = [];
  saleProducts: Record<string, unknown>[] = [
    {
      id: 'sp-vr',
      kind: 'Pronkzitting',
      name: 'Pronkzitting vrijdag',
      description: null,
      eventId: null,
      date: '2027-02-05',
      priceCents: 1250,
      capacity: 300,
      maxPerOrder: 10,
      saleOpensAt: null,
      saleClosesAt: null,
      onSale: true,
      sortOrder: 0,
      sold: 300,
      held: 0,
      remaining: 0,
      revenueCents: 125000,
      waiting: 2,
    },
    {
      id: 'sp-za',
      kind: 'Pronkzitting',
      name: 'Pronkzitting zaterdag',
      description: null,
      eventId: null,
      date: '2027-02-06',
      priceCents: 1250,
      capacity: 300,
      maxPerOrder: 10,
      saleOpensAt: null,
      saleClosesAt: null,
      onSale: true,
      sortOrder: 1,
      sold: 250,
      held: 8,
      remaining: 42,
      revenueCents: 91250,
      waiting: 0,
    },
    {
      id: 'sp-dag',
      kind: 'DayTicket',
      name: 'Dagkaart zaterdag',
      description: null,
      eventId: null,
      date: '2027-02-13',
      priceCents: 750,
      capacity: 500,
      maxPerOrder: 10,
      saleOpensAt: null,
      saleClosesAt: null,
      onSale: true,
      sortOrder: 2,
      sold: 96,
      held: 0,
      remaining: 404,
      revenueCents: 72000,
      waiting: 0,
    },
    {
      id: 'sp-mu',
      kind: 'Tokens',
      name: 'Consumptiemunten',
      description: null,
      eventId: null,
      date: null,
      priceCents: 250,
      capacity: null,
      maxPerOrder: 100,
      saleOpensAt: null,
      saleClosesAt: null,
      onSale: true,
      sortOrder: 5,
      sold: 1120,
      held: 0,
      remaining: null,
      revenueCents: 280000,
      waiting: 0,
    },
  ];
  saleOrders: Record<string, unknown>[] = [
    {
      id: 'so-1',
      number: '2027-0142',
      productId: 'sp-za',
      productName: 'Pronkzitting zaterdag',
      status: 'AwaitingPayment',
      paymentMethod: 'Mollie',
      channel: 'Portal',
      groupName: null,
      memberQuantity: 0,
      paidQuantity: 2,
      amountCents: 2500,
      buyerName: 'Jan Jansen',
      buyerEmail: 'jan@example.com',
      buyerPhone: '0612345678',
      remark: null,
      buyerIsMember: false,
      createdAt: '2026-10-12T18:00:00Z',
      holdUntil: '2026-10-14T18:00:00Z',
      paidAt: null,
      collected: false,
    },
    {
      id: 'so-2',
      number: '2027-0101',
      productId: 'sp-mu',
      productName: 'Consumptiemunten',
      status: 'Confirmed',
      paymentMethod: 'Mollie',
      channel: 'App',
      groupName: null,
      memberQuantity: 0,
      paidQuantity: 20,
      amountCents: 5000,
      buyerName: 'Mendy Mom',
      buyerEmail: 'mendy@example.com',
      buyerPhone: null,
      remark: null,
      buyerIsMember: true,
      createdAt: '2026-10-10T18:00:00Z',
      holdUntil: null,
      paidAt: '2026-10-10T18:01:00Z',
      collected: false,
    },
  ];
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
  ] as {
    id: string;
    memberNumber: string | null;
    email: string;
    status: string;
    mismatchReason: string | null;
    rejectionReason: string | null;
    requestedAt: string;
    decidedAt: string | null;
    member: Record<string, unknown> | null;
    candidates?: Record<string, unknown>[];
    approvedMemberId?: string;
  }[];
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

  // Fase 21a: websitebeheer.
  websiteSettings = {
    heroEyebrow: 'CARNAVAL · LOIL' as string | null,
    heroTitle: 'Alaaf! Het feest komt eraan.',
    heroSubtitle: 'Pronkzitting, optocht en vier dagen feest.' as string | null,
    heroPrimaryLabel: 'Bekijk de agenda' as string | null,
    heroPrimaryLink: 'Agenda' as string | null,
    heroSecondaryLabel: 'Word lid' as string | null,
    heroSecondaryLink: 'Membership' as string | null,
    heroImageUrl: null as string | null,
    facebookPageUrl: 'https://www.facebook.com/vrolijkedrammers' as string | null,
    instagramUrl: null as string | null,
    showYouthPrinces: false,
  };
  // Fase 27a: mailings.
  mailingLists: {
    id: string;
    name: string;
    description: string | null;
    allMembers: boolean;
    memberIds: string[];
    groupIds: string[];
    addresses: { email: string; name: string | null }[];
  }[] = [
    {
      id: 'ml-1',
      name: 'Alle leden',
      description: 'Alle actieve leden met een e-mailadres (nieuwsbrief).',
      allMembers: true,
      memberIds: [],
      groupIds: [],
      addresses: [],
    },
  ];
  mailings: {
    id: string;
    kind: 'Newsletter' | 'Invitation';
    subject: string;
    preheader: string | null;
    blocks: Record<string, unknown>[];
    listIds: string[];
    status: 'Draft' | 'Sending' | 'Sent';
    sentAt: string | null;
    recipientCount: number;
  }[] = [];
  mailingTests = 0;
  // Fase 27b: adverteerders.
  advertiserCollectors = [{ memberId: 'm-1', name: 'Piet van der Berg' }];
  advertisers: MockAdvertiser[] = [
    {
      id: 'adv-1',
      number: 1,
      companyName: 'Bakkerij De Test',
      contactName: 'Jan Test',
      phone: null as string | null,
      mobile: null as string | null,
      email: 'bakker@example.com',
      addressLine: 'Dorpsstraat 1',
      postalCode: '6941 XX',
      city: 'Loil',
      website: null as string | null,
      page: '4',
      kind: 'Advertisement',
      payment: 'Mandate',
      maskedIban: '**** 4300' as string | null,
      mandateReference: 'DVD000000001' as string | null,
      collectorMemberId: 'm-1' as string | null,
      importedCollectorName: null as string | null,
      notes: null as string | null,
      active: true,
      addedViaApp: false,
      status2027: 'Open',
      amount2026: 35,
    },
    {
      id: 'adv-2',
      number: 2,
      companyName: 'Garage Proef',
      contactName: null,
      phone: null,
      mobile: null,
      email: null,
      addressLine: null,
      postalCode: null,
      city: 'Didam',
      website: null,
      page: null,
      kind: 'Gift',
      payment: 'Cash',
      maskedIban: null,
      mandateReference: null,
      collectorMemberId: null,
      importedCollectorName: 'ALFRED ONBEKEND',
      notes: null,
      active: true,
      addedViaApp: false,
      status2027: 'Open',
      amount2026: 70,
    },
  ];
  advertiserCampaignYear = 2027;
  advertiserRuns: {
    id: string;
    collectionDate: string;
    description: string;
    messageId: string;
    lineCount: number;
    total: number;
    createdAt: string;
    exportedAt: string | null;
  }[] = [];
  advertiserImports = 0;
  mailingUnsubscribes = [{ email: 'weg@example.com', unsubscribedAt: '2026-10-01T10:00:00Z' }];

  websitePages: {
    id: string;
    slug: string;
    title: string;
    intro: string | null;
    body: string;
    imageUrl: string | null;
    isPublished: boolean;
    sortOrder: number;
    menu: 'None' | 'Association' | 'Carnival';
    photoAlbumId: string | null;
  }[] = [
    {
      id: 'pg-1',
      slug: 'over-ons',
      title: 'Over ons',
      intro: null,
      body: '# Over ons',
      imageUrl: null,
      isPublished: true,
      sortOrder: 0,
      menu: 'Association',
      photoAlbumId: null,
    },
  ];
  committees = [
    { id: 1, name: 'Bestuur', slug: 'bestuur', sortOrder: 10 },
    { id: 2, name: 'Raad van Elf', slug: 'raad-van-elf', sortOrder: 20 },
  ];
  kader: {
    id: string;
    committeeId: number;
    memberId: string | null;
    memberNumber: string | null;
    name: string;
    function: string | null;
    photoUrl: string | null;
    sortOrder: number;
  }[] = [
    {
      id: 'k-1',
      committeeId: 1,
      memberId: 'm-1',
      memberNumber: '0031',
      name: 'Marcel Wiendels',
      function: 'Voorzitter',
      photoUrl: null,
      sortOrder: 10,
    },
    {
      id: 'k-2',
      committeeId: 1,
      memberId: null,
      memberNumber: null,
      name: 'Anouk Berendsen',
      function: 'President',
      photoUrl: null,
      sortOrder: 20,
    },
  ];
  princes: {
    id: string;
    kind: string;
    year: number;
    princeName: string;
    name: string | null;
    motto: string | null;
    photoUrl: string | null;
  }[] = [
    {
      id: 'p-1',
      kind: 'Prince',
      year: 2025,
      princeName: 'Prins Ronnie I',
      name: 'Ronnie Loeters',
      motto: 'Mee lache!',
      photoUrl: null,
    },
  ];
  awards: {
    id: string;
    type: string;
    year: number;
    recipient: string;
    body: string | null;
    photoUrl: string | null;
    slug: string;
    isPublished: boolean;
  }[] = [
    {
      id: 'a-1',
      type: 'Drammertje',
      year: 2025,
      recipient: 'Harrie Sloot',
      body: 'Chauffeur.',
      photoUrl: null,
      slug: 'harrie-sloot-2025',
      isPublished: true,
    },
  ];
  news: Record<string, unknown>[] = [];

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

  /** Laatste voorbeeld van een mailing (fase 27a), zoals de API het op een eigen adres zet. */
  mailingPreviewHtml = '';

  async install(page: Page) {
    await page.route('**/api/v1/**', (route) => this.handle(route));
    await page.route('**/mailing-voorbeeld/**', (route) =>
      route.fulfill({ status: 200, contentType: 'text/html', body: this.mailingPreviewHtml }),
    );
  }

  // Fase 22a: jury van de huidige optocht.
  jury = {
    paradeId: 'p-1',
    paradeName: 'Optocht Loil 2027',
    paradeDate: '2027-02-07',
    jurors: [
      {
        userId: 'j-1',
        name: 'Anke Jansen',
        email: 'anke@example.com',
        invited: false,
        headJury: true,
        categoryIds: [1],
        submittedAt: '2027-02-07T15:40:00Z' as string | null,
        scored: 13,
        assigned: 13,
      },
      {
        userId: 'j-2',
        name: 'Bert de Vries',
        email: 'bert@example.com',
        invited: false,
        headJury: false,
        categoryIds: [1, 3],
        submittedAt: null as string | null,
        scored: 9,
        assigned: 19,
      },
    ],
    categories: [
      {
        categoryId: 1,
        name: 'Getrokken wagens volwassenen',
        judged: true,
        originality: 1,
        carnivalesque: 1,
        quality: 2,
        overall: 1,
        jurorCount: 2,
        entryCount: 13,
      },
      {
        categoryId: 3,
        name: 'Loopgroepen groot volwassenen',
        judged: true,
        originality: 1,
        carnivalesque: 1,
        quality: 1,
        overall: 2,
        jurorCount: 1,
        entryCount: 6,
      },
    ],
    // Fase 22b: Bert heeft via "Hele optocht" ook loopgroepen gejureerd die niet aan hem zijn toegewezen.
    outside: [
      {
        userId: 'j-2',
        jurorName: 'Bert de Vries',
        registrationId: 'r-6',
        startNumber: 6,
        groupName: 'De jeugdige Sökkels',
        categoryName: 'Loopgroepen groot jeugd',
        passes: 3,
        decision: null as 'Approved' | 'Rejected' | null,
      },
      {
        userId: 'j-2',
        jurorName: 'Bert de Vries',
        registrationId: 'r-12',
        startNumber: 12,
        groupName: 'DwarZ',
        categoryName: 'Loopgroepen groot jeugd',
        passes: 2,
        decision: null as 'Approved' | 'Rejected' | null,
      },
    ],
  };

  // Fase 22c: uitslag (alleen de uitslagcommissie).
  results = {
    paradeId: 'p-1',
    paradeName: 'Optocht Loil 2027',
    paradeDate: '2027-02-07',
    publishedAt: null as string | null,
    categories: [
      {
        categoryId: 1,
        name: 'Getrokken wagens volwassenen',
        jurors: 5,
        submitted: 5,
        ready: true,
        weightOriginality: 1,
        weightCarnivalesque: 1,
        weightQuality: 2,
        weightOverall: 1,
        maxPoints: 2500,
        entries: 2,
        rows: [
          {
            place: 1,
            registrationId: 'r-64',
            startNumber: 64,
            groupName: 'De Droatneagels',
            motto: 'We-j goan deur tot in de 7de hemel.',
            originality: 420,
            carnivalesque: 432,
            quality: 912,
            overall: 440,
            total: 2204,
            photoCount: 0,
          },
          {
            place: 2,
            registrationId: 'r-66',
            startNumber: 66,
            groupName: 'De Druktemoakers',
            motto: 'Veltinzz in de kerk',
            originality: 412,
            carnivalesque: 418,
            quality: 846,
            overall: 425,
            total: 2101,
            photoCount: 2,
          },
        ],
      },
      {
        categoryId: 2,
        name: 'Wagens jeugd',
        jurors: 5,
        submitted: 3,
        ready: false,
        weightOriginality: 1,
        weightCarnivalesque: 1,
        weightQuality: 2,
        weightOverall: 1,
        maxPoints: 2500,
        entries: 4,
        rows: [] as unknown[],
      },
    ],
  };

  private handleJury(
    path: string,
    method: string,
    body: Record<string, unknown>,
    json: (data: unknown, status?: number) => Promise<void>,
    noContent: () => Promise<void>,
  ) {
    let m: RegExpMatchArray | null;
    const recount = () =>
      this.jury.categories.forEach(
        (c) => (c.jurorCount = this.jury.jurors.filter((j) => j.categoryIds.includes(c.categoryId)).length),
      );
    if (path === '/admin/jury' && method === 'GET') return json(this.jury);
    if (/^\/admin\/jury\/parades\/[^/]+\/outside$/.test(path)) {
      for (const d of body.decisions as {
        userId: string;
        registrationId: string;
        decision: 'Approved' | 'Rejected' | null;
      }[]) {
        const o = this.jury.outside.find((x) => x.userId === d.userId && x.registrationId === d.registrationId)!;
        o.decision = d.decision;
      }
      this.record('jury.outside-decided', 'Parade', this.jury.paradeId, body);
      return noContent();
    }
    if (path === '/admin/jury/jurors' && method === 'POST') {
      const juror = {
        userId: `j-${this.jury.jurors.length + 1}`,
        name: String(body.name),
        email: String(body.email),
        invited: true,
        headJury: false,
        categoryIds: [] as number[],
        submittedAt: null,
        scored: 0,
        assigned: 0,
      };
      this.jury.jurors.push(juror);
      this.record('jury.invited', 'User', juror.userId, body);
      return json({ id: juror.userId }, 201);
    }
    if ((m = path.match(/^\/admin\/jury\/parades\/[^/]+\/jurors\/([^/]+)\/categories$/))) {
      this.jury.jurors.find((j) => j.userId === m![1])!.categoryIds = body.categoryIds as number[];
      recount();
      return noContent();
    }
    if ((m = path.match(/^\/admin\/jury\/jurors\/([^/]+)\/head-jury$/))) {
      this.jury.jurors.find((j) => j.userId === m![1])!.headJury = Boolean(body.headJury);
      return noContent();
    }
    if ((m = path.match(/^\/admin\/jury\/jurors\/([^/]+)\/resend-invite$/))) return noContent();
    if ((m = path.match(/^\/admin\/jury\/jurors\/([^/]+)$/)) && method === 'DELETE') {
      this.jury.jurors = this.jury.jurors.filter((j) => j.userId !== m![1]);
      recount();
      return noContent();
    }
    if ((m = path.match(/^\/admin\/jury\/parades\/[^/]+\/categories\/(\d+)$/))) {
      Object.assign(
        this.jury.categories.find((c) => c.categoryId === Number(m![1]))!,
        body,
      );
      return noContent();
    }
    return json({ title: 'Niet gevonden' }, 404);
  }

  websiteImport: {
    counts: { kind: string; pending: number; done: number; skipped: number; failed: number }[];
    failures: { id: number; kind: string; sourceUrl: string; title: string | null; error: string | null }[];
    running: boolean;
    lastActivity: string | null;
  } = { counts: [], failures: [], running: false, lastActivity: null };

  private handleAdvertisers(
    path: string,
    method: string,
    body: Record<string, unknown>,
    url: URL,
    json: (data: unknown, status?: number) => Promise<void>,
    noContent: () => Promise<void>,
  ) {
    let m: RegExpMatchArray | null;
    const collectorName = (id: string | null) => this.advertiserCollectors.find((c) => c.memberId === id)?.name ?? null;
    if (path === '/admin/advertisers/collections/preview') {
      const done = this.advertiserRuns.length > 0;
      return json({
        date: url.searchParams.get('date'),
        count: done ? 0 : 1,
        total: done ? 0 : 35,
        lines: done
          ? []
          : [
              {
                memberId: 'adv-1',
                memberNumber: '1',
                fullName: 'Bakkerij De Test',
                kind: 'Advertentie',
                amount: 35,
                ibanMasked: '**** 4300',
                mandateReference: 'DVD000000001',
                mandateSignedOn: null,
                sequenceType: 'Rcur',
                warning: 'Datum machtiging onbekend: 01-11-2009 (gemigreerde machtiging)',
              },
            ],
        skipped: [
          { memberId: 'adv-3', memberNumber: '3', fullName: 'Kapsalon Zonder', amount: 50, reason: 'Geen IBAN' },
          ...(done
            ? [
                {
                  memberId: 'adv-1',
                  memberNumber: '1',
                  fullName: 'Bakkerij De Test',
                  amount: 35,
                  reason: 'Al in een incasso van 2027',
                },
              ]
            : []),
        ],
        warnings: [],
        creditorComplete: true,
      });
    }
    if (path === '/admin/advertisers/collections' && method === 'POST') {
      this.advertiserRuns.push({
        id: 'arun-1',
        collectionDate: body.date as string,
        description: 'Drammerskrant 2027 CV De Vrolijke Drammers',
        messageId: 'DVDADV-1',
        lineCount: 1,
        total: 35,
        createdAt: '2026-10-04T12:00:00Z',
        exportedAt: null,
      });
      return json({ id: 'arun-1' }, 201);
    }
    if (path === '/admin/advertisers/collections') return json(this.advertiserRuns);
    if ((m = path.match(/^\/admin\/advertisers\/collections\/([^/]+)\/file$/))) {
      this.advertiserRuns[0]!.exportedAt = '2026-10-04T12:05:00Z';
      return json({ document: 'pain.008' });
    }
    if (path === '/admin/advertisers/collectors') return json(this.advertiserCollectors);
    if (path === '/admin/advertisers/campaign-year') {
      if (method === 'PUT') {
        this.advertiserCampaignYear = body.year as number;
        return noContent();
      }
      return json({ year: this.advertiserCampaignYear });
    }
    if (path === '/admin/advertisers/import/preview' || path === '/admin/advertisers/import') {
      if (path === '/admin/advertisers/import') this.advertiserImports++;
      return json({
        rows: 187,
        new: 185,
        updated: 2,
        errors: [],
        warnings: [
          { row: 12, message: 'Kapsalon: machtiging zonder IBAN; incasso is pas mogelijk als die is ingevuld.' },
        ],
        unknownCollectors: [],
        years: [2025, 2026],
      });
    }
    if (path === '/admin/advertisers/status') {
      const collector = url.searchParams.get('collector');
      const rows = this.advertisers
        .filter((a) => !collector || a.collectorMemberId === collector)
        .map((a) => ({
          id: a.id,
          number: a.number,
          companyName: a.companyName,
          city: a.city,
          kind: a.kind,
          payment: a.payment,
          collectorMemberId: a.collectorMemberId,
          collectorName: collectorName(a.collectorMemberId) ?? a.importedCollectorName,
          status: a.status2027,
          amount: a.status2027 === 'Collected' ? a.amount2026 : null,
          isFree: false,
          previousAmount: a.amount2026,
          statusChangedAt: null,
          note: null,
        }));
      const totals = (list: typeof rows) => ({
        total: list.length,
        collected: list.filter((r) => r.status === 'Collected').length,
        stopped: list.filter((r) => r.status === 'Stopped').length,
        open: list.filter((r) => r.status === 'Open').length,
        collectedAmount: list.reduce((sum, r) => sum + (r.amount ?? 0), 0),
        expectedAmount: list.filter((r) => r.status !== 'Stopped').reduce((sum, r) => sum + (r.previousAmount ?? 0), 0),
      });
      return json({
        year: Number(url.searchParams.get('year') ?? this.advertiserCampaignYear),
        totals: totals(rows),
        perCollector: [
          {
            collectorMemberId: 'm-1',
            name: 'Piet van der Berg',
            totals: totals(rows.filter((r) => r.collectorMemberId === 'm-1')),
          },
          {
            collectorMemberId: null,
            name: 'ALFRED ONBEKEND',
            totals: totals(rows.filter((r) => r.collectorMemberId === null)),
          },
        ],
        rows,
      });
    }
    if ((m = path.match(/^\/admin\/advertisers\/([^/]+)\/years\/(\d+)$/))) {
      const a = this.advertisers.find((x) => x.id === m![1])!;
      a.status2027 = body.status as string;
      this.record('advertiser.status-changed', 'Advertiser', a.id, body);
      return noContent();
    }
    if ((m = path.match(/^\/admin\/advertisers\/([^/]+)\/iban$/))) {
      return json({ iban: 'NL91ABNA0417164300' });
    }
    if (path === '/admin/advertisers' && method === 'POST') {
      const id = `adv-${this.advertisers.length + 1}`;
      this.advertisers.push({
        ...(body as unknown as (typeof this.advertisers)[number]),
        id,
        maskedIban: body.iban ? '**** 0000' : null,
        importedCollectorName: null,
        addedViaApp: false,
        status2027: 'Open',
        amount2026: 0,
      });
      return json({ id }, 201);
    }
    if (path === '/admin/advertisers') {
      const collector = url.searchParams.get('collector');
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      return json(
        this.advertisers
          .filter(
            (a) =>
              (!collector || a.collectorMemberId === collector) &&
              (!search || a.companyName.toLowerCase().includes(search)),
          )
          .map((a) => ({
            id: a.id,
            number: a.number,
            companyName: a.companyName,
            contactName: a.contactName,
            city: a.city,
            email: a.email,
            kind: a.kind,
            payment: a.payment,
            collectorMemberId: a.collectorMemberId,
            collectorName: collectorName(a.collectorMemberId),
            importedCollectorName: a.importedCollectorName,
            hasIban: a.maskedIban !== null,
            hasMandate: a.mandateReference !== null,
            lastYear: 2026,
            lastAmount: a.amount2026,
            lastFree: false,
            active: a.active,
            addedViaApp: a.addedViaApp,
          })),
      );
    }
    if ((m = path.match(/^\/admin\/advertisers\/([^/]+)$/))) {
      const a = this.advertisers.find((x) => x.id === m![1])!;
      if (method === 'PUT') {
        const { iban, ...rest } = body;
        Object.assign(a, rest, iban ? { maskedIban: '**** 9999' } : {});
        if (a.collectorMemberId) a.importedCollectorName = null;
        this.record('advertiser.updated', 'Advertiser', a.id, rest);
        return noContent();
      }
      return json({
        ...a,
        collectorName: collectorName(a.collectorMemberId),
        years: [
          { year: 2026, amount: a.amount2026, isFree: false, status: 'Collected', statusChangedAt: null, note: null },
        ],
      });
    }
    return json({ title: 'Niet gevonden (mock)' }, 404);
  }

  private handleMailing(
    path: string,
    method: string,
    body: Record<string, unknown>,
    url: URL,
    json: (data: unknown, status?: number) => Promise<void>,
    noContent: () => Promise<void>,
  ) {
    let m: RegExpMatchArray | null;
    const audience = (listIds: string[]) => ({
      recipients: listIds.some((id) => this.mailingLists.find((l) => l.id === id)?.allMembers)
        ? 120
        : listIds.length * 10,
      unsubscribed: listIds.length > 0 ? 1 : 0,
      withoutEmail: 0,
    });
    if (path === '/admin/mailing/lists' && method === 'GET') {
      return json(
        this.mailingLists.map((l) => ({
          id: l.id,
          name: l.name,
          description: l.description,
          allMembers: l.allMembers,
          memberCount: l.memberIds.length,
          groupCount: l.groupIds.length,
          addressCount: l.addresses.length,
        })),
      );
    }
    if (path === '/admin/mailing/lists' && method === 'POST') {
      const id = `ml-${this.mailingLists.length + 1}`;
      this.mailingLists.push({ ...(body as unknown as (typeof this.mailingLists)[number]), id });
      this.record('mailing.list-created', 'MailingList', id, body);
      return json({ id }, 201);
    }
    if ((m = path.match(/^\/admin\/mailing\/lists\/([^/]+)$/))) {
      const list = this.mailingLists.find((l) => l.id === m![1])!;
      if (method === 'PUT') {
        Object.assign(list, body);
        return noContent();
      }
      return json({
        id: list.id,
        name: list.name,
        description: list.description,
        allMembers: list.allMembers,
        members: this.members
          .filter((x) => list.memberIds.includes(x.id))
          .map((x) => ({ id: x.id, fullName: x.fullName, memberNumber: x.memberNumber, email: x.email })),
        groups: this.groups.filter((g) => list.groupIds.includes(g.id)).map((g) => ({ id: g.id, name: g.name })),
        addresses: list.addresses,
        audience: audience([list.id]),
      });
    }
    if (path === '/admin/mailing/mailings' && method === 'GET') {
      return json(
        this.mailings.map((x) => ({
          id: x.id,
          kind: x.kind,
          subject: x.subject,
          status: x.status,
          recipientCount: x.recipientCount,
          sentCount: x.status === 'Draft' ? 0 : x.recipientCount,
          sentAt: x.sentAt,
          updatedAt: '2026-10-04T10:00:00Z',
        })),
      );
    }
    if (path === '/admin/mailing/mailings' && method === 'POST') {
      const id = `mail-${this.mailings.length + 1}`;
      this.mailings.push({
        ...(body as unknown as (typeof this.mailings)[number]),
        id,
        status: 'Draft',
        sentAt: null,
        recipientCount: 0,
      });
      return json({ id }, 201);
    }
    if (path === '/admin/mailing/preview') {
      const blocks = body.blocks as { type: string; text?: string | null; label?: string | null }[];
      const html = blocks
        .map((b) =>
          b.type === 'button' ? `<a>${b.label}</a>` : `<p>${(b.text ?? '').replace('{voornaam}', 'Piet')}</p>`,
        )
        .join('');
      this.mailingPreviewHtml = `<!DOCTYPE html><html><body>${html}</body></html>`;
      return json({
        subject: body.subject,
        html: this.mailingPreviewHtml,
        previewUrl: `/mailing-voorbeeld/${'0'.repeat(31)}${(this.mailingTests % 10).toString()}`,
        perHour: 90,
        plainText: blocks.map((b) => b.text ?? b.label ?? '').join('\n\n'),
        audience: audience((body.listIds as string[]) ?? []),
      });
    }
    if ((m = path.match(/^\/admin\/mailing\/mailings\/([^/]+)\/(test|send|duplicate)$/))) {
      const mailing = this.mailings.find((x) => x.id === m![1])!;
      if (m[2] === 'test') {
        this.mailingTests++;
        return json({ sentTo: 'bestuur@example.com' });
      }
      if (m[2] === 'send') {
        Object.assign(mailing, {
          status: 'Sending',
          sentAt: '2026-10-04T12:00:00Z',
          recipientCount: audience(mailing.listIds).recipients,
        });
        this.record('mailing.sent', 'Mailing', mailing.id, {});
        return json({ recipients: mailing.recipientCount, lastAt: '2026-10-04T13:20:00Z' });
      }
      const id = `mail-${this.mailings.length + 1}`;
      this.mailings.push({ ...mailing, id, status: 'Draft', sentAt: null, recipientCount: 0 });
      return json({ id }, 201);
    }
    if ((m = path.match(/^\/admin\/mailing\/mailings\/([^/]+)$/))) {
      const mailing = this.mailings.find((x) => x.id === m![1])!;
      if (method === 'PUT') {
        Object.assign(mailing, body);
        return noContent();
      }
      if (method === 'DELETE') {
        this.mailings = this.mailings.filter((x) => x !== mailing);
        return noContent();
      }
      const sent = mailing.status === 'Sent' ? mailing.recipientCount : 0;
      return json({ ...mailing, perHour: 90, progress: { pending: mailing.recipientCount - sent, sent, failed: 0 } });
    }
    if (path === '/admin/mailing/unsubscribes') {
      if (method === 'DELETE') {
        const email = url.searchParams.get('email');
        this.mailingUnsubscribes = this.mailingUnsubscribes.filter((u) => u.email !== email);
        return noContent();
      }
      return json(this.mailingUnsubscribes);
    }
    if (path === '/admin/mailing/images') {
      return json({ path: 'uploads/0123456789abcdef0123456789abcdef.jpg', url: PIXEL });
    }
    return json({ title: 'Niet gevonden (mock)' }, 404);
  }

  private handleWebsite(
    path: string,
    method: string,
    body: Record<string, unknown>,
    url: URL,
    json: (data: unknown, status?: number) => Promise<void>,
    noContent: () => Promise<void>,
  ) {
    let m: RegExpMatchArray | null;
    const photo = (value: unknown, current: string | null) => (value === '' ? null : value ? PIXEL : current);
    if (path === '/admin/website/import' || path === '/admin/website/import/retry') {
      if (method === 'POST') {
        // Nep-import: na starten staat alles meteen klaar, met één mislukte pagina; opnieuw proberen lost die op.
        const retry = path.endsWith('/retry');
        this.websiteImport = {
          counts: [
            { kind: 'Post', pending: 0, done: 120, skipped: 0, failed: 0 },
            { kind: 'Page', pending: 0, done: 14, skipped: 9, failed: retry ? 0 : 1 },
            { kind: 'Prince', pending: 0, done: 60, skipped: 0, failed: 0 },
          ],
          failures: retry
            ? []
            : [
                {
                  id: 7,
                  kind: 'Page',
                  sourceUrl: 'https://vrolijkedrammers.nl/oud/',
                  title: 'Oude pagina',
                  error: 'Niet gevonden',
                },
              ],
          running: false,
          lastActivity: '2026-10-01T10:00:00Z',
        };
        this.record('website.import-started', 'WebsiteImport', '1', {});
        return json(null, 202);
      }
      return json(this.websiteImport);
    }
    if (path === '/admin/website/settings') {
      if (method === 'PUT') {
        const { heroImage, ...rest } = body as Record<string, never>;
        Object.assign(this.websiteSettings, rest, {
          heroImageUrl: photo(heroImage, this.websiteSettings.heroImageUrl),
        });
        this.record('website.settings-updated', 'WebsiteSettings', '1', rest);
        return noContent();
      }
      return json({
        ...this.websiteSettings,
        youthPrinceCount: this.princes.filter((p) => p.kind === 'YouthPrince').length,
      });
    }
    if (path === '/admin/website/albums') {
      return json(
        this.albums.map((a) => ({ id: a.id, title: a.title, albumDate: a.albumDate, photoCount: a.photos.length })),
      );
    }
    if (path === '/admin/website/pages') {
      if (method === 'POST') {
        const id = `pg-${this.websitePages.length + 1}`;
        this.websitePages.push({
          ...(body as unknown as (typeof this.websitePages)[number]),
          id,
          slug: (body.slug as string) ?? 'nieuwe-pagina',
          imageUrl: photo(body.image, null),
        });
        return json({ id }, 201);
      }
      return json(
        this.websitePages.map((p) => ({
          id: p.id,
          slug: p.slug,
          title: p.title,
          isPublished: p.isPublished,
          sortOrder: p.sortOrder,
          menu: p.menu,
          updatedAt: '2026-09-30T20:00:00Z',
        })),
      );
    }
    if ((m = path.match(/^\/admin\/website\/pages\/([^/]+)$/))) {
      const page = this.websitePages.find((p) => p.id === m![1])!;
      if (method === 'PUT') {
        Object.assign(page, body, { imageUrl: photo(body.image, page.imageUrl) });
        return noContent();
      }
      if (method === 'DELETE') {
        this.websitePages = this.websitePages.filter((p) => p !== page);
        return noContent();
      }
      return json(page);
    }
    if (path === '/admin/website/committees') {
      if (method === 'POST') {
        const id = this.committees.length + 1;
        this.committees.push({ id, name: body.name as string, slug: `c-${id}`, sortOrder: body.sortOrder as number });
        return json({ id }, 201);
      }
      return json(
        this.committees.map((c) => ({
          ...c,
          members: this.kader.filter((k) => k.committeeId === c.id).sort((a, b) => a.sortOrder - b.sortOrder),
        })),
      );
    }
    if ((m = path.match(/^\/admin\/website\/committees\/(\d+)\/order$/))) {
      (body.ids as string[]).forEach((id, i) => (this.kader.find((k) => k.id === id)!.sortOrder = (i + 1) * 10));
      this.record('website.kader-reordered', 'Committee', m[1]!, body.ids);
      return noContent();
    }
    if ((m = path.match(/^\/admin\/website\/committees\/(\d+)$/))) {
      const c = this.committees.find((x) => x.id === Number(m![1]))!;
      if (method === 'DELETE') this.committees = this.committees.filter((x) => x !== c);
      else Object.assign(c, body);
      return noContent();
    }
    if (path === '/admin/website/member-search') {
      const q = (url.searchParams.get('q') ?? '').toLowerCase();
      return json(
        this.members
          .filter((x) => x.fullName.toLowerCase().includes(q))
          .map((x) => ({ id: x.id, memberNumber: x.memberNumber, fullName: x.fullName, city: x.city })),
      );
    }
    if (path === '/admin/website/kader' && method === 'POST') {
      const id = `k-${this.kader.length + 1}`;
      const member = this.members.find((x) => x.id === body.memberId);
      this.kader.push({
        id,
        committeeId: body.committeeId as number,
        memberId: (body.memberId as string) ?? null,
        memberNumber: member?.memberNumber ?? null,
        name: body.name as string,
        function: (body.function as string) ?? null,
        photoUrl: photo(body.photo, null),
        sortOrder: 1000,
      });
      this.record('website.kader-added', 'CommitteeMember', id, body);
      return json({ id }, 201);
    }
    if ((m = path.match(/^\/admin\/website\/kader\/([^/]+)$/))) {
      const k = this.kader.find((x) => x.id === m![1])!;
      if (method === 'DELETE') this.kader = this.kader.filter((x) => x !== k);
      else Object.assign(k, body, { photoUrl: photo(body.photo, k.photoUrl) });
      return noContent();
    }
    if (path === '/admin/website/princes') {
      if (method === 'POST') {
        const id = `p-${this.princes.length + 1}`;
        this.princes.push({
          ...(body as unknown as (typeof this.princes)[number]),
          id,
          photoUrl: photo(body.photo, null),
        });
        return json({ id }, 201);
      }
      const kind = url.searchParams.get('kind');
      return json(this.princes.filter((p) => !kind || p.kind === kind).sort((a, b) => b.year - a.year));
    }
    if ((m = path.match(/^\/admin\/website\/princes\/([^/]+)$/))) {
      const p = this.princes.find((x) => x.id === m![1])!;
      if (method === 'DELETE') this.princes = this.princes.filter((x) => x !== p);
      else Object.assign(p, body, { photoUrl: photo(body.photo, p.photoUrl) });
      return noContent();
    }
    if (path === '/admin/website/awards') {
      if (method === 'POST') {
        const id = `a-${this.awards.length + 1}`;
        this.awards.push({
          ...(body as unknown as (typeof this.awards)[number]),
          id,
          slug: id,
          photoUrl: photo(body.photo, null),
        });
        return json({ id }, 201);
      }
      const type = url.searchParams.get('type');
      return json(this.awards.filter((a) => !type || a.type === type).sort((a, b) => b.year - a.year));
    }
    if ((m = path.match(/^\/admin\/website\/awards\/([^/]+)$/))) {
      const a = this.awards.find((x) => x.id === m![1])!;
      if (method === 'DELETE') this.awards = this.awards.filter((x) => x !== a);
      else Object.assign(a, body, { photoUrl: photo(body.photo, a.photoUrl) });
      return noContent();
    }
    return json({ title: 'Niet gevonden (mock)' }, 404);
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
    if (path.startsWith('/admin/sales')) {
      if (method !== 'GET') this.salesCalls.push({ method, path, body });
      if (path === '/admin/sales/summary') {
        return json({
          sold: url.searchParams.get('kind') === 'Tokens' ? 1120 : 558,
          revenueCents: 348250,
          openPaymentLinks: 4,
          openAmountCents: 8750,
          tokensToCollect: 640,
          tokensSold: 1120,
        });
      }
      if (path === '/admin/sales/products' && method === 'GET') return json(this.saleProducts);
      if (path === '/admin/sales/products' && method === 'POST') return json('sp-new', 201);
      if ((m = path.match(/^\/admin\/sales\/products\/([^/]+)$/)) && method === 'PUT') return noContent();
      if ((m = path.match(/^\/admin\/sales\/products\/([^/]+)\/waitlist$/))) {
        return json(
          m[1] === 'sp-vr'
            ? [
                {
                  id: 'w-1',
                  position: 1,
                  status: 'Waiting',
                  groupName: 'De Snotapen',
                  memberQuantity: 10,
                  paidQuantity: 0,
                  buyerName: 'Piet Lid',
                  buyerEmail: 'piet@example.com',
                  buyerPhone: null,
                  buyerIsMember: true,
                  remark: null,
                  createdAt: '2026-10-12T18:14:00Z',
                  invitedAt: null,
                  orderNumber: null,
                  fits: false,
                },
                {
                  id: 'w-2',
                  position: 2,
                  status: 'Waiting',
                  groupName: null,
                  memberQuantity: 0,
                  paidQuantity: 2,
                  buyerName: 'Jan Jansen',
                  buyerEmail: 'jan@example.com',
                  buyerPhone: null,
                  buyerIsMember: false,
                  remark: null,
                  createdAt: '2026-10-13T07:40:00Z',
                  invitedAt: null,
                  orderNumber: null,
                  fits: true,
                },
              ]
            : [],
        );
      }
      if (path === '/admin/sales/orders' && method === 'GET') {
        const kind = url.searchParams.get('kind');
        const items = this.saleOrders.filter(
          (o) => !kind || this.saleProducts.find((p) => p.id === o.productId)?.kind === kind,
        );
        return json({ items, page: 1, pageSize: 25, totalCount: items.length });
      }
      if (path === '/admin/sales/orders' && method === 'POST') {
        return json(
          { id: 'so-new', number: '2027-0200', status: body.payment === 'Cash' ? 'Confirmed' : 'AwaitingPayment' },
          201,
        );
      }
      if (path.match(/^\/admin\/sales\/orders\/[^/]+\/(paid-cash|cancel|resend-link)$/)) return noContent();
      if (path === '/admin/sales/groups') {
        return json([
          { groupName: 'De Kruumels', activeMembers: 11, persons: 13, ordered: 8, remaining: 5 },
          { groupName: 'De Snotapen', activeMembers: 12, persons: 12, ordered: 0, remaining: 12 },
        ]);
      }
      if ((m = path.match(/^\/admin\/sales\/waitlist\/([^/]+)\/grant$/))) {
        return json({
          id: 'so-w',
          number: '2027-0201',
          status: body.payment === 'Cash' ? 'Confirmed' : 'AwaitingPayment',
        });
      }
      if (path.match(/^\/admin\/sales\/waitlist\/[^/]+$/) && method === 'DELETE') return noContent();
      if (path === '/admin/sales/pronkzitting') {
        return json([
          {
            productId: 'sp-vr',
            name: 'Pronkzitting vrijdag',
            date: '2027-02-05',
            capacity: 300,
            sold: 300,
            held: 0,
            waiting: 2,
            rows: [
              {
                name: 'De Kruumels',
                isGroup: true,
                quantity: 4,
                orderers: 'Mendy Mom',
                phones: '06 3333 4444',
                emails: 'mendy@example.com',
                membership: 'Lid',
                paid: 'Gratis (contributie)',
                remarks: '1 rolstoelplek',
                orderNumbers: ['2027-0011'],
              },
              {
                name: 'Jan Jansen',
                isGroup: false,
                quantity: 2,
                orderers: 'Jan Jansen',
                phones: '06 5555 6666',
                emails: 'jan@example.com',
                membership: 'Niet-lid',
                paid: 'Betaald (iDEAL)',
                remarks: 'Bij De Kruumels zitten',
                orderNumbers: ['2027-0012'],
              },
            ],
          },
          {
            productId: 'sp-za',
            name: 'Pronkzitting zaterdag',
            date: '2027-02-06',
            capacity: 300,
            sold: 250,
            held: 8,
            waiting: 0,
            rows: [],
          },
        ]);
      }
      if (path === '/admin/sales/pronkzitting/export') {
        return route.fulfill({
          status: 200,
          headers: {
            'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'content-disposition': "attachment; filename*=UTF-8''Pronkzitting%20tafelindeling%2029-9-2026.xlsx",
          },
          body: 'xlsx',
        });
      }
      if (path === '/admin/sales/kassalog') {
        return json({
          day: url.searchParams.get('day') ?? '2027-02-13',
          issued: 86,
          tokensIssued: 1240,
          refused: 1,
          rows: [
            {
              id: 'k-1',
              scannedAt: '2027-02-13T20:14:00Z',
              issuedAt: '2027-02-13T20:14:30Z',
              outcome: 'Issued',
              reason: null,
              memberName: 'Mendy Mom',
              memberGroup: 'Kruumels',
              quantity: 20,
              orderNumber: '2027-0311',
              paidWith: 'iDEAL',
              operatorName: 'Kees',
              deviceName: 'Kassa 1',
            },
            {
              id: 'k-2',
              scannedAt: '2027-02-13T19:57:00Z',
              issuedAt: null,
              outcome: 'Refused',
              reason: 'WrongDevice',
              memberName: 'Ruby Mom',
              memberGroup: 'Kruumels',
              quantity: 10,
              orderNumber: '2027-0305',
              paidWith: 'iDEAL',
              operatorName: 'Anja',
              deviceName: 'Kassa 2',
            },
          ],
        });
      }
      if (path === '/admin/sales/tokens') return json(this.saleOrders.filter((o) => o.productId === 'sp-mu'));
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
    if (path === '/admin/parade/arrival-times' && method === 'GET') {
      return json(this.arrivals);
    }
    if (path === '/admin/parade/arrival-times/generate') {
      const b = body as { first: string; intervalMinutes: number };
      const [h, mi] = b.first.split(':').map(Number) as [number, number];
      this.arrivals.rows.forEach((row, i) => {
        const minutes = h * 60 + mi + i * b.intervalMinutes;
        row.arrivalTime = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}:00`;
      });
      this.lineupCalls.push({ path, body });
      return json({ changed: this.arrivals.rows.length });
    }
    if (path === '/admin/parade/arrival-times/location') {
      this.arrivals.location = (body as { location: string | null }).location;
      this.lineupCalls.push({ path, body });
      return noContent();
    }
    if (path === '/admin/parade/arrival-times/publish') {
      this.arrivals.publishedAt = '2026-09-29T12:00:00Z';
      this.lineupCalls.push({ path, body: null });
      return json({ changed: this.arrivals.rows.filter((r) => r.arrivalTime).length });
    }
    if ((m = path.match(/^\/admin\/parade\/arrival-times\/(r-\d+)$/)) && method === 'PUT') {
      this.arrivals.rows.find((r) => r.id === m![1])!.arrivalTime = (
        body as { arrivalTime: string | null }
      ).arrivalTime;
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
    if ((m = path.match(/^\/admin\/parades\/([^/]+)$/)) && method === 'DELETE') {
      const parade = this.parades.find((p) => p.id === m![1]);
      if (!parade) return json({ title: 'Niet gevonden' }, 404);
      if (String(url.searchParams.get('confirmName') ?? '').toLowerCase() !== String(parade.name).toLowerCase()) {
        return json(
          { title: 'Bevestiging', detail: `Typ ter bevestiging de naam van de optocht: ${String(parade.name)}.` },
          422,
        );
      }
      this.parades = this.parades.filter((p) => p !== parade);
      this.record('parade.deleted', 'Parade', String(parade.id), {});
      return noContent();
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
        warnings: this.compositionWarnings,
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
    if (path === '/admin/news/images' || path === '/admin/website/images') {
      return json({ path: 'uploads/0123456789abcdef0123456789abcdef.jpg', url: PIXEL });
    }
    if (path === '/admin/news' && method === 'POST') {
      const id = `n-${this.news.length + 1}`;
      this.news.push({
        ...body,
        id,
        imageUrl: body.image ? PIXEL : null,
        slug: body.showOnWebsite ? 'drammertje-2026' : null,
        pushStatus: null,
      });
      this.record('news.created', 'News', id, body);
      return json({ id }, 201);
    }
    if ((m = path.match(/^\/admin\/news\/([^/]+)$/)) && method === 'GET') {
      return json(this.news.find((n) => n.id === m![1]));
    }
    if (path === '/admin/results') return json(this.results);
    if (path === '/admin/results/export') {
      return route.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: {
          'content-disposition': `attachment; filename=${url.searchParams.get('kind') === 'Zaallijst' ? 'zaallijst' : 'uitslag'}-20270207.xlsx`,
        },
        body: 'xlsx',
      });
    }
    if ((m = path.match(/^\/admin\/results\/entries\/([^/]+)\/photos$/)) && method === 'POST') {
      const row = (this.results.categories[0]!.rows as { registrationId: string; photoCount: number }[]).find(
        (r) => r.registrationId === m![1],
      )!;
      row.photoCount += 1;
      return json([{ id: `f-${row.photoCount}` }], 201);
    }
    if (path === '/admin/results/publish' && method === 'POST') {
      if (!body.prizeCeremonyHeld) return json({ title: 'Na de prijsuitreiking' }, 422);
      this.results.publishedAt = '2027-02-07T21:00:00Z';
      this.record('parade.results-published', 'Parade', 'p-1', body);
      return json({ publishedAt: this.results.publishedAt });
    }
    if (path.startsWith('/admin/jury')) {
      return this.handleJury(path, method, body, json, noContent);
    }
    if (path.startsWith('/admin/website/')) {
      return this.handleWebsite(path, method, body, url, json, noContent);
    }
    if (path.startsWith('/admin/advertisers')) {
      return this.handleAdvertisers(path, method, body, url, json, noContent);
    }
    if (path.startsWith('/admin/mailing/')) {
      return this.handleMailing(path, method, body, url, json, noContent);
    }
    if (path === '/admin/news') {
      return json(
        this.news.map((n) => ({
          id: n.id,
          title: n.title,
          visibility: 'Public',
          status: 'Published',
          publishAt: null,
          expireAt: null,
          showOnWebsite: n.showOnWebsite ?? false,
        })),
      );
    }
    if (path === '/admin/photo-albums' && method === 'POST') {
      const id = `a-${this.albums.length + 1}`;
      this.albums.push({
        ...(body as unknown as (typeof this.albums)[number]),
        id,
        coverPhotoId: null,
        eventId: null,
        photos: [],
      });
      return json({ id }, 201);
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
          category: a.category,
          coverUrl: a.photos[0]?.thumbnailUrl ?? null,
        })),
      );
    }
    if ((m = path.match(/^\/admin\/photo-albums\/([^/]+)\/photos$/)) && method === 'POST') {
      // Bulk-upload in het portal: één foto per verzoek.
      const album = this.albums.find((a) => a.id === m![1])!;
      const id = `p-${Math.random().toString(36).slice(2, 8)}`;
      album.photos.push({
        id,
        processingStatus: 'Ready',
        hidden: false,
        caption: null,
        photographer: null,
        thumbnailUrl: PIXEL,
      });
      return json([{ id }], 201);
    }
    if ((m = path.match(/^\/admin\/photo-albums\/([^/]+)\/photos\/bulk$/))) {
      const album = this.albums.find((a) => a.id === m![1])!;
      const ids = body.photoIds as string[];
      const chosen = album.photos.filter((p) => ids.includes(p.id));
      switch (body.action) {
        case 'Hide':
        case 'Show':
          chosen.forEach((p) => (p.hidden = body.action === 'Hide'));
          break;
        case 'SetPhotographer':
          chosen.forEach((p) => (p.photographer = body.photographer as never));
          break;
        case 'Move':
          this.albums.find((a) => a.id === body.targetAlbumId)!.photos.push(...chosen);
          album.photos = album.photos.filter((p) => !ids.includes(p.id));
          break;
        case 'Delete':
          album.photos = album.photos.filter((p) => !ids.includes(p.id));
          break;
      }
      this.record(`photo.bulk-${String(body.action).toLowerCase()}`, 'PhotoAlbum', album.id, { count: chosen.length });
      return json({ count: chosen.length });
    }
    if ((m = path.match(/^\/admin\/photo-albums\/([^/]+)$/))) {
      if (method === 'PUT') {
        Object.assign(
          this.albums.find((a) => a.id === m![1])!,
          body,
        );
        return noContent();
      }
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
    if (path === '/carnival-years') {
      // De publieke lijst heeft geen actief-vlag; die extra eigenschap negeert de portal.
      return json([...this.years].sort((a, b) => a.startDate.localeCompare(b.startDate)));
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
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/data$/)) && method === 'PUT') {
      const member = this.members.find((x) => x.id === m![1])!;
      const changed: string[] = [];
      if (body.email !== member.email) changed.push('email');
      if (body.city !== member.city) changed.push('city');
      if (body.fullName !== member.fullName) changed.push('name');
      member.email = body.email as string;
      member.city = body.city as string;
      member.fullName = body.fullName as string;
      this.memberLocalFields[member.id] = [...new Set([...(this.memberLocalFields[member.id] ?? []), ...changed])];
      this.record('member.data-updated', 'Member', member.id, body);
      return noContent();
    }
    if ((m = path.match(/^\/admin\/members\/([^/]+)\/local-fields$/)) && method === 'DELETE') {
      delete this.memberLocalFields[m[1]!];
      this.record('member.local-fields-released', 'Member', m[1]!, {});
      return noContent();
    }
    if (path === '/admin/account-requests') {
      const status = url.searchParams.get('status');
      const items = this.accountRequests.filter((r) => !status || r.status === status);
      return json({ items, page: 1, pageSize: 25, totalCount: items.length });
    }
    if ((m = path.match(/^\/admin\/account-requests\/([^/]+)\/(approve|reject)$/))) {
      const request = this.accountRequests.find((r) => r.id === m![1])!;
      request.status = m[2] === 'approve' ? 'Approved' : 'Rejected';
      request.approvedMemberId = body.memberId as string | undefined;
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
        paradeGroupName: 'De Kruumels',
        persons: 1,
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
        localFields: this.memberLocalFields[member.id] ?? [],
        jubileeJoinYearOverride: member.jubileeJoinYearOverride,
        jubileeNote: member.jubileeNote,
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
    if (path === '/admin/collections/creditor') {
      if (method === 'PUT') {
        this.sepaCreditor = body as typeof this.sepaCreditor;
        return noContent();
      }
      return json(this.sepaCreditor);
    }
    if (path === '/admin/collections/preview') {
      const complete = !!(this.sepaCreditor.name && this.sepaCreditor.iban && this.sepaCreditor.creditorId);
      return json({
        date: url.searchParams.get('date'),
        count: 1,
        total: 57.5,
        lines: [
          {
            memberId: 'm-1',
            memberNumber: '001',
            fullName: 'Piet van der Berg',
            kind: 'Combinatie',
            amount: 57.5,
            ibanMasked: '**** 4300',
            mandateReference: 'M-001',
            mandateSignedOn: null,
            sequenceType: 'Rcur',
            warning: 'Datum machtiging onbekend: 01-11-2009 (gemigreerde machtiging)',
          },
        ],
        skipped: [
          {
            memberId: 'm-2',
            memberNumber: '002',
            fullName: 'Anna Jansen',
            amount: 32.5,
            reason: 'Geen IBAN of machtiging',
          },
        ],
        warnings: complete ? [] : ['Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID).'],
        creditorComplete: complete,
      });
    }
    if (path === '/admin/collections') {
      if (method === 'POST') {
        this.collectionRuns.unshift({
          id: 'run-1',
          collectionDate: body.date,
          description: 'Contributie 2027 CV De Vrolijke Drammers',
          messageId: 'DVD-20270301-ABCD1234',
          lineCount: 1,
          total: 57.5,
          createdAt: '2026-10-03T12:00:00Z',
          exportedAt: null,
        });
        this.record('contribution.collection-created', 'CollectionRun', 'run-1', body);
        return json({ id: 'run-1' }, 201);
      }
      return json(this.collectionRuns);
    }
    if ((m = path.match(/^\/admin\/collections\/([^/]+)\/file$/))) {
      this.collectionRuns.find((r) => r.id === m![1])!.exportedAt = '2026-10-03T12:05:00Z';
      return route.fulfill({
        status: 200,
        contentType: 'application/xml',
        headers: { 'content-disposition': 'attachment; filename=incasso-20270301.xml' },
        body: '<Document/>',
      });
    }
    if ((m = path.match(/^\/admin\/collections\/([^/]+)$/)) && method === 'DELETE') {
      this.collectionRuns = this.collectionRuns.filter((r) => r.id !== m![1]);
      return noContent();
    }
    if (path === '/admin/member-requests') return json(this.memberRequests);
    if (
      (m = path.match(/^\/admin\/member-requests\/(changes|breaks)\/([^/]+)\/(approve|reject)$/)) &&
      method === 'POST'
    ) {
      const list = m[1] === 'changes' ? this.memberRequests.changes : this.memberRequests.breaks;
      const index = list.findIndex((x) => x.id === m![2]);
      list.splice(index, 1);
      this.record(`member.${m[1]}-${m[3]}`, 'Member', m[2]!, body);
      return noContent();
    }
    if (path === '/admin/memberships/overview') {
      return json({
        active: 2,
        exempt: 0,
        byKind: [
          { label: 'Lid', count: 1 },
          { label: 'Tweepersoonslid, nog niet gesplitst', count: 1 },
        ],
      });
    }
    if (path === '/admin/memberships/splits') return json(this.splitCandidates);
    if (path === '/admin/memberships/splits/invite' && method === 'POST') {
      const ids =
        (body.memberIds as string[] | null) ??
        this.splitCandidates.filter((c) => c.state === 'NotInvited').map((c) => c.memberId);
      let invited = 0;
      let withoutEmail = 0;
      for (const c of this.splitCandidates.filter((x) => ids.includes(x.memberId))) {
        if (!c.email) {
          withoutEmail++;
          continue;
        }
        c.state = 'Invited';
        c.invitedAt = '2026-10-03T10:00:00Z';
        c.timesInvited++;
        invited++;
      }
      this.record('member.split-invited', 'Member', 'split', { invited });
      return json({ invited, withoutEmail, skipped: 0 });
    }
    if (path === '/admin/contributions' && method === 'GET') {
      const rate = this.contributionRates[0]!;
      const lines = this.members.map((x) => {
        const s = this.membershipSettings[x.id];
        if (s?.kind === 'Partner') {
          return {
            memberId: x.id,
            memberNumber: x.memberNumber,
            fullName: x.fullName,
            kind: 'Partner',
            kindFromEBoekhouden: false,
            senior: false,
            amount: 0,
            status: 'PaidByPartner',
            note: 'Betaald door ' + this.members.find((p) => p.id === s.payerMemberId)?.fullName,
            partnerMemberId: s.payerMemberId,
            partnerName: null,
          };
        }
        const kind = s?.kind ?? (x.id === 'm-1' ? 'TwoPersons' : null);
        return {
          memberId: x.id,
          memberNumber: x.memberNumber,
          fullName: x.fullName,
          kind,
          kindFromEBoekhouden: !s?.kind,
          senior: false,
          amount: kind === 'TwoPersons' ? rate.twoPersons : kind === 'OnePerson' ? rate.onePerson : 0,
          status: kind ? 'Due' : 'Unknown',
          note: kind ? null : 'Soort lidmaatschap onbekend',
          partnerMemberId: null,
          partnerName: null,
        };
      });
      const due = lines.filter((l) => l.status === 'Due');
      return json({
        date: url.searchParams.get('date'),
        rate,
        lines,
        totals: due.length
          ? [{ label: 'Twee personen', count: due.length, amount: due.reduce((a, l) => a + l.amount, 0) }]
          : [],
        total: due.reduce((a, l) => a + l.amount, 0),
      });
    }
    if (path === '/admin/contributions/export') {
      return route.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: { 'content-disposition': 'attachment; filename=contributie.xlsx' },
        body: 'xlsx',
      });
    }
    if (path === '/admin/contributions/rates') {
      if (method === 'PUT') {
        const rate = body as unknown as (typeof this.contributionRates)[number];
        this.contributionRates = [
          { ...rate, id: this.contributionRates.length + 1 },
          ...this.contributionRates.filter((r) => r.validFrom !== rate.validFrom),
        ].sort((a, b) => b.validFrom.localeCompare(a.validFrom));
        this.record('contribution.rate-saved', 'ContributionRate', rate.validFrom, body);
        return noContent();
      }
      return json(this.contributionRates);
    }
    if ((m = path.match(/^\/admin\/contributions\/members\/([^/]+)$/))) {
      if (method === 'PUT') {
        this.membershipSettings[m[1]!] = body as (typeof this.membershipSettings)[string];
        this.record('member.membership-changed', 'Member', m[1]!, body);
        return noContent();
      }
      return json(
        this.membershipSettings[m[1]!] ?? { kind: null, payerMemberId: null, exempt: false, exemptReason: null },
      );
    }
    // Fase 20: jubilarissen; het actieve carnavalsjaar 2026/2027 heeft carnaval in 2027.
    if (path === '/admin/jubilees' && method === 'GET') {
      const reference = 2027;
      const active = this.members.filter((x) => (x.localStatusOverride ?? x.status) === 'Active');
      return json({
        carnivalYearId: 1,
        carnivalYearName: '2026/2027',
        referenceYear: reference,
        milestones: this.jubileeMilestones,
        jubilarians: active
          .map((x) => ({ x, base: x.jubileeJoinYearOverride ?? x.joinYear }))
          .filter(({ base }) => base !== null && this.jubileeMilestones.includes(reference - base))
          .map(({ x, base }) => ({
            memberId: x.id,
            memberNumber: x.memberNumber,
            fullName: x.fullName,
            joinYear: x.joinYear,
            joinYearOverride: x.jubileeJoinYearOverride,
            baseYear: base,
            years: reference - base!,
            note: x.jubileeNote,
            hasEmail: !!x.email,
            invitedAt: this.jubileeInvitations[x.id] ?? null,
          })),
        withoutJoinYear: active
          .filter((x) => (x.jubileeJoinYearOverride ?? x.joinYear) === null)
          .map((x) => ({ memberId: x.id, memberNumber: x.memberNumber, fullName: x.fullName, city: x.city })),
        carnivalYears: [
          { id: 1, name: '2026/2027', active: true },
          { id: 2, name: '2025/2026', active: false },
        ],
      });
    }
    if (path === '/admin/jubilees/invitation-template') {
      if (method === 'PUT') {
        this.jubileeTemplate = body as typeof this.jubileeTemplate;
        return noContent();
      }
      return json({ ...this.jubileeTemplate, placeholders: ['{voornaam}', '{naam}', '{jaren}', '{carnavalsjaar}'] });
    }
    if (path === '/admin/jubilees/invitations' && method === 'POST') {
      const jubilarians = this.members
        .filter((x) => (x.jubileeJoinYearOverride ?? x.joinYear) !== null)
        .filter((x) => this.jubileeMilestones.includes(2027 - (x.jubileeJoinYearOverride ?? x.joinYear)!))
        .map((x) => x.id);
      const ids = (body.memberIds as string[] | null) ?? jubilarians;
      let invited = 0;
      let alreadyInvited = 0;
      for (const id of ids) {
        if (this.jubileeInvitations[id]) alreadyInvited++;
        else {
          this.jubileeInvitations[id] = '2026-10-02T08:00:00Z';
          invited++;
        }
      }
      this.record('member.jubilee-invited', 'CarnivalYear', '1', { invited });
      return json({ invited, alreadyInvited, withoutEmail: 0 });
    }
    if (path === '/admin/jubilees/export') {
      this.record('report.jubilees.exported', 'Report', 'jubilees', {});
      return route.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: { 'content-disposition': 'attachment; filename=jubilarissen-2026-2027.xlsx' },
        body: 'xlsx',
      });
    }
    if (path === '/admin/jubilees/settings') {
      if (method === 'PUT') {
        this.jubileeMilestones = [...new Set(body.milestones as number[])].sort((a, b) => a - b);
        return noContent();
      }
      return json({ milestones: this.jubileeMilestones });
    }
    if ((m = path.match(/^\/admin\/jubilees\/members\/([^/]+)$/)) && method === 'PUT') {
      const member = this.members.find((x) => x.id === m![1])!;
      member.jubileeJoinYearOverride = (body.joinYearOverride as number | null) ?? null;
      member.jubileeNote = (body.note as string | null) ?? null;
      this.record('member.jubilee-year.changed', 'Member', member.id, body);
      return noContent();
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
