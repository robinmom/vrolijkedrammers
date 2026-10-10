import type { IconName } from './components/icons';

/** Menu van het beheerportal (docs/02 §6, Figma "Beheerportal"); een item is alleen zichtbaar met de bijbehorende permission. */
export interface NavItem {
  label: string;
  to: string;
  /** Eén permission, of meerdere waarvan er één volstaat. */
  permission: string | readonly string[];
  icon: IconName;
  /** Kop in de zijbalk; zonder sectie staat het item bovenaan. */
  section?:
    | 'Content'
    | 'Website'
    | 'Mailing'
    | 'Adverteerders'
    | 'Leden'
    | 'Dansgarde'
    | 'Prins'
    | 'Optocht'
    | 'Toegang'
    | 'Verkoop'
    | 'Beheer';
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
  // Fase 21a: beheer van de website.
  { label: 'Homepage', to: '/website/homepage', permission: 'website.manage', icon: 'home', section: 'Website' },
  { label: "Pagina's", to: '/website/paginas', permission: 'website.manage', icon: 'rapport', section: 'Website' },
  { label: 'Kader', to: '/website/kader', permission: 'website.manage', icon: 'groepen', section: 'Website' },
  { label: 'Prinsen', to: '/website/prinsen', permission: 'website.manage', icon: 'gebruiker', section: 'Website' },
  { label: 'Onderscheidingen', to: '/website/onderscheidingen', permission: 'website.manage', icon: 'jaar', section: 'Website' },
  { label: 'Instellingen website', to: '/website/instellingen', permission: 'website.manage', icon: 'instellingen', section: 'Website' },
  // Fase 27a: nieuwsbrieven en uitnodigingen.
  { label: 'Mailings', to: '/mailing', permission: 'mailing.manage', icon: 'meldingen', section: 'Mailing' },
  { label: 'Mailinggroepen', to: '/mailing/groepen', permission: 'mailing.manage', icon: 'groepen', section: 'Mailing' },
  // Fase 27b: adverteerders en de campagne voor de Drammerskrant.
  { label: 'Overzicht', to: '/adverteerders', permission: 'advertiser.manage', icon: 'rapport', section: 'Adverteerders' },
  { label: 'Campagne', to: '/adverteerders/campagne', permission: 'advertiser.manage', icon: 'jaar', section: 'Adverteerders' },
  // Fase 27c: incasso van de opgehaalde bijdragen.
  { label: 'Incasso', to: '/adverteerders/incasso', permission: 'advertiser.manage', icon: 'sync', section: 'Adverteerders' },
  // Fase 27e: facturen als PDF.
  { label: 'Facturen', to: '/adverteerders/facturen', permission: 'advertiser.manage', icon: 'download', section: 'Adverteerders' },
  { label: 'Leden', to: '/leden', permission: 'member.read', icon: 'leden', section: 'Leden' },
  { label: 'Aanmeldingen', to: '/aanmeldingen', permission: 'member.approve', icon: 'plus', section: 'Leden' },
  // Fase 26: wijzigingen en het verbreken van combinaties vanuit de app.
  { label: 'Wijzigingsverzoeken', to: '/wijzigingsverzoeken', permission: 'member.update', icon: 'sync', section: 'Leden' },
  { label: 'Accountverzoeken', to: '/accountverzoeken', permission: 'member.approve', icon: 'gebruiker', section: 'Leden' },
  { label: 'Koppelverzoeken', to: '/koppelverzoeken', permission: 'member.read', icon: 'groepen', section: 'Leden' },
  { label: 'Groepen', to: '/groepen', permission: 'member.read', icon: 'groepen', section: 'Leden' },
  { label: 'Ledensync', to: '/ledensync', permission: 'import.run', icon: 'sync', section: 'Leden' },
  { label: 'Rapportage', to: '/rapportage', permission: 'report.view', icon: 'rapport', section: 'Leden' },
  // Fase 25: lidmaatschappen (soorten, tarieven, splitsen); contributie is het overzicht per lid.
  { label: 'Lidmaatschappen', to: '/lidmaatschappen', permission: 'contribution.manage', icon: 'groepen', section: 'Leden' },
  // Fase 23c: SEPA-incasso (pain.008).
  { label: 'Incasso', to: '/incasso', permission: 'contribution.manage', icon: 'sync', section: 'Leden' },
  // Fase 23a: contributie zonder e-Boekhouden.
  { label: 'Contributie', to: '/contributie', permission: 'contribution.manage', icon: 'rollen', section: 'Leden' },
  // Fase 20: jubilarissen per carnavalsjaar.
  { label: 'Jubilarissen', to: '/jubilarissen', permission: 'member.read', icon: 'jaar', section: 'Leden' },
  { label: 'Overzicht', to: '/dansgarde', permission: 'member.read', icon: 'leden', section: 'Dansgarde' },
  { label: 'Dansgroepen', to: '/dansgarde/groepen', permission: 'member.read', icon: 'groepen', section: 'Dansgarde' },
  // 2026-10-10: prins(es) en adjudanten, met hun informatie in de app.
  { label: 'Prins en adjudanten', to: '/prins', permission: 'role.manage', icon: 'rollen', section: 'Prins' },
  { label: 'Inschrijvingen', to: '/optocht/inschrijvingen', permission: 'parade.read', icon: 'optocht', section: 'Optocht' },
  { label: 'Samenstellen', to: '/optocht/samenstellen', permission: 'parade.read', icon: 'audit', section: 'Optocht' },
  { label: 'Aanrijtijden', to: '/optocht/aanrijtijden', permission: 'parade.import-arrival-times', icon: 'agenda', section: 'Optocht' },
  // Fase 22a: jury; de hoofdjury ziet in het portal alleen deze pagina.
  { label: 'Jury', to: '/optocht/jury', permission: 'jury.assign', icon: 'groepen', section: 'Optocht' },
  // Fase 22c: alleen de uitslagcommissie.
  { label: 'Uitslag', to: '/optocht/uitslag', permission: 'parade.result', icon: 'rapport', section: 'Optocht' },
  { label: 'Optocht en categorieën', to: '/optocht', permission: 'parade.config', icon: 'instellingen', section: 'Optocht' },
  { label: 'Ledentickets', to: '/tickets', permission: 'ticket.read', icon: 'rollen', section: 'Toegang' },
  { label: 'Toegangslog', to: '/toegangslog', permission: 'ticket.read', icon: 'audit', section: 'Toegang' },
  { label: 'Statistieken', to: '/toegang/statistieken', permission: 'ticket.read', icon: 'rapport', section: 'Toegang' },
  // Fase 19: één pagina per soort product.
  { label: 'Pronkzitting', to: '/verkoop/pronkzitting', permission: 'sale.manage', icon: 'agenda', section: 'Verkoop' },
  { label: 'Dagkaarten', to: '/verkoop/dagkaarten', permission: 'sale.manage', icon: 'ticket', section: 'Verkoop' },
  { label: 'Activiteiten', to: '/verkoop/activiteiten', permission: 'sale.manage', icon: 'jaar', section: 'Verkoop' },
  { label: 'Munten', to: '/verkoop/munten', permission: 'sale.manage', icon: 'munten', section: 'Verkoop' },
  { label: 'Kassalog', to: '/verkoop/kassalog', permission: 'sale.manage', icon: 'audit', section: 'Verkoop' },
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
