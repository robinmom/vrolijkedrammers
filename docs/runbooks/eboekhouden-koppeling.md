# Runbook: e-Boekhouden-koppeling (ledensync)

De ledensync (fase 8, [ADR-010](../adr/ADR-010-eboekhouden-sync.md)) leest leden uit e-Boekhouden. Ze schrijft in fase 8 **niets** terug naar e-Boekhouden.

## 1. API-token aanmaken in e-Boekhouden

1. Log in bij e-Boekhouden met een gebruiker die bij de ledenadministratie mag (bij voorkeur een aparte gebruiker met minimale rechten, OQ-03).
2. Maak een API-token aan (in e-Boekhouden onder de instellingen voor de API/koppelingen; de menunaam kan per versie verschillen).
3. Kopieer het token direct: e-Boekhouden toont het maar één keer.

## 2. Token in Key Vault zetten

Het token komt nooit in de repository, in app-instellingen of in de chat. Zet het zelf in Key Vault (rol *Key Vault Secrets Officer* op de vault nodig):

```bash
read -rs EB_TOKEN   # plak het token; het verschijnt niet op het scherm en niet in de shellhistorie
az keyvault secret set --vault-name kv-dvd-dev --name eboekhouden-api-token --value "$EB_TOKEN" --output none
unset EB_TOKEN
```

De API leest het secret met zijn managed identity (rol *Key Vault Secrets User*, al toegekend in fase 1). Er is geen herstart nodig: de sync leest het token bij elke run.

## 3. Eerste sync

1. Portal → **Leden → Synchronisatie**: stel eerst de mapping van de vrije velden in (welk vrij veld geboortedatum, inschrijfjaar, status en categorie bevat; leeg = niet gemapt).
2. Start een **dry-run**. Het rapport toont hoeveel leden nieuw zouden zijn, en eventuele parsefouten en conflicten.
3. Klopt het rapport, start dan een **echte run**.
4. De nachtelijke sync (03:00) staat aan zodra de feature flag `members-sync` aan staat (Portal → Configuratie).

## 4. Leden weer verwijderen (alleen Dev en Acc)

Besluit 2026-09-25: in Dev wordt tijdelijk het echte ledenbestand gebruikt. Om alles in één keer weer te verwijderen:

- Portal → **Leden → Alle leden verwijderen** (recht `member.purge`, standaard bij Bestuur en Beheerder IT), en typ ter bevestiging `LEDEN VERWIJDEREN`.
- Dit verwijdert alle leden, syncruns en conflicten, ontkoppelt app-accounts van leden en **zet de nachtelijke sync uit**, zodat de leden niet vanzelf terugkomen. De actie komt in de auditlog.
- In productie is deze functie niet beschikbaar (de API weigert het, ook met het recht).
- Het token in Key Vault blijft staan; verwijder het ook als de koppeling niet meer nodig is: `az keyvault secret delete --vault-name kv-dvd-dev --name eboekhouden-api-token`.

## 5. Problemen

| Melding in het portal | Oorzaak | Oplossing |
|---|---|---|
| "Het e-Boekhouden-token staat nog niet in Key Vault" | Secret ontbreekt | Stap 2 |
| "Aanmelden bij e-Boekhouden mislukt: controleer het API-token" | Token ongeldig of verlopen | Nieuw token maken (stap 1) en opnieuw zetten (stap 2) |
| Status **Conflict** met "meer dan 10 % ontbreekt" | Massadeletie-guard | Controleer e-Boekhouden; er is niemand gedeactiveerd |
| "Er loopt al een ledensync" | Een run is bezig | Wachten; een run die na 30 minuten niet klaar is, wordt automatisch afgebroken |
