# Noodprocedure toegangscontrole

Voor het deurpersoneel (rol Deurcontrole) en het bestuur. Geldt tijdens carnaval en bij activiteiten met
toegangscontrole (zie ADR-005 en ADR-006, fase 14/15).

## Internet valt weg

1. **Gewoon doorscannen.** De scanner controleert dan zelf met de controlelijst die bij het openen is opgehaald;
   bovenaan het resultaat staat **"Offline gecontroleerd"**.
2. De scans komen in een wachtrij ("… offline scans wachten op verzending") en worden vanzelf verstuurd zodra er
   weer internet is. Opnieuw versturen is veilig: er ontstaan geen dubbele scans.
3. **Scanner niet sluiten of uitloggen** zolang er scans wachten. De controlelijst staat alleen in het geheugen: wie de
   scanner zonder internet opnieuw opent, kan pas weer scannen als er internet is.
4. Beperking (lichte variant): een ticket dat pas **tijdens** de storing wordt geblokkeerd, herkent de scanner offline
   niet. Na verzending staat zo'n scan in de **Toegangslog** met het label **Offline toegelaten**.
5. Twee deuren offline: allebei groen voor dezelfde persoon is mogelijk. Na verzending telt die persoon één keer als
   binnen en staat de tweede scan als "al eerder binnen via een ander toestel" in de log.

## Scanner of telefoon werkt helemaal niet

1. Leden opzoeken in het portal: **Leden → lid → kaart Toegang → Inchecken** (werkt op elke laptop of telefoon met
   internet). Dat gaat in hetzelfde toegangslog, dus "al binnen" blijft kloppen.
2. Geen internet en geen portal: een **papieren lijst** bijhouden (naam, lidnummer, tijd). Na afloop checkt het bestuur
   deze leden alsnog in of noteert de lijst bij de Toegangslog.

## Na afloop

- **Toegang → Toegangslog**: kies de carnavalsdag of activiteit; controleer de labels **QR (offline)** en
  **Offline toegelaten**.
- Bij misbruik: het ticket blokkeren of opnieuw uitgeven onder **Toegang → Ledentickets**.
