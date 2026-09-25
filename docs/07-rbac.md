# 07 – Rollen- en rechtenmodel (RBAC)

> Status: v0.4 · 2026-09-25 · accountmodel: alleen leden + ouders (B-05, ADR-014) · fase 3 gebouwd (§8)

## 1. Principes

1. **Permissions zijn de enige autorisatie-eenheid in code.** Er zijn geen checks als `if (user.IsBestuur)`. Een endpoint of actie vereist één of meer permissions.
2. **Rollen zijn configureerbare bundels** permissions, beheerd in het portal (`role.manage`). Een gebruiker kan meerdere rollen hebben (N:M, met optionele geldigheid).
3. **Resource-scoping** bovenop permissions: sommige permissions gelden alleen voor "eigen" objecten (eigen inschrijving, eigen kind, eigen groep). Dat wordt afgedwongen met resource-based authorization handlers.
4. **Audiences ≠ permissions**: wie content *ziet* (events, nieuws, foto's) wordt bepaald door audience-regels (iedereen/leden/rol/groep/lid). Wie content *beheert* wordt bepaald door permissions.
5. **Deny by default**: elk endpoint is standaard `[Authorize]`; publieke endpoints zijn expliciet `[AllowAnonymous]` en worden in de tests opgesomd.
6. De server is leidend; de app/portal gebruikt `GET /me` → `permissions[]` alleen voor het tonen/verbergen van UI.

## 2. Permissiecatalogus

| Permission | Omschrijving | Scope |
|---|---|---|
| `member.read.own` | Eigen gegevens/lidmaatschap bekijken | eigen |
| `member.read` | Leden bekijken en zoeken | alle |
| `member.update` | Lokale ledengegevens wijzigen (status-override, validiteit, foto, groepen, guardians) | alle |
| `member.approve` | Lidmaatschapsaanvragen beoordelen | alle |
| `member.export` | Ledenlijsten exporteren | alle |
| `member.block` | Account/toegang blokkeren | alle |
| `member.privacy` | AVG-verzoeken afhandelen (export, anonimiseren) | alle |
| `guardian.read.own` | Gegevens/meldingen van eigen kinderen | eigen kinderen |
| `event.read` | Leden-events bekijken (audience-regels gelden daarnaast) | — |
| `event.manage` | Agenda/programma beheren | alle |
| `news.read` | Ledennieuws bekijken | — |
| `news.manage` | Nieuws beheren en publiceren | alle |
| `photo.read` | Ledenfoto's bekijken | — |
| `photo.manage` | Albums en foto's beheren | alle |
| `notification.read.own` | Eigen inbox | eigen |
| `notification.send` | Meldingen versturen naar elke doelgroep | alle |
| `notification.send.group` | Meldingen versturen naar eigen groep(en) (bijv. dansgarde-leiding) | eigen groepen |
| `notification.send.urgent` | Categorie "Dringend" gebruiken | — |
| `parade.read` | Optochtinschrijvingen inzien (alle) | alle |
| `parade.register` | Inschrijving starten | — |
| `parade.update` | Eigen inschrijving wijzigen binnen het statusbeleid | eigen (manager) |
| `parade.manage` | Inschrijvingen beoordelen/wijzigen, status, gemeten lengte | alle |
| `parade.manage-final` | Wijzigen na status Final | alle |
| `parade.assign-start-number` | Startnummers/volgorde toekennen en publiceren | alle |
| `parade.import-arrival-times` | Aanrijtijden importeren/publiceren | alle |
| `parade.export` | Optochtexports | alle |
| `parade.config` | Optochten en categorieën configureren | alle |
| `ticket.read.own` | Eigen tickets/QR | eigen |
| `ticket.read` | Tickets en scanlogs inzien | alle |
| `ticket.scan` | Scanmodus gebruiken (alleen op trusted device) | — |
| `ticket.scan.details` | Bij scan extra details zien (vorige scanner) | — |
| `ticket.manage` | Tickets blokkeren, intrekken, heruitgeven, bandjesbeleid, scanners goedkeuren | alle |
| `payment.read` | Betalingen/orders inzien | alle |
| `payment.manage` | Refunds, handmatige correcties | alle |
| `report.view` | Rapportages en dashboards | alle |
| `import.run` | Sync/imports starten en conflicten afhandelen | alle |
| `audit.read` | Auditlog inzien | alle |
| `role.manage` | Rollen, permissions en toewijzingen beheren | alle |
| `config.manage` | Carnavalsjaar, feature flags, appversie, bewaartermijnen | alle |

Permissions leven in code (constants) en in de database (seed via migratie). Een nieuwe permission = codewijziging + migratie. Rollen en koppelingen = configuratie.

## 3. Standaardrollen en matrix

Legenda: ● ja · ◐ scoped/eigen · leeg = nee.

| Permission | Lid (label "Carnavalist") | Groeps­verantw. | Kaderlid | Dansgarde leiding | Dansgarde lid | Ouder/ verzorger | Raad van Elf | Scanner | Optocht­commissie | Redactie | Bestuur | Beheerder (IT) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| member.read.own | ● | | ● | ● | ● | | ● | ● | ● | ● | ● | ● |
| member.read | | | | | | | | | | | ● | ● |
| member.update | | | | | | | | | | | ● | ● |
| member.approve | | | | | | | | | | | ● | |
| member.export | | | | | | | | | | | ● | |
| member.block | | | | | | | | | | | ● | ● |
| member.privacy | | | | | | | | | | | ● | |
| guardian.read.own | | | | | | ◐ | | | | | | |
| event.read | ● | | ● | ● | ● | | ● | ● | ● | ● | ● | ● |
| event.manage | | | | | | | | | | ● | ● | |
| news.read | ● | | ● | ● | ● | | ● | ● | ● | ● | ● | ● |
| news.manage | | | | | | | | | | ● | ● | |
| photo.read | ● | | ● | ● | ● | | ● | ● | ● | ● | ● | ● |
| photo.manage | | | | | | | | | | ● | ● | |
| notification.read.own | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| notification.send | | | | | | | | | | ● | ● | |
| notification.send.group | | | | ◐ | | | | | ◐ | | | |
| notification.send.urgent | | | | | | | | | | | ● | |
| parade.register | ● | ● | | | | | | | | | ● | |
| parade.update | | ◐ | | | | | | | | | | |
| parade.read | | | | | | | | | ● | | ● | |
| parade.manage | | | | | | | | | ● | | ● | |
| parade.manage-final | | | | | | | | | ◐ (voorzitter) | | ● | |
| parade.assign-start-number | | | | | | | | | ● | | ● | |
| parade.import-arrival-times | | | | | | | | | ● | | ● | |
| parade.export | | | | | | | | | ● | | ● | |
| parade.config | | | | | | | | | ● | | ● | |
| ticket.read.own | ● | | ● | ● | ● | ◐ (kinderen) | ● | ● | ● | ● | ● | ● |
| ticket.scan | | | | | | | ◐ | ● | | | ● | |
| ticket.scan.details | | | | | | | | | | | ● | |
| ticket.read | | | | | | | | | | | ● | |
| ticket.manage | | | | | | | | | | | ● | |
| payment.read | | | | | | | | | | | ● | |
| payment.manage | | | | | | | | | | | ◐ (penningmeester) | |
| report.view | | | | | | | ◐ | | ◐ (optocht) | | ● | |
| import.run | | | | | | | | | | | ● | ● |
| audit.read | | | | | | | | | | | ● | ● |
| role.manage | | | | | | | | | | | ● | ● |
| config.manage | | | | | | | | | | | ● | ● |

Toelichting:
- **Accountmodel (B-05, ADR-014)**: alleen **leden** en **ouders/verzorgers van minderjarige leden** hebben een account. Er is geen rol voor niet-leden; niet-leden gebruiken de app als gast en schrijven zich voor de optocht in via het openbare webformulier (zonder account).
- **Lid** (systeemrol; UI-label "Carnavalist", §4.1) wordt bij het aanmaken van het account toegekend (ADR-014) en vervalt automatisch bij `Inactive/Suspended/Deceased` (het Entra-account wordt dan uitgeschakeld).
- **Groepsverantwoordelijke** is een lid; de rol wordt automatisch toegekend bij het eerste optochtconcept via de app (per carnavalsjaar) en dient als doelgroep voor optochtmeldingen; `parade.update` is resource-scoped (alleen eigen inschrijvingen). Inschrijven zelf kan ieder lid (`parade.register` zit in de rol Lid).
- **Ouder/verzorger** (systeemrol) wordt toegekend aan het ouderaccount dat bij goedkeuring of via het portal is aangemaakt (ADR-014). Een ouder ziet **alleen** gegevens, meldingen en de QR van de eigen kinderen, **geen ledencontent** (tenzij de ouder zelf ook lid is en dus ook de rol Lid heeft).
- **Raad van Elf** krijgt standaard geen `ticket.scan`. Scanrecht wordt per persoon via de extra rol **Scanner** toegekend (eventueel tijdelijk met `valid_from/valid_to`). Dit volgt "afhankelijk van rechten" uit §4.5.
- **Kaderlid** onderscheidt zich vooral via **audiences** (kader-events/-nieuws) en niet via extra beheerrechten.
- **Dansgarde lid** (meisje) kan minderjarig zijn: meldingen gaan (ook) naar de gekoppelde ouders (`GuardianMemberRelation.receives_notifications`).
- **Beheerder (IT)** heeft technische rechten maar bewust geen inhoudelijke rechten op betalingen/ledengoedkeuring (functiescheiding).
- Het bestuur kan in het portal aparte rollen maken (bijv. "Penningmeester", "Secretaris", "Optochtvoorzitter") met fijnmazigere sets.

## 4. Audience-regels (zichtbaarheid content)

Een item is zichtbaar voor gebruiker U als:

```
visibility = Public
OR (visibility = Members AND U heeft rol Lid (actief lidmaatschap))
OR (visibility = Restricted AND EXISTS audience-regel r:
      r.type = Role   AND U heeft rol r.ref (geldig)
   OR r.type = Group  AND U (of kind van U) zit in groep r.ref
   OR r.type = Member AND U.member_id = r.ref (of kind van U))
OR U heeft de beheer-permission voor dat contenttype
```

- Wordt server-side geëvalueerd in één query-filter (EF Core `IQueryable`-extensie `VisibleTo(user)`), met tests per combinatie.
- Gasten krijgen uitsluitend `Public` + `publication_status = Published` + binnen publicatievenster.
- Blob-bestanden van niet-publieke content zijn alleen via kortlevende SAS-urls bereikbaar, uitgegeven na dezelfde check.

## 5. Implementatie in ASP.NET Core

```csharp
// Permission-constants (single source of truth)
public static class Permissions
{
    public const string ParadeAssignStartNumber = "parade.assign-start-number";
    // ...
}

// Endpoint
[HttpPut("admin/parade/registrations/{id:guid}/start-number")]
[RequirePermission(Permissions.ParadeAssignStartNumber)]
public Task<IActionResult> SetStartNumber(Guid id, SetStartNumberRequest req) { ... }

// Resource-based voorbeeld
var auth = await _authorization.AuthorizeAsync(User, registration, ParadePolicies.CanEdit);
```

- `RequirePermissionAttribute` → dynamische policy `perm:<code>` → `PermissionHandler` controleert de claims-set uit `IPermissionService`.
- `IPermissionService.GetPermissionsAsync(userId)` → cache (IMemoryCache, 5 min, sleutel inclusief `permissions_version`). Rolwijziging verhoogt `permissions_version` → directe invalidatie op dezelfde instance. Met 1 instance (B1) is een distributed cache niet nodig; bij scale-out: korte TTL (≤ 5 min) is acceptabel.
- Permissions worden **niet** in het access token van Entra gezet (rollen wijzigen vaak, tokens leven tot 60 min); ze worden per request uit de cache geladen.
- `ParadeRegistrationAuthorizationHandler`: `CanView` (manager of `parade.read`), `CanEdit(fields)` (combinatie van status, `ParadeStatusEditPolicy`, manager/permission).
- `GuardianAuthorizationHandler`: toegang tot kindgegevens alleen met een actieve relatie.

## 6. Beheer van rollen

- Portal "Rollen/rechten": rollen aanmaken/wijzigen, permissions aanvinken per categorie, gebruikers toewijzen (met geldigheid).
- Systeemrollen (`is_system`) kunnen niet verwijderd worden. `role.manage` en `audit.read` kunnen niet uit de rol "Bestuur" worden verwijderd zolang er geen andere houder is (lock-out-preventie).
- Minimaal 2 gebruikers met `role.manage` (bewaakt; waarschuwing in het dashboard).
- Elke toewijzing of wijziging → AuditLog (`role.assigned`, `role.revoked`, `role.permissions.changed`).

## 7. Tests (zie 12)

- Een permission-matrix-test genereert voor **elk endpoint** × **elke standaardrol** het verwachte resultaat (200/403/401) uit een tabel en vergelijkt die met de werkelijkheid (WebApplicationFactory + test-JWT).
- Een reflectietest: elk controller-endpoint heeft `[RequirePermission]` of `[AllowAnonymous]`, zodat er geen "vergeten" endpoints zijn.
- Audience-filtertests per combinatie (gast, lid, rol, groep, individueel, ouder van kind).

## 8. Implementatie fase 3 (2026-09-25)

- **Seed:** `DefaultRoles` (Infrastructure) is de bron voor de migratie. Rolcodes: `lid`, `groepsverantwoordelijke`, `kaderlid`, `dansgarde-leiding`, `dansgarde-lid`, `ouder`, `raad-van-elf`, `scanner`, `optochtcommissie`, `redactie`, `bestuur`, `beheerder-it`. Systeemrollen (niet te verwijderen): `lid`, `ouder`, `bestuur`, `beheerder-it`.
- **Scoped (◐):** de permission zit in de rol, de scoping (eigen inschrijving, eigen kinderen, eigen groep) komt met de resource-handlers in de betreffende fase. Uitzonderingen voor één functie zijn **niet** standaard toegekend: `parade.manage-final` voor de optochtvoorzitter, `payment.manage` voor de penningmeester en `ticket.scan` voor de Raad van Elf. Die gaan via een eigen rol (bijv. "Penningmeester") of de rol Scanner.
- **401 of 403:** een ongeldig token (handtekening, issuer, audience, looptijd) geeft **401**. Een geldig token zonder toegang geeft **403**: onbekende `oid` (geen JIT), geblokkeerd of inactief account, ontbrekende `environmentAccess` in Dev/Acc, ontbrekende permission.
- **Annotaties:** `[RequirePermission(...)]`, `[RequireActiveUser]` (ingelogd, bijv. `GET /me`) of `[AllowAnonymous]`. De fallbackpolicy weigert endpoints zonder annotatie. Een onbekende route geeft gewoon 404. De reflectietest somt de publieke endpoints op, en de permission-matrix-test controleert elk beschermd endpoint tegen elke standaardrol.
- **Cache:** `IUserAccessService` cachet de gebruiker met rollen en permissions 5 minuten. Een rol- of statuswijziging maakt de cache op dezelfde instantie direct ongeldig en verhoogt `permissions_version`.
- **Lock-out-preventie:** elke wijziging aan rollen, rolrechten of blokkades wordt teruggedraaid (409 `LOCKOUT_PREVENTED`) als er daarna geen actieve gebruiker met `role.manage` overblijft.
- **Portal-MFA:** geen Conditional Access (besluit B-02-MFA, 2026-09-25). Beheerders loggen in met een e-mailcode naar een vrolijkedrammers.nl-mailbox met MFA. Gecontroleerd met een echt Dev-token: `environmentAccess` komt mee (`dev,acc`), `amr` niet. Een MFA-controle in de API is daarom niet mogelijk en niet gebouwd.
