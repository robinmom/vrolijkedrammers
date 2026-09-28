import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import { Layout } from './components/Layout';
import { RequirePermission } from './components/RequirePermission';
import { AccountRequestsPage } from './pages/AccountRequestsPage';
import { ApplicationDetailPage, ApplicationsPage } from './pages/ApplicationsPage';
import { AuditLogPage } from './pages/AuditLogPage';
import { PrivacyRequestsPage } from './pages/PrivacyRequestsPage';
import { CarnivalYearsPage } from './pages/CarnivalYearsPage';
import { ConfigPage } from './pages/ConfigPage';
import { DashboardPage } from './pages/DashboardPage';
import { EventEditorPage, EventsPage } from './pages/EventsPage';
import { GroupDetailPage, GroupsPage } from './pages/GroupsPage';
import { MemberDetailPage } from './pages/MemberDetailPage';
import { MembersPage } from './pages/MembersPage';
import { MemberSyncPage, SyncJobPage } from './pages/MemberSyncPage';
import { NewsEditorPage, NewsPage } from './pages/NewsPage';
import { ParadePage } from './pages/ParadePage';
import { ParadeRegistrationDetailPage, ParadeRegistrationsPage } from './pages/ParadeRegistrationsPage';
import { NotificationComposerPage, NotificationDetailPage, NotificationsPage } from './pages/NotificationsPage';
import { AlbumEditorPage, PhotosPage } from './pages/PhotosPage';
import { ReportsPage } from './pages/ReportsPage';
import { RolesPage } from './pages/RolesPage';
import { UserDetailPage } from './pages/UserDetailPage';
import { UsersPage } from './pages/UsersPage';

const rootRoute = createRootRoute({ component: Layout });

/** Meldingen: met notification.send (elke doelgroep) of notification.send.group (eigen groepen). */
const NOTIFY = ['notification.send', 'notification.send.group'] as const;

function guarded(permission: string | readonly string[], Page: () => React.ReactNode) {
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
  createRoute({ getParentRoute: () => rootRoute, path: '/agenda', component: guarded('event.manage', EventsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/agenda/$id', component: guarded('event.manage', EventEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/nieuws', component: guarded('news.manage', NewsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/nieuws/$id', component: guarded('news.manage', NewsEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/fotos', component: guarded('photo.manage', PhotosPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/fotos/$id', component: guarded('photo.manage', AlbumEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/leden', component: guarded('member.read', MembersPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/leden/$id', component: guarded('member.read', MemberDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht', component: guarded('parade.config', ParadePage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/inschrijvingen', component: guarded('parade.read', ParadeRegistrationsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/inschrijvingen/$id', component: guarded('parade.read', ParadeRegistrationDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/meldingen', component: guarded(NOTIFY, NotificationsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/meldingen/nieuw', component: guarded(NOTIFY, NotificationComposerPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/meldingen/$id', component: guarded(NOTIFY, NotificationDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ledensync', component: guarded('import.run', MemberSyncPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ledensync/$id', component: guarded('import.run', SyncJobPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/aanmeldingen', component: guarded('member.approve', ApplicationsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/aanmeldingen/$id', component: guarded('member.approve', ApplicationDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/accountverzoeken', component: guarded('member.approve', AccountRequestsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/groepen', component: guarded('member.read', GroupsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/groepen/$id', component: guarded('member.read', GroupDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/rapportage', component: guarded('report.view', ReportsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/gebruikers', component: guarded('role.manage', UsersPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/gebruikers/$id', component: guarded('role.manage', UserDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/rollen', component: guarded('role.manage', RolesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/carnavalsjaren', component: guarded('config.manage', CarnivalYearsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/configuratie', component: guarded('config.manage', ConfigPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/avg', component: guarded('member.privacy', PrivacyRequestsPage) }),
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
