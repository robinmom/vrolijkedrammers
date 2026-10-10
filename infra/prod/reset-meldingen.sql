-- Eenmalig (besluit product owner, 2026-10-10): de meldingen uit de testperiode in Dev (meegekopieerd naar Prod) weg.
-- Alle pushmeldingen met hun ontvangers en bezorgingen; wat in de app onder Meer → Meldingen en in het portal onder
-- Meldingen stond. Meldingsvoorkeuren, pushtokens en de auditlog blijven. Nog niet verstuurde meldingen in de outbox
-- (verzenden en ontvangstbewijzen) gaan ook weg.
--
-- Alles in één transactie. $(APPLY) = 0: proefrun (toont de aantallen en draait alles terug); 1: vastleggen.
-- Uitvoeren via infra/prod/reset-testdata.sh meldingen [--apply].
SET XACT_ABORT ON;
SET NOCOUNT ON;
DECLARE @apply bit = $(APPLY);
DECLARE @n int;

BEGIN TRANSACTION;

SELECT @n = COUNT(*) FROM notification.NotificationDelivery;
DELETE FROM notification.NotificationRecipient; -- bezorgingen gaan mee (cascade)
PRINT CONCAT('Ontvangers:   ', @@ROWCOUNT, ' (bezorgingen: ', @n, ')');
DELETE FROM notification.Notification;
PRINT CONCAT('Meldingen:    ', @@ROWCOUNT);
DELETE FROM notification.Outbox WHERE processed_at IS NULL AND type IN ('notification.dispatch', 'notification.receipts');
PRINT CONCAT('In de wachtrij: ', @@ROWCOUNT);

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
