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
import { ParadeCompositionPage } from './pages/ParadeCompositionPage';
import { JuryPage } from './pages/JuryPage';
import { ResultsPage } from './pages/ResultsPage';
import { ParadePage } from './pages/ParadePage';
import { TicketsPage } from './pages/TicketsPage';
import { PronkzittingPage } from './pages/PronkzittingPage';
import { KassaLogPage } from './pages/KassaLogPage';
import { AwardsPage } from './pages/AwardsPage';
import { KaderPage } from './pages/KaderPage';
import { PrincesPage } from './pages/PrincesPage';
import { WebsiteContentPagesPage, WebsitePageEditorPage } from './pages/WebsiteContentPages';
import { MailingEditorPage, MailingsPage } from './pages/MailingPages';
import { MailingListEditorPage, MailingListsPage } from './pages/MailingListPages';
import { WebsiteHomePage, WebsiteSettingsPage } from './pages/WebsiteHomePage';
import { DayTicketsPage, EventTicketsPage, TokensPage } from './pages/SalesKindPage';
import { AccessLogPage } from './pages/AccessLogPage';
import { ArrivalTimesPage } from './pages/ArrivalTimesPage';
import { DanceGroupsPage } from './pages/DanceGroupsPage';
import { DansgardePage } from './pages/DansgardePage';
import { GuardianRequestsPage } from './pages/GuardianRequestsPage';
import { AccessStatsPage } from './pages/AccessStatsPage';
import { ParadeRegistrationDetailPage, ParadeRegistrationsPage } from './pages/ParadeRegistrationsPage';
import { NotificationComposerPage, NotificationDetailPage, NotificationsPage } from './pages/NotificationsPage';
import { AlbumEditorPage, PhotosPage } from './pages/PhotosPage';
import { ReportsPage } from './pages/ReportsPage';
import { JubileesPage } from './pages/JubileesPage';
import { ContributionsPage } from './pages/ContributionsPage';
import { MembershipsPage } from './pages/MembershipsPage';
import { MemberRequestsPage } from './pages/MemberRequestsPage';
import { CollectionsPage } from './pages/CollectionsPage';
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
  createRoute({ getParentRoute: () => rootRoute, path: '/website/homepage', component: guarded('website.manage', WebsiteHomePage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/paginas', component: guarded('website.manage', WebsiteContentPagesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/paginas/$id', component: guarded('website.manage', WebsitePageEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/mailing', component: guarded('mailing.manage', MailingsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/mailing/groepen', component: guarded('mailing.manage', MailingListsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/mailing/groepen/$id', component: guarded('mailing.manage', MailingListEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/mailing/$id', component: guarded('mailing.manage', MailingEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/kader', component: guarded('website.manage', KaderPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/prinsen', component: guarded('website.manage', PrincesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/onderscheidingen', component: guarded('website.manage', AwardsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/website/instellingen', component: guarded('website.manage', WebsiteSettingsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/fotos/$id', component: guarded('photo.manage', AlbumEditorPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/leden', component: guarded('member.read', MembersPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/leden/$id', component: guarded('member.read', MemberDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht', component: guarded('parade.config', ParadePage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/inschrijvingen', component: guarded('parade.read', ParadeRegistrationsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/toegangslog', component: guarded('ticket.read', AccessLogPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/toegang/statistieken', component: guarded('ticket.read', AccessStatsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/tickets', component: guarded('ticket.read', TicketsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/verkoop/pronkzitting', component: guarded('sale.manage', PronkzittingPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/verkoop/dagkaarten', component: guarded('sale.manage', DayTicketsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/verkoop/activiteiten', component: guarded('sale.manage', EventTicketsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/verkoop/munten', component: guarded('sale.manage', TokensPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/verkoop/kassalog', component: guarded('sale.manage', KassaLogPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/aanrijtijden', component: guarded('parade.import-arrival-times', ArrivalTimesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/samenstellen', component: guarded('parade.read', ParadeCompositionPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/jury', component: guarded('jury.assign', JuryPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/optocht/uitslag', component: guarded('parade.result', ResultsPage) }),
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
  createRoute({ getParentRoute: () => rootRoute, path: '/koppelverzoeken', component: guarded('member.read', GuardianRequestsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/dansgarde', component: guarded('member.read', DansgardePage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/dansgarde/groepen', component: guarded('member.read', DanceGroupsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/groepen/$id', component: guarded('member.read', GroupDetailPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/rapportage', component: guarded('report.view', ReportsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/jubilarissen', component: guarded('member.read', JubileesPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/contributie', component: guarded('contribution.manage', ContributionsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/lidmaatschappen', component: guarded('contribution.manage', MembershipsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/wijzigingsverzoeken', component: guarded('member.update', MemberRequestsPage) }),
  createRoute({ getParentRoute: () => rootRoute, path: '/incasso', component: guarded('contribution.manage', CollectionsPage) }),
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
