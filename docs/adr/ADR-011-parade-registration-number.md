# ADR-011: Opgavenummer optochtinschrijving

- **Status**: Voorgesteld · 2026-09-24

## Context

Iedere optochtinschrijving krijgt bij het **definitief indienen** automatisch een opgavenummer (`registration_number`) dat de volgorde van binnenkomst weergeeft. Het nummer moet:
- uniek zijn binnen een optocht/carnavalsjaar;
- oplopend zijn (1, 2, 3, …);
- automatisch worden toegekend bij het indienen (niet bij een concept);
- niet door een gebruiker wijzigbaar zijn;
- nooit opnieuw worden gebruikt (ook niet na intrekken of verwijderen);
- ook bij gelijktijdige inschrijvingen nooit dubbel voorkomen (database-locking/transacties).

## Options considered

| Optie | Beschrijving | Voor | Tegen |
|---|---|---|---|
| A. `MAX(registration_number) + 1` in de transactie | Lezen + schrijven | Eenvoudig | Race condition zonder zware lock; hergebruikt nummers als het hoogste nummer verwijderd wordt |
| B. SQL Server `SEQUENCE` per optocht | `NEXT VALUE FOR seq_parade_{id}` | Geen locks, snel | Dynamische sequences per optocht (DDL at runtime); nummers gaan verloren bij een rollback (gaten) |
| C. **Teller-tabel met rij-lock** | `ParadeNumberSequence(parade_id, last_number)`; `UPDATE … SET last_number += 1 OUTPUT inserted.last_number` in dezelfde transactie als de statuswijziging | Atomair, geen gaten bij een rollback, per optocht, eenvoudig testbaar, geen DDL | Serialiseert submits per optocht (bij < 100 submits per jaar irrelevant) |
| D. Identity-kolom | Globale identity | Eenvoudig | Niet per optocht; gaten |
| E. Applicatie-lock (mutex/distributed lock) | Lock in de app | — | Werkt niet over meerdere instances; complexer |

## Decision

**Optie C: teller-tabel met een atomaire update in de submit-transactie**, plus een unique index als laatste verdedigingslinie.

```sql
-- één rij per optocht, aangemaakt bij het aanmaken van de optocht
CREATE TABLE parade.ParadeNumberSequence (
  parade_id uniqueidentifier NOT NULL PRIMARY KEY REFERENCES parade.Parade(id),
  last_registration_number int NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX UX_ParadeRegistration_RegistrationNumber
  ON parade.ParadeRegistration(parade_id, registration_number)
  WHERE registration_number IS NOT NULL;
```

Submit (één transactie, isolatieniveau READ COMMITTED; de `UPDATE` pakt zelf een exclusieve rij-lock):

```sql
BEGIN TRAN;
  -- 1. registratie vergrendelen en controleren (status Draft, rowversion, periode open, validatie OK in app)
  -- 2. nummer reserveren (atomair, serialiseert gelijktijdige submits per optocht)
  UPDATE parade.ParadeNumberSequence WITH (ROWLOCK)
     SET last_registration_number = last_registration_number + 1
  OUTPUT inserted.last_registration_number
   WHERE parade_id = @paradeId;
  -- 3. inschrijving bijwerken
  UPDATE parade.ParadeRegistration
     SET registration_number = @nr, status = 'Submitted', submitted_at = SYSUTCDATETIME(), ...
   WHERE id = @id AND status = 'Draft' AND row_version = @rv;   -- 0 rijen → rollback (concurrency/dubbele submit)
  -- 4. ParadeStatusHistory + ParadeRegistrationHistory + AuditLog + Outbox (bevestiging/push)
COMMIT;
```

In EF Core: `ExecuteSqlInterpolatedAsync` voor stap 2 binnen `Database.BeginTransactionAsync()`, met de rest via de DbContext in dezelfde transactie.

Regels:
- `registration_number` staat niet in een request-DTO; een update-poging → 422 + audit.
- Status `Draft` heeft nooit een nummer (CHECK-constraint); na submit altijd (CHECK).
- Ingetrokken/afgewezen inschrijvingen houden hun nummer; hard delete na submit is niet mogelijk (alleen `Withdrawn`). Daardoor wordt een nummer nooit hergebruikt: de teller gaat alleen omhoog.
- Idempotency-Key op submit: een herhaalde request met dezelfde key geeft het oorspronkelijke resultaat terug.
- Een correctie (bijv. testinschrijving in productie) gaat alleen via een gedocumenteerd DBA-script met audit, niet via de applicatie.

## Reasoning

- Deterministisch, eenvoudig en transactioneel veilig. Bij een rollback wordt ook de teller teruggezet (geen gaten), in tegenstelling tot een SEQUENCE.
- De unique index vangt elk onvoorzien pad af (defense in depth).
- De serialisatie per optocht heeft geen merkbare impact bij dit volume.

## Consequences

- Bij het aanmaken van een Parade wordt automatisch een sequence-rij aangemaakt (in dezelfde transactie).
- De integratietest P1 (50 parallelle submits → 1..50 uniek, geen gaten) is verplicht in CI tegen een echte SQL Server (Testcontainers).
- Het nummer is "volgorde van definitief indienen", niet van het aanmaken van het concept; dit wordt duidelijk gecommuniceerd in de UI ("Jullie opgavenummer is 12. Dit is de volgorde van binnenkomst, niet jullie startnummer.").

## Security implications

- Beschermt tegen T15 (manipuleren opgavenummer): server-side toekenning, niet in DTO's, constraints, audit.
- Race conditions uitgesloten via rij-lock + unique index.

## Cost implications

- Geen; één kleine tabel.
