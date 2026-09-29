import type { IconName } from './components/icons';

/** Menu van het beheerportal (docs/02 §6, Figma "Beheerportal"); een item is alleen zichtbaar met de bijbehorende permission. */
export interface NavItem {
  label: string;
  to: string;
  /** Eén permission, of meerdere waarvan er één volstaat. */
  permission: string | readonly string[];
  icon: IconName;
  /** Kop in de zijbalk; zonder sectie staat het item bovenaan. */
  section?: 'Content' | 'Leden' | 'Optocht' | 'Toegang' | 'Beheer';
}

export const navItems: readonly NavItem[] = [
  { label: 'Dashboard', to: '/', permission: 'report.view', icon: 'home' },
  { label: 'Agenda', to: '/agenda', permission: 'event.manage', icon: 'agenda', section: 'Content' },
  { label: 'Nieuws', to: '/nieuws', permission: 'news.manage', icon: 'nieuws', section: 'Content' },
  { label: "Foto's", to: '/fotos', permission: 'photo.manage', icon: 'fotos', section: 'Content' },
  {
    label: 'Meldingen',
    to: '/meldingen',
    permission: ['notification.send', 'notification.send.group'],
    icon: 'meldingen',
    section: 'Content',
  },
  { label: 'Leden', to: '/leden', permission: 'member.read', icon: 'leden', section: 'Leden' },
  { label: 'Aanmeldingen', to: '/aanmeldingen', permission: 'member.approve', icon: 'plus', section: 'Leden' },
  { label: 'Accountverzoeken', to: '/accountverzoeken', permission: 'member.approve', icon: 'gebruiker', section: 'Leden' },
  { label: 'Groepen', to: '/groepen', permission: 'member.read', icon: 'groepen', section: 'Leden' },
  { label: 'Ledensync', to: '/ledensync', permission: 'import.run', icon: 'sync', section: 'Leden' },
  { label: 'Rapportage', to: '/rapportage', permission: 'report.view', icon: 'rapport', section: 'Leden' },
  { label: 'Inschrijvingen', to: '/optocht/inschrijvingen', permission: 'parade.read', icon: 'optocht', section: 'Optocht' },
  { label: 'Samenstellen', to: '/optocht/samenstellen', permission: 'parade.read', icon: 'audit', section: 'Optocht' },
  { label: 'Optocht en categorieën', to: '/optocht', permission: 'parade.config', icon: 'instellingen', section: 'Optocht' },
  { label: 'Ledentickets', to: '/tickets', permission: 'ticket.read', icon: 'rollen', section: 'Toegang' },
  { label: 'Toegangslog', to: '/toegangslog', permission: 'ticket.read', icon: 'audit', section: 'Toegang' },
  { label: 'Statistieken', to: '/toegang/statistieken', permission: 'ticket.read', icon: 'rapport', section: 'Toegang' },
  { label: 'Gebruikers', to: '/gebruikers', permission: 'role.manage', icon: 'gebruiker', section: 'Beheer' },
  { label: 'Rollen en rechten', to: '/rollen', permission: 'role.manage', icon: 'rollen', section: 'Beheer' },
  { label: 'Carnavalsjaren', to: '/carnavalsjaren', permission: 'config.manage', icon: 'jaar', section: 'Beheer' },
  { label: 'Configuratie', to: '/configuratie', permission: 'config.manage', icon: 'instellingen', section: 'Beheer' },
  { label: 'AVG-verzoeken', to: '/avg', permission: 'member.privacy', icon: 'download', section: 'Beheer' },
  { label: 'Auditlog', to: '/auditlog', permission: 'audit.read', icon: 'audit', section: 'Beheer' },
];

export function navPermissions(item: NavItem): readonly string[] {
  return typeof item.permission === 'string' ? [item.permission] : item.permission;
}

export function visibleNavItems(permissions: readonly string[]): NavItem[] {
  return navItems.filter((item) => navPermissions(item).some((p) => permissions.includes(p)));
}

/** Zichtbare items gegroepeerd per sectie, in de volgorde van het menu; lege secties vallen weg. */
export function navSections(permissions: readonly string[]): { section: NavItem['section']; items: NavItem[] }[] {
  const result: { section: NavItem['section']; items: NavItem[] }[] = [];
  for (const item of visibleNavItems(permissions)) {
    const last = result.at(-1);
    if (last && last.section === item.section) {
      last.items.push(item);
    } else {
      result.push({ section: item.section, items: [item] });
    }
  }
  return result;
}
