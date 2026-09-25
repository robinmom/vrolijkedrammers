/** Menu van het beheerportal (docs/02 §6); een item is alleen zichtbaar met de bijbehorende permission. */
export interface NavItem {
  label: string;
  to: string;
  permission: string;
}

export const navItems: readonly NavItem[] = [
  { label: 'Dashboard', to: '/', permission: 'report.view' },
  { label: 'Gebruikers', to: '/gebruikers', permission: 'role.manage' },
  { label: 'Rollen en rechten', to: '/rollen', permission: 'role.manage' },
  { label: 'Carnavalsjaren', to: '/carnavalsjaren', permission: 'config.manage' },
  { label: 'Configuratie', to: '/configuratie', permission: 'config.manage' },
  { label: 'Auditlog', to: '/auditlog', permission: 'audit.read' },
];

export function visibleNavItems(permissions: readonly string[]): NavItem[] {
  return navItems.filter((item) => permissions.includes(item.permission));
}
