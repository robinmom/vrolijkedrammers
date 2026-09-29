import type { components } from '@drammers/api-client';

type Schemas = components['schemas'];
export type Registration = Schemas['RegistrationResponse'];
export type RegistrationSummary = Schemas['RegistrationSummaryResponse'];
export type RegistrationStatus = Schemas['RegistrationStatus'];
export type ParadeCategory = Schemas['ParadeCategoryResponse'];
export type BuildLocation = Schemas['BuildLocationResponse'];
export type ValidationIssue = Schemas['ValidationIssueResponse'];
export type Address = Schemas['AddressDto'];

/** Formulier van de wizard (Figma 🏁 Optocht-wizard): alles als tekst, zoals de invoervelden het kennen. */
export interface RegistrationForm {
  groupName: string;
  contactName: string;
  contactPhone: string;
  contactEmail: string;
  categoryId: number | null;
  subject: string;
  subjectDescription: string;
  adultCount: number;
  childrenCount: number;
  /** Muziek bij de groep (fase 12c); `null` = nog niet gekozen. */
  hasMusic: boolean | null;
  build: AddressForm;
  jurySame: boolean;
  jury: AddressForm;
  estimatedLength: string;
  additionalInformation: string;
}

export interface AddressForm {
  street: string;
  houseNumber: string;
  addition: string;
  postalCode: string;
  city: string;
}

const emptyAddress: AddressForm = { street: '', houseNumber: '', addition: '', postalCode: '', city: '' };

export const emptyForm: RegistrationForm = {
  groupName: '',
  contactName: '',
  contactPhone: '',
  contactEmail: '',
  categoryId: null,
  subject: '',
  subjectDescription: '',
  adultCount: 0,
  childrenCount: 0,
  hasMusic: null,
  build: emptyAddress,
  jurySame: true,
  jury: emptyAddress,
  estimatedLength: '',
  additionalInformation: '',
};

export const addressForm = (a: Address | null | undefined): AddressForm => ({
  street: a?.street ?? '',
  houseNumber: a?.houseNumber ?? '',
  addition: a?.addition ?? '',
  postalCode: a?.postalCode ?? '',
  city: a?.city ?? '',
});

const addressDto = (a: AddressForm): Address => ({
  street: a.street.trim() || null,
  houseNumber: a.houseNumber.trim() || null,
  addition: a.addition.trim() || null,
  postalCode: a.postalCode.trim() || null,
  city: a.city.trim() || null,
  country: 'NL',
});

export function formFromRegistration(r: Registration): RegistrationForm {
  return {
    groupName: r.groupName ?? '',
    contactName: r.contactName ?? '',
    contactPhone: r.contactPhoneDisplay ?? r.contactPhone ?? '',
    contactEmail: r.contactEmail ?? '',
    categoryId: r.categoryId ?? null,
    subject: r.subject ?? '',
    subjectDescription: r.subjectDescription ?? '',
    adultCount: r.adultCount,
    childrenCount: r.childrenCount,
    hasMusic: r.hasMusic ?? null,
    build: addressForm(r.buildAddress),
    jurySame: r.juryInspectionSameAsBuildAddress,
    jury: addressForm(r.juryInspectionAddress),
    estimatedLength: r.estimatedLengthMeters != null ? String(r.estimatedLengthMeters).replace('.', ',') : '',
    additionalInformation: r.additionalInformation ?? '',
  };
}

/** Body voor PUT /parade/registrations/{id} en voor het openbare formulier (gasten). */
export function toRequest(form: RegistrationForm, version: string | null): Schemas['UpdateRegistrationRequest'] {
  const length = Number(form.estimatedLength.replace(',', '.'));
  return {
    version,
    groupName: form.groupName.trim() || null,
    contactName: form.contactName.trim() || null,
    contactPhone: form.contactPhone.trim() || null,
    contactEmail: form.contactEmail.trim() || null,
    categoryId: form.categoryId,
    subject: form.subject.trim() || null,
    subjectDescription: form.subjectDescription.trim() || null,
    childrenCount: form.childrenCount,
    adultCount: form.adultCount,
    hasMusic: form.hasMusic,
    buildAddress: addressDto(form.build),
    juryInspectionSameAsBuildAddress: form.jurySame,
    juryInspectionAddress: form.jurySame ? null : addressDto(form.jury),
    estimatedLengthMeters: form.estimatedLength.trim() && Number.isFinite(length) ? Math.round(length * 10) / 10 : null,
    additionalInformation: form.additionalInformation.trim() || null,
  };
}

export const formatAddress = (a: AddressForm) =>
  [`${a.street} ${a.houseNumber}${a.addition ? ` ${a.addition}` : ''}`.trim(), `${a.postalCode} ${a.city}`.trim()]
    .filter(Boolean)
    .join(', ');

export const sameAddress = (a: AddressForm, b: AddressForm) =>
  a.postalCode.replace(/\s/g, '').toUpperCase() === b.postalCode.replace(/\s/g, '').toUpperCase() &&
  a.houseNumber.trim() === b.houseNumber.trim() &&
  a.addition.trim().toUpperCase() === b.addition.trim().toUpperCase();

/** Stappen van de wizard (Figma); gasten hebben geen documentenstap. */
export type StepKey = 'group' | 'contact' | 'category' | 'subject' | 'location' | 'length' | 'documents' | 'review';

