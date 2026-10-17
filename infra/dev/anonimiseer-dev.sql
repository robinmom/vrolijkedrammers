-- Dev met testgegevens (besluit 2026-10-17, voor de beveiligingsscans): de persoonsgegevens uit de kopie van de echte
-- administratie worden vervangen door testwaarden of gewist. Accounts (inloggen), rollen, website, nieuws, agenda,
-- foto's, programma, optocht-indeling, festival-indeling en producten blijven. Alleen voor DEV.
--   - leden: naam "Test Lid <nummer>", e-mail lid<nummer>@example.com, adres Teststraat, geen telefoon/IBAN;
--     geboortedatum wordt 1 januari van het geboortejaar (leeftijdscategorieën en jubilea blijven kloppen);
--   - adverteerders, bestellingen, wachtlijst, optocht- en festivalinschrijvingen, mailingadressen: testcontact;
--   - weg: lidmaatschapsaanvragen, accountaanvragen, wijzigingsverzoeken, privacyverzoeken, incassobestanden,
--     adverteerdersfacturen, mailingontvangers en afmeldingen, versies/historie met oude gegevens, auditlog,
--     loginhistorie, sync-details en verwerkte outbox-berichten.
-- Eén transactie. $(APPLY) = 0: proefrun (aantallen, alles terugdraaien); 1: vastleggen. Via infra/dev/anonimiseer-dev.sh.
SET XACT_ABORT ON;
SET NOCOUNT ON;
DECLARE @apply bit = $(APPLY);

BEGIN TRANSACTION;

-- ----- Weg -------------------------------------------------------------------------------------------------------
DELETE FROM [membership].[CollectionRun];                 PRINT CONCAT('Incassobestanden:            ', @@ROWCOUNT);
DELETE FROM [membership].[AdvertiserInvoice];             PRINT CONCAT('Adverteerdersfacturen:       ', @@ROWCOUNT);
DELETE FROM [membership].[MemberChangeRequest];           PRINT CONCAT('Wijzigingsverzoeken:         ', @@ROWCOUNT);
DELETE FROM [membership].[CombinationBreakRequest];       PRINT CONCAT('Verbreekverzoeken:           ', @@ROWCOUNT);
DELETE FROM [membership].[GuardianLinkRequest];           PRINT CONCAT('Koppelverzoeken ouders:      ', @@ROWCOUNT);
DELETE FROM [membership].[PrivacyRequest];                PRINT CONCAT('Privacyverzoeken:            ', @@ROWCOUNT);
DELETE FROM [membership].[MembershipApplication];         PRINT CONCAT('Lidmaatschapsaanvragen:      ', @@ROWCOUNT);
DELETE FROM [identity].[AccountRequest];                  PRINT CONCAT('Accountaanvragen:            ', @@ROWCOUNT);
DELETE FROM [identity].[LoginHistory];                    PRINT CONCAT('Loginhistorie:               ', @@ROWCOUNT);
DELETE FROM [notification].[MailingRecipient];            PRINT CONCAT('Mailingontvangers:           ', @@ROWCOUNT);
DELETE FROM [notification].[MailingUnsubscribe];          PRINT CONCAT('Mailingafmeldingen:          ', @@ROWCOUNT);
DELETE FROM [content].[DanceFestivalRegistrationVersion]; PRINT CONCAT('Festival: oude versies:      ', @@ROWCOUNT);
DELETE FROM [parade].[ParadeRegistrationHistory];         PRINT CONCAT('Optocht: historie:           ', @@ROWCOUNT);
DELETE FROM [import].[SyncConflict];                      PRINT CONCAT('Sync: conflicten:            ', @@ROWCOUNT);
DELETE FROM [import].[SyncJobItem];                       PRINT CONCAT('Sync: details:               ', @@ROWCOUNT);
DELETE FROM [audit].[AuditLog];                           PRINT CONCAT('Auditlog:                    ', @@ROWCOUNT);
DELETE FROM [notification].[Outbox] WHERE processed_at IS NOT NULL;
                                                      PRINT CONCAT('Outbox (verwerkt):           ', @@ROWCOUNT);

