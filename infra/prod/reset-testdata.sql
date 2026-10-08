-- Eenmalig na de kopie Dev → Prod (besluit product owner, 2026-10-09): testgegevens uit Productie halen.
--   - adverteerders: alles weg (adverteerders, alle jaren, facturen, incassobestanden voor adverteerders);
--   - verkoop: alle bestellingen (dagkaarten, pronkzitting, activiteiten, munten), uitgegeven kaarten, scans, kassalog
--     en wachtlijsten; bestelnummers beginnen opnieuw. De producten zelf blijven;
--   - ledentickets: alle tickets (Mijn QR) en toegangsscans; een lid krijgt bij het openen van Mijn QR een nieuw ticket;
--   - optocht: alle inschrijvingen met historie, documenten, beheerders, juryscores, buitenmededinging, bouwlocaties,
--     foto-koppelingen en gepubliceerde aanrijtijden/uitslag; opgavenummers beginnen weer bij 1. De optocht,
--     categorieën, wijzigingsregels en jury-indeling blijven.
-- Leden, accounts, rollen, website, nieuws, agenda, foto's, mailing en contributie blijven ongemoeid; de auditlog ook.
--
-- Alles in één transactie. $(APPLY) = 0: proefrun (toont de aantallen en draait alles terug); 1: vastleggen.
-- Uitvoeren via infra/prod/reset-testdata.sh (één batch, geen GO: de variabelen moeten blijven bestaan).
SET XACT_ABORT ON;
SET NOCOUNT ON;
DECLARE @apply bit = $(APPLY);
DECLARE @n int;

BEGIN TRANSACTION;

-- ----- Adverteerders --------------------------------------------------------------------------------------------
DELETE FROM membership.CollectionRun WHERE kind = 'Advertisers'; -- regels gaan mee (cascade)
PRINT CONCAT('Incassobestanden adverteerders: ', @@ROWCOUNT);
DELETE FROM membership.AdvertiserInvoice;
PRINT CONCAT('Facturen adverteerders:         ', @@ROWCOUNT);
DELETE FROM membership.AdvertiserYear;
PRINT CONCAT('Adverteerders per jaar:         ', @@ROWCOUNT);
DELETE FROM membership.Advertiser;
PRINT CONCAT('Adverteerders:                  ', @@ROWCOUNT);

-- ----- Verkoop (producten blijven) ------------------------------------------------------------------------------
DELETE FROM ticketing.TokenScan;
PRINT CONCAT('Kassalog (munten):              ', @@ROWCOUNT);
DELETE FROM ticketing.AccessScan;
PRINT CONCAT('Toegangsscans:                  ', @@ROWCOUNT);
DELETE FROM ticketing.WaitlistEntry;
PRINT CONCAT('Wachtlijst:                     ', @@ROWCOUNT);
DELETE FROM ticketing.OrderTicket;
PRINT CONCAT('Uitgegeven kaarten/munten:      ', @@ROWCOUNT);
DELETE FROM payments.SaleOrder;
PRINT CONCAT('Bestellingen:                   ', @@ROWCOUNT);
UPDATE payments.SaleOrderSequence SET last_number = 0;

-- ----- Ledentickets (Mijn QR) -----------------------------------------------------------------------------------
DELETE FROM ticketing.Ticket;
PRINT CONCAT('Ledentickets:                   ', @@ROWCOUNT);

-- ----- Optocht (instellingen blijven) ---------------------------------------------------------------------------
UPDATE content.Photo SET registration_id = NULL WHERE registration_id IS NOT NULL;
PRINT CONCAT('Foto''s losgekoppeld:            ', @@ROWCOUNT);
DELETE FROM parade.JudgingOutsideReview;
DELETE FROM parade.JudgingScore;
PRINT CONCAT('Juryscores:                     ', @@ROWCOUNT);
DELETE FROM parade.JudgingSubmission;
SELECT @n = COUNT(*) FROM parade.ParadeDocument;
DELETE FROM parade.ParadeRegistration; -- historie, statushistorie, beheerders en documenten gaan mee (cascade)
PRINT CONCAT('Optochtinschrijvingen:          ', @@ROWCOUNT, ' (documenten: ', @n, ')');
DELETE FROM parade.ParadeBuildLocation;
PRINT CONCAT('Bouwlocaties:                   ', @@ROWCOUNT);
UPDATE parade.ParadeNumberSequence SET last_registration_number = 0;
UPDATE parade.Parade
SET arrival_times_published_at = NULL, results_published_at = NULL, results_published_by = NULL, results_album_id = NULL;

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
