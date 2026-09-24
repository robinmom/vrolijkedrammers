# ADR-012: Startnummer en volgorde optocht

- **Status**: Voorgesteld · 2026-09-24

## Context

Het startnummer (`start_number`) wordt later door het bestuur/de optochtcommissie toegekend bij het samenstellen van de optocht en staat los van het opgavenummer (bijv. opgave 3 → startnummer 17). Eisen: handmatig invullen, later wijzigen, wijzigingen loggen, controle op dubbele nummers, uniek per optocht, alleen met de juiste permission. Onderzocht is of drag-and-drop nuttig is; zo ja, dan een aparte `parade_order`, en startnummers alleen **expliciet** daaruit genereren (geen onbedoelde hernummering).

## Options considered

1. **Alleen handmatige startnummers** (inline-edit in een tabel).
2. **Startnummer = positie in een gesleepte lijst** (automatisch): intuïtief, maar elke sleepactie hernummert → onbedoelde wijzigingen van gepubliceerde nummers.
3. **Aparte `parade_order` (drag-and-drop) + expliciete generatie van startnummers + handmatige override**.

Uniciteit:
- a. Controle in de applicatie (SELECT vooraf): race condition mogelijk.
- b. **Filtered unique index** in de database + een nette foutmelding.

## Decision

**Optie 3 met uniciteit via (b).**

- `start_number int NULL`, `parade_order int NULL` op ParadeRegistration.
- `CREATE UNIQUE INDEX UX_ParadeRegistration_StartNumber ON parade.ParadeRegistration(parade_id, start_number) WHERE start_number IS NOT NULL;`
- `CHECK (start_number IS NULL OR start_number > 0)`.
- **Handmatig**: `PUT /admin/parade/registrations/{id}/start-number` (`parade.assign-start-number`, If-Match). Bij een unique-violation → 409 `PARADE_START_NUMBER_TAKEN` met de naam van de groep die het nummer al heeft. De UI biedt "Wisselen" aan (swap in één transactie: A → tijdelijk NULL, B → x, A → y).
- **Drag-and-drop** wijzigt alleen `parade_order` (`PUT /admin/parades/{id}/order`, met een `composition_version` op Parade voor optimistic concurrency tussen commissieleden).
- **Genereren**: `preview` (modus `FillEmpty` of `Renumber`, startwaarde) → server geeft een diff + een eenmalig `previewToken` (5 min, gebonden aan `composition_version`) → `apply` voert de diff in één transactie uit (tijdelijk NULL zetten om unique-conflicten te vermijden, daarna nieuwe waarden). Hernummeren van reeds gepubliceerde nummers vraagt een extra bevestiging.
- **Publiceren**: aparte actie → status `StartNumberAssigned` + push "Jullie startnummer is 17".
- **Logging**: elke wijziging in `ParadeRegistrationHistory` (per registratie: oud/nieuw/wie/wanneer/bron) + `AuditLog` (`parade.start-number.changed`, bij bulk één record met de diff).
- Withdrawn/Rejected → het startnummer wordt NULL (gelogd), zodat het vrijkomt.
- Groepsverantwoordelijken kunnen het startnummer alleen zien, niet wijzigen (niet in hun DTO).

## Reasoning

- Drag-and-drop helpt de commissie bij 30–80 groepen om afwisseling en lengte te overzien (zie 13 §7.3).
- Het scheiden van volgorde en nummer voorkomt onbedoelde hernummering, wat expliciet een eis is.
- Een database-constraint is de enige betrouwbare uniciteitsgarantie bij gelijktijdig werkende commissieleden.

## Consequences

- Twee concepten in de UI (volgorde en startnummer); de UI toont een waarschuwing als ze uit de pas lopen ("5 groepen hebben een startnummer dat niet overeenkomt met de volgorde").
- Een wissel-/swap-actie is nodig vanwege de unique index.
- Tests P6–P10, P23 (zie 12).

## Security implications

- Beschermt tegen T14: alleen `parade.assign-start-number`, niet in groeps-DTO's, DB-uniciteit, rowversion, volledige historie en audit.

## Cost implications

- Geen infrastructuurkosten; frontendbibliotheek `dnd-kit` is open source.
