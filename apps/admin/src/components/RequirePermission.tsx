import { Navigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useMe } from '../api/hooks';
import { visibleNavItems } from '../navigation';

/** Pagina alleen tonen met de permission; anders naar het eerste toegestane menu-item. */
export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  if (permissions.includes(permission)) {
    return children;
  }
  const first = visibleNavItems(permissions)[0];
  return first && first.permission !== permission ? <Navigate to={first.to} replace /> : null;
}
