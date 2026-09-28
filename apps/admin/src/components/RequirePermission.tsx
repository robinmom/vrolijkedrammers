import { Navigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useMe } from '../api/hooks';
import { navPermissions, visibleNavItems } from '../navigation';

/** Pagina alleen tonen met (een van) de permission(s); anders naar het eerste toegestane menu-item. */
export function RequirePermission({ permission, children }: { permission: string | readonly string[]; children: ReactNode }) {
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const required = typeof permission === 'string' ? [permission] : permission;
  if (required.some((p) => permissions.includes(p))) {
    return children;
  }
  const first = visibleNavItems(permissions)[0];
  return first && !required.some((p) => navPermissions(first).includes(p)) ? <Navigate to={first.to} replace /> : null;
}
