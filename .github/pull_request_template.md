## Wat en waarom

<!-- Korte beschrijving; verwijs naar de fase uit docs/15-implementation-plan.md en het issue. -->

Fase: 
Issue: 

## Definition of Done (docs/16 – niveau 1)

Markeer wat niet van toepassing is met "n.v.t. + reden".

- [ ] Code compileert zonder nieuwe waarschuwingen
- [ ] Linting en formattering slagen
- [ ] Unit tests toegevoegd/bijgewerkt en groen
- [ ] Integratietests waar relevant (database, externe integraties, workers)
- [ ] Nieuwe endpoints in de permission-matrix-test; autorisatie-annotatie aanwezig
- [ ] Geen secrets en geen echte persoonsgegevens in code, tests, fixtures of deze PR (openbare repository)
- [ ] Database-migratie aanwezig en getest (bij modelwijzigingen)
- [ ] API-documentatie (OpenAPI) en gegenereerde client bijgewerkt
- [ ] Logging toegevoegd, zonder wachtwoorden/tokens/onnodige PII
- [ ] Foutafhandeling via ProblemDetails; duidelijke Nederlandse foutmeldingen in de UI
- [ ] Toegankelijkheid gecontroleerd (labels, contrast, grote tekst, schermlezer) bij UI-wijzigingen
- [ ] Documentatie/ADR bijgewerkt

## Securitycontrole

- Autorisatie (welke permission of scope beschermt dit?):
- Invoervalidatie:
- Gevoelige gegevens in responses of logs:
- Audit nodig?:

## Testbewijs

<!-- Testuitvoer, screenshots (licht/donker) of opnames bij UI-wijzigingen. -->
