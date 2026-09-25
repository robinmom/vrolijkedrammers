import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import { Layout } from './components/Layout';
import { RequirePermission } from './components/RequirePermission';
import { AuditLogPage } from './pages/AuditLogPage';
import { CarnivalYearsPage } from './pages/CarnivalYearsPage';
import { ConfigPage } from './pages/ConfigPage';
import { DashboardPage } from './pages/DashboardPage';
import { RolesPage } from './pages/RolesPage';
import { UserDetailPage } from './pages/UserDetailPage';
import { UsersPage } from './pages/UsersPage';

const rootRoute = createRootRoute({ component: Layout });

function guarded(permission: string, Page: () => React.ReactNode) {
  return function GuardedPage() {
    return (
      <RequirePermission permission={permission}>
        <Page />
      </RequirePermission>
    );
  };
}

const routeTree = rootRoute.addChildren([
  createRoute({ getParentRoute: () => rootRoute, path: '/', component: guarded('report.view', DashboardPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/gebruikers', component: guarded('role.manage', UsersPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/gebruikers/$id', component: guarded('role.manage', UserDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/rollen', component: guarded('role.manage', RolesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/carnavalsjaren', component: guarded('config.manage', CarnivalYearsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/configuratie', component: guarded('config.manage', ConfigPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/auditlog', component: guarded('audit.read', AuditLogPage) }),
]);

export function createAppRouter() {
  return createRouter({ routeTree, basepath: '/beheer' });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
