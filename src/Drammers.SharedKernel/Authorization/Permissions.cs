namespace Drammers.SharedKernel.Authorization;

/// <summary>
/// De permissiecatalogus (docs/07 §2): de enige autorisatie-eenheid in code. Een nieuwe permission is een codewijziging
/// plus een migratie (seed); rollen zijn configuratie.
/// </summary>
public static class Permissions
{
    public const string MemberReadOwn = "member.read.own";
    public const string MemberRead = "member.read";
    public const string MemberUpdate = "member.update";
    public const string MemberApprove = "member.approve";
    public const string MemberExport = "member.export";
    public const string MemberBlock = "member.block";
    public const string MemberPrivacy = "member.privacy";
    public const string GuardianReadOwn = "guardian.read.own";
    public const string EventRead = "event.read";
    public const string EventManage = "event.manage";
    public const string NewsRead = "news.read";
    public const string NewsManage = "news.manage";
    public const string PhotoRead = "photo.read";
    public const string PhotoManage = "photo.manage";
    public const string NotificationReadOwn = "notification.read.own";
    public const string NotificationSend = "notification.send";
    public const string NotificationSendGroup = "notification.send.group";
    public const string NotificationSendUrgent = "notification.send.urgent";
    public const string ParadeRead = "parade.read";
    public const string ParadeRegister = "parade.register";
    public const string ParadeUpdate = "parade.update";
    public const string ParadeManage = "parade.manage";
    public const string ParadeManageFinal = "parade.manage-final";
    public const string ParadeAssignStartNumber = "parade.assign-start-number";
    public const string ParadeImportArrivalTimes = "parade.import-arrival-times";
    public const string ParadeExport = "parade.export";
    public const string ParadeConfig = "parade.config";
    public const string TicketReadOwn = "ticket.read.own";
    public const string TicketRead = "ticket.read";
    public const string TicketScan = "ticket.scan";
    public const string TicketScanDetails = "ticket.scan.details";
    public const string TicketManage = "ticket.manage";
    public const string PaymentRead = "payment.read";
    public const string PaymentManage = "payment.manage";
    public const string ReportView = "report.view";
    public const string ImportRun = "import.run";
    public const string AuditRead = "audit.read";
    public const string RoleManage = "role.manage";
    public const string ConfigManage = "config.manage";
    public const string MemberPurge = "member.purge";
    public const string SaleManage = "sale.manage";
    public const string SaleCollect = "sale.collect";
    public const string WebsiteManage = "website.manage";
    public const string ParadeJudge = "parade.judge";
    public const string JuryAssign = "jury.assign";
    public const string JuryManage = "jury.manage";
    public const string ParadeResult = "parade.result";

    /// <summary>Alle permissions met omschrijving en categorie; bron voor de seed en <c>GET /admin/permissions</c>.</summary>
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(MemberReadOwn, "Eigen gegevens en lidmaatschap bekijken", "Leden"),
        new(MemberRead, "Leden bekijken en zoeken", "Leden"),
        new(MemberUpdate, "Lokale ledengegevens wijzigen", "Leden"),
        new(MemberApprove, "Lidmaatschapsaanvragen beoordelen", "Leden"),
        new(MemberExport, "Ledenlijsten exporteren", "Leden"),
        new(MemberBlock, "Accounts en toegang blokkeren", "Leden"),
        new(MemberPrivacy, "AVG-verzoeken afhandelen", "Leden"),
        new(GuardianReadOwn, "Gegevens en meldingen van eigen kinderen", "Leden"),
        new(EventRead, "Leden-events bekijken", "Content"),
        new(EventManage, "Agenda en programma beheren", "Content"),
        new(NewsRead, "Ledennieuws bekijken", "Content"),
        new(NewsManage, "Nieuws beheren en publiceren", "Content"),
        new(PhotoRead, "Ledenfoto's bekijken", "Content"),
        new(PhotoManage, "Albums en foto's beheren", "Content"),
        new(NotificationReadOwn, "Eigen inbox", "Meldingen"),
        new(NotificationSend, "Meldingen versturen naar elke doelgroep", "Meldingen"),
        new(NotificationSendGroup, "Meldingen versturen naar eigen groep(en)", "Meldingen"),
        new(NotificationSendUrgent, "Categorie Dringend gebruiken", "Meldingen"),
        new(ParadeRead, "Optochtinschrijvingen inzien", "Optocht"),
        new(ParadeRegister, "Optochtinschrijving starten", "Optocht"),
        new(ParadeUpdate, "Eigen inschrijving wijzigen binnen het statusbeleid", "Optocht"),
        new(ParadeManage, "Inschrijvingen beoordelen en wijzigen", "Optocht"),
        new(ParadeManageFinal, "Wijzigen na status Final", "Optocht"),
        new(ParadeAssignStartNumber, "Startnummers en volgorde toekennen", "Optocht"),
        new(ParadeImportArrivalTimes, "Aanrijtijden importeren en publiceren", "Optocht"),
        new(ParadeExport, "Optochtexports", "Optocht"),
        new(ParadeConfig, "Optochten en categorieën configureren", "Optocht"),
        new(TicketReadOwn, "Eigen tickets en QR", "Toegang"),
        new(TicketRead, "Tickets en scanlogs inzien", "Toegang"),
        new(TicketScan, "Scanmodus gebruiken (alleen op trusted device)", "Toegang"),
        new(TicketScanDetails, "Extra details bij een scan", "Toegang"),
        new(TicketManage, "Tickets en scanners beheren", "Toegang"),
        new(PaymentRead, "Betalingen en orders inzien", "Financieel"),
        new(PaymentManage, "Refunds en handmatige correcties", "Financieel"),
        new(ReportView, "Rapportages en dashboards", "Beheer"),
        new(ImportRun, "Sync en imports starten, conflicten afhandelen", "Beheer"),
        new(AuditRead, "Auditlog inzien", "Beheer"),
        new(RoleManage, "Rollen, permissions en toewijzingen beheren", "Beheer"),
        new(ConfigManage, "Carnavalsjaar, feature flags, appversie, bewaartermijnen", "Beheer"),
        // Nieuwe permissions achteraan (vaste Id per positie).
        new(MemberPurge, "Alle leden uit de test-/acceptatieomgeving verwijderen (niet in productie)", "Leden"),
        new(SaleManage, "Kaartverkoop beheren: producten, bestellingen, contant, betaallinks en wachtlijst", "Financieel"),
        new(SaleCollect, "Kassa: munten-QR scannen en bestellingen uitgeven", "Financieel"),
        new(WebsiteManage, "Website beheren: homepage, pagina's, kader, prinsen en onderscheidingen", "Content"),
        // Fase 22: jury en uitslag van de optocht.
        new(ParadeJudge, "Jureren in de app (eigen categorieën)", "Optocht"),
        new(JuryAssign, "Juryleden aan categorieën toewijzen en beoordelingen buiten categorie goedkeuren", "Optocht"),
        new(JuryManage, "Juryleden uitnodigen, hoofdjury aanwijzen en de weging instellen", "Optocht"),
        new(ParadeResult, "Uitslag van de optocht inzien, exporteren en publiceren", "Optocht"),
    ];
}

public sealed record PermissionDefinition(string Code, string Description, string Category);
