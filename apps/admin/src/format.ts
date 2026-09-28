const dateTime = new Intl.DateTimeFormat('nl-NL', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Amsterdam' });
const date = new Intl.DateTimeFormat('nl-NL', { dateStyle: 'medium', timeZone: 'Europe/Amsterdam' });

/** Tijden komen als UTC uit de API en worden in Europe/Amsterdam getoond (docs/03 §7). */
export function formatDateTime(value: string | null | undefined): string {
  return value ? dateTime.format(new Date(value)) : '—';
}

export function formatDate(value: string | null | undefined): string {
  return value ? date.format(new Date(`${value}T12:00:00Z`)) : '—';
}

export const accountStatusLabels: Record<string, string> = {
  Active: 'Actief',
  Blocked: 'Geblokkeerd',
  Disabled: 'Uitgeschakeld',
  Deleted: 'Verwijderd',
};

/** ISO (UTC) → waarde voor &lt;input type="datetime-local"&gt; in de lokale tijd van de browser. */
export function toLocalInput(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const d = new Date(value);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Waarde van &lt;input type="datetime-local"&gt; → ISO (UTC); leeg wordt null. */
export function fromLocalInput(value: string): string | null {
  return value ? new Date(value).toISOString() : null;
}

export const visibilityLabels: Record<string, string> = { Public: 'Iedereen', Members: 'Leden', Restricted: 'Beperkt (rollen)' };
export const statusLabels: Record<string, string> = { Draft: 'Concept', Scheduled: 'Gepland', Published: 'Gepubliceerd', Archived: 'Gearchiveerd' };

export const membershipStatusLabels: Record<string, string> = {
  Active: 'Actief',
  Inactive: 'Inactief',
  Suspended: 'Geschorst',
  Deceased: 'Overleden',
};

export const syncStateLabels: Record<string, string> = {
  InSync: 'Gesynchroniseerd',
  Missing: 'Ontbreekt in e-Boekhouden',
  Conflict: 'Conflict',
};

export const syncJobStatusLabels: Record<string, string> = {
  Queued: 'In de wachtrij',
  Running: 'Bezig',
  Succeeded: 'Geslaagd',
  SucceededWithWarnings: 'Geslaagd met waarschuwingen',
  Conflict: 'Conflicten',
  Failed: 'Mislukt',
};

export const syncItemActionLabels: Record<string, string> = {
  Created: 'Nieuw',
  Updated: 'Gewijzigd',
  Unchanged: 'Ongewijzigd',
  Missing: 'Ontbreekt',
  Deactivated: 'Op inactief gezet',
  Reactivated: 'Teruggekeerd',
  Warning: 'Waarschuwing',
  Error: 'Fout',
  Conflict: 'Conflict',
  Excluded: 'Uitgesloten',
};

export const syncConflictTypeLabels: Record<string, string> = {
  DuplicateMemberNumber: 'Dubbel lidnummer',
  MemberNumberChanged: 'Lidnummer gewijzigd',
  EmailChangedForActiveAccount: 'E-mail gewijzigd bij actief account',
  MassDeletionGuard: 'Veel leden ontbreken',
};

/** Veldnamen uit de sync (bijv. "email,city") in het Nederlands. */
const fieldLabels: Record<string, string> = {
  name: 'naam',
  salutation: 'aanhef',
  gender: 'geslacht',
  address: 'adres',
  postalCode: 'postcode',
  city: 'plaats',
  country: 'land',
  email: 'e-mail',
  phone: 'telefoon',
  mobilePhone: 'mobiel',
  birthDate: 'geboortedatum',
  joinYear: 'inschrijfjaar',
  status: 'status',
  category: 'categorie',
};

export function formatChangedFields(value: string | null | undefined): string {
  return value
    ? value
        .split(',')
        .map((f) => fieldLabels[f] ?? f)
        .join(', ')
    : '';
}

export const groupTypeLabels: Record<string, string> = {
  Committee: 'Commissie',
  DanceGuard: 'Dansgarde',
  ParadeGroup: 'Optochtgroep',
  Other: 'Overig',
};

export const groupFunctionLabels: Record<string, string> = { Member: 'Lid', Lead: 'Leiding' };

export const accountRequestStatusLabels: Record<string, string> = {
  Pending: 'Wacht op beoordeling',
  Approved: 'Goedgekeurd',
  Rejected: 'Afgewezen',
  Duplicate: 'Had al een account',
};

/** Waarom een accountverzoek niet automatisch is goedgekeurd (fase 9). */
export const mismatchReasonLabels: Record<string, string> = {
  'unknown-member-number': 'Lidnummer onbekend',
  'email-mismatch': 'E-mailadres wijkt af van e-Boekhouden',
  'member-not-active': 'Lid is niet actief',
  'has-account': 'Lid heeft al een account',
};

export const provisioningStepLabels: Record<string, string> = {
  Pending: 'Wordt gestart',
  AccountCreated: 'Inlogaccount aangemaakt',
  MemberCreated: 'Gekoppeld aan het lid',
  WelcomeSent: 'Welkomstmail verstuurd',
  Completed: 'Klaar',
  Failed: 'Mislukt',
  EbCreated: 'In e-Boekhouden aangemaakt',
};

export const applicationStatusLabels: Record<string, string> = {
  Draft: 'Concept',
  Submitted: 'Nieuw',
  InReview: 'In behandeling',
  Approved: 'Goedgekeurd',
  Provisioning: 'Wordt verwerkt',
  ProvisioningFailed: 'Verwerken mislukt',
  Activated: 'Lid geworden',
  Rejected: 'Afgewezen',
  Withdrawn: 'Ingetrokken',
};

export const applicationStatusTone: Record<string, string> = {
  Submitted: 'warn',
  InReview: 'info',
  Approved: 'info',
  Provisioning: 'info',
  ProvisioningFailed: 'error',
  Activated: 'ok',
  Rejected: 'neutral',
  Withdrawn: 'neutral',
};

export const applicationSourceLabels: Record<string, string> = { App: 'App', Website: 'Website', Portal: 'Beheerportal' };

export const notificationCategoryLabels: Record<string, string> = {
  Urgent: 'Dringend',
  Program: 'Programma',
  News: 'Nieuws',
  Parade: 'Optocht',
  DanceGuard: 'Dansgarde',
  Kader: 'Kader',
  Tickets: 'Tickets',
  Reminder: 'Herinnering',
  System: 'Systeem',
};

export const notificationStatusLabels: Record<string, string> = {
  Scheduled: 'Gepland',
  Sending: 'Wordt verstuurd',
  Sent: 'Verstuurd',
  PartiallyFailed: 'Deels mislukt',
  Failed: 'Mislukt',
  Canceled: 'Geannuleerd',
};

export const paradeStatusLabels: Record<string, string> = {
  Planned: 'Gepland',
  RegistrationOpen: 'Inschrijving open',
  RegistrationClosed: 'Inschrijving gesloten',
  Composing: 'Samenstellen',
  Final: 'Definitief',
  Completed: 'Afgerond',
};

export const countBasisLabels: Record<string, string> = {
  AdultsOnly: 'Alleen volwassenen',
  ChildrenOnly: 'Alleen kinderen',
  Total: 'Kinderen + volwassenen',
};

export const validationModeLabels: Record<string, string> = { Block: 'Blokkeren', Warn: 'Waarschuwen', None: 'Geen controle' };

export const registrationStatusLabels: Record<string, string> = {
  Draft: 'Concept',
  Submitted: 'Ingediend',
  UnderReview: 'In behandeling',
  AdditionalInformationRequired: 'Aanvulling gevraagd',
  Approved: 'Goedgekeurd',
  Rejected: 'Afgewezen',
  Withdrawn: 'Ingetrokken',
  StartNumberAssigned: 'Startnummer toegekend',
  Final: 'Definitief',
};

export const registrationSourceLabels: Record<string, string> = { App: 'App (lid)', WebForm: 'Formulier (gast)', Portal: 'Portal' };