export const stepTitles: Record<StepKey, string> = {
  group: 'Groepsgegevens',
  contact: 'Contactpersoon',
  category: 'Categorie en deelnemers',
  subject: 'Onderwerp',
  location: 'Bouwlocatie',
  length: 'Lengte en extra informatie',
  documents: 'Documenten',
  review: 'Controleren en indienen',
};

export const stepsFor = (guest: boolean): StepKey[] =>
  guest
    ? ['group', 'contact', 'category', 'subject', 'location', 'length', 'review']
    : ['group', 'contact', 'category', 'subject', 'location', 'length', 'documents', 'review'];

/** Welke meldingen van de API bij welke stap horen. */
export const stepFields: Record<StepKey, string[]> = {
  group: ['groupName'],
  contact: ['contactName', 'contactPhone', 'contactEmail'],
  category: ['categoryId', 'adultCount', 'childrenCount', 'hasMusic'],
  subject: ['subject', 'subjectDescription'],
  location: ['buildAddress', 'juryInspection'],
  length: ['estimatedLengthMeters', 'additionalInformation'],
  documents: ['documents'],
  review: [],
};

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const filled = (a: AddressForm) =>
  Boolean(a.street.trim() && a.houseNumber.trim() && a.postalCode.trim() && a.city.trim());

/** Lengte-invoer: alleen cijfers en één komma, met hoogstens 1 decimaal (bijv. "12,5"). */
export function lengthInput(value: string): string {
  const [whole = '', ...rest] = value
    .replace('.', ',')
    .replace(/[^0-9,]/g, '')
    .split(',');
  return rest.length ? `${whole.slice(0, 3)},${rest.join('').slice(0, 1)}` : whole.slice(0, 3);
}

/** Minimale controle vóór "Volgende"; de API controleert alles (ook categorieregels) bij opslaan en indienen. */
export function stepMissing(step: StepKey, form: RegistrationForm, subjectRequired: boolean): string | null {
  switch (step) {
    case 'group':
      return form.groupName.trim() ? null : 'Vul de naam van de groep in.';
    case 'contact':
      if (!form.contactName.trim() || !form.contactPhone.trim()) return 'Vul naam en telefoonnummer in.';
      return emailPattern.test(form.contactEmail.trim()) ? null : 'Vul een geldig e-mailadres in.';
    case 'category':
      if (form.categoryId == null) return 'Kies een categorie.';
      return form.adultCount + form.childrenCount > 0 ? null : 'Vul het aantal deelnemers in.';
    case 'subject':
      return subjectRequired && !form.subject.trim() ? 'Vul het onderwerp in.' : null;
    case 'length': {
      const length = Number(form.estimatedLength.replace(',', '.'));
      if (!form.estimatedLength.trim()) return 'Vul de geschatte lengte in.';
      return length > 0 && length <= 100 ? null : 'De lengte is groter dan 0 en hoogstens 100 meter.';
    }
    case 'location':
      if (!filled(form.build)) return 'Vul het bouwadres volledig in.';
      return form.jurySame || filled(form.jury) ? null : 'Vul het adres voor de jury volledig in.';
    default:
      return null;
  }
}

/** Statusteksten voor leden en gasten: pas na goedkeuring door de optochtcommissie is een inschrijving definitief. */
export const statusLabels: Record<RegistrationStatus, string> = {
  Draft: 'Concept',
  Submitted: 'Ingediend – wacht op beoordeling',
  UnderReview: 'In behandeling bij de commissie',
  AdditionalInformationRequired: 'Aanvulling gevraagd',
  Approved: 'Goedgekeurd – definitief',
  Rejected: 'Afgewezen',
  Withdrawn: 'Ingetrokken',
  StartNumberAssigned: 'Startnummer toegekend',
  Final: 'Definitief',
};

export const statusBadge = (status: RegistrationStatus): 'highlight' | 'category' | 'youth' =>
  status === 'Approved' || status === 'StartNumberAssigned' || status === 'Final'
    ? 'youth'
    : status === 'AdditionalInformationRequired' || status === 'Draft'
      ? 'highlight'
      : 'category';

export function categoryRule(c: ParadeCategory): string {
  const who =
    c.participantCountBasis === 'ChildrenOnly'
      ? 'kinderen'
      : c.participantCountBasis === 'AdultsOnly'
        ? 'volwassenen'
        : 'deelnemers';
  const min = c.minimumParticipants;
  const max = c.maximumParticipants;
  const range =
    min != null && max != null
      ? `${min} tot ${max}`
      : min != null
        ? `${min} of meer`
        : max != null
          ? `maximaal ${max}`
          : null;
  return (
    [range ? `${range} ${who}` : null, c.hasVehicle ? 'met voertuig' : null].filter(Boolean).join(' · ') ||
    'Geen aantalseis'
  );
}

export interface Problem {
  message: string;
  issues: ValidationIssue[];
}

/** Uitleg uit ProblemDetails (met de blokkerende meldingen van de inschrijfregels), anders een algemene tekst. */
export function problemFrom(status: number, error: unknown): Problem {
  if (status === 429)
    return { message: 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.', issues: [] };
  if (status === 412)
    return { message: 'Iemand anders heeft deze inschrijving net gewijzigd. Open hem opnieuw.', issues: [] };
  const body = error as { detail?: string; issues?: ValidationIssue[] } | undefined;
  return {
    message:
      body?.detail ??
      (status === 400 || status === 422
        ? 'Controleer de ingevulde gegevens.'
        : 'Er ging iets mis. Probeer het later opnieuw.'),
    issues: (body?.issues ?? []).filter((i) => i.severity === 'Block'),
  };
}