-- ----- Testwaarden -----------------------------------------------------------------------------------------------
UPDATE [membership].[Member] SET
    first_name = 'Test', name_prefix = NULL, last_name = CONCAT('Lid ', member_number), full_name = CONCAT('Test Lid ', member_number),
    second_member_name = NULL, salutation = NULL,
    email = CASE WHEN email IS NULL THEN NULL ELSE CONCAT('lid', member_number, '@example.com') END,
    phone = NULL, mobile_phone = NULL,
    address_line = CONCAT('Teststraat ', member_number), postal_code = '6999 AA', city = 'Loil',
    birth_date = CASE WHEN birth_date IS NULL THEN NULL ELSE DATEFROMPARTS(YEAR(birth_date), 1, 1) END,
    iban_protected = NULL, account_holder = NULL, jubilee_note = NULL, eb_status_raw = NULL;
PRINT CONCAT('Leden geanonimiseerd:        ', @@ROWCOUNT);

-- Accounts: wie een handmatig toegekende rol heeft (bestuur, beheer, testers) houdt zijn eigen adres om op Dev te
-- kunnen inloggen; alle andere accounts krijgen een testadres.
WITH u AS (
    SELECT display_name, email, ROW_NUMBER() OVER (ORDER BY id) AS n FROM [identity].[User] AS x
    WHERE NOT EXISTS (SELECT 1 FROM [identity].[UserRole] AS ur JOIN [identity].[Role] AS r ON r.id = ur.role_id
                      WHERE ur.user_id = x.id AND r.is_assignable_by_sync = 0))
UPDATE u SET display_name = CONCAT('Test Account ', n), email = CONCAT('account-', n, '@example.com');
PRINT CONCAT('Accounts geanonimiseerd:     ', @@ROWCOUNT);

UPDATE [membership].[GuardianRelation] SET guardian_name = 'Test Ouder', guardian_phone = NULL;
PRINT CONCAT('Ouders:                      ', @@ROWCOUNT);

UPDATE [membership].[Advertiser] SET
    company_name = CONCAT('Testbedrijf ', LEFT(CONVERT(varchar(36), id), 8)), contact_name = 'Test Contact',
    email = CONCAT('adverteerder-', LEFT(CONVERT(varchar(36), id), 8), '@example.com'), phone = NULL, mobile = NULL,
    address_line = 'Teststraat 1', postal_code = '6999 AA', city = 'Loil', website = NULL, notes = NULL,
    iban_protected = NULL, imported_collector_name = NULL;
PRINT CONCAT('Adverteerders:               ', @@ROWCOUNT);

UPDATE [membership].[JubileeInvitation] SET sent_to = 'jubilaris@example.com';
PRINT CONCAT('Jubilea-uitnodigingen:       ', @@ROWCOUNT);

UPDATE [payments].[SaleOrder] SET buyer_name = 'Test Koper', buyer_email = CONCAT('koper-', number, '@example.com'), buyer_phone = NULL, remark = NULL;
PRINT CONCAT('Bestellingen:                ', @@ROWCOUNT);

UPDATE [ticketing].[WaitlistEntry] SET buyer_name = 'Test Wachtlijst', buyer_email = CONCAT('wachtlijst-', LEFT(CONVERT(varchar(36), id), 8), '@example.com'), buyer_phone = NULL, remark = NULL;
PRINT CONCAT('Wachtlijst:                  ', @@ROWCOUNT);

UPDATE [parade].[ParadeRegistration] SET contact_name = 'Test Contact', contact_email = CONCAT('groep-', LEFT(CONVERT(varchar(36), id), 8), '@example.com'),
    contact_phone = NULL, additional_information = NULL;
PRINT CONCAT('Optochtinschrijvingen:       ', @@ROWCOUNT);

UPDATE [content].[DanceFestivalRegistration] SET contact_name = 'Test Contact', email = CONCAT('vereniging-', number, '@example.com'), phone = '0600000000',
    address_line = 'Teststraat 1', postal_code = '6999 AA', city = 'Loil', remarks = NULL, info_request = NULL;
PRINT CONCAT('Festivalinschrijvingen:      ', @@ROWCOUNT);

-- Sleutel is (list_id, email): nummeren zodat het adres per lijst uniek blijft.
WITH a AS (SELECT name, email, ROW_NUMBER() OVER (ORDER BY list_id, email) AS n FROM [notification].[MailingListAddress])
UPDATE a SET name = 'Test', email = CONCAT('adres-', n, '@example.com');
PRINT CONCAT('Losse mailingadressen:       ', @@ROWCOUNT);

IF @apply = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'VASTGELEGD.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PROEFRUN: niets gewijzigd. Vastleggen met --apply.';
END
