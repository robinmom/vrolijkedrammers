# ADR-008: Bestandsopslag

- **Status**: Voorgesteld · 2026-09-24 · aangevuld: workers in de API-app (B-01), scanmodus per omgeving (OQ-65)

## Context

Er worden bestanden opgeslagen voor: foto's (albums, publiek/leden/rollen), afbeeldingen bij events/nieuws, optochtdocumenten (PDF/JPG/PNG/DOCX; mogelijk met persoonsgegevens), bijlagen bij events, Excel-imports (aanrijtijden) en exports. Bestanden mogen nooit publiek toegankelijk zijn, tenzij het publieke content betreft. Er is malwarescanning nodig of een ontwerp dat dit ondersteunt.

## Options considered

1. **Azure Blob Storage, private containers, toegang via de API met kortlevende SAS**.
2. Bestanden in de database (varbinary/FILESTREAM): eenvoudige transacties, maar een dure/grote DB en trage backups.
3. Blob met publieke containers voor publieke foto's + private voor de rest: sneller/cachebaar, maar risico op verkeerde classificatie.

Malwarescanning:
- a. **Microsoft Defender for Storage – on-upload malware scanning** (managed, per storage-account + per GB).
- b. ClamAV in een container/Function (gratis software, maar signatures bijhouden en beheer nodig).
- c. Geen scan, alleen typebeperking + re-encoding.

## Decision

- **Optie 1**: één storage-account per omgeving, `allowBlobPublicAccess = false`, shared key access uit, alleen Entra/managed identity + **user-delegation SAS** (read, 5–15 min, per blob).
- Containers: `quarantine`, `parade-documents`, `event-attachments`, `photos-original`, `photos-derived` (display 1600 px + thumbnail 400 px), `imports`, `exports` (lifecycle: verwijderen na 7 dagen), `dataprotection`.
- **Publieke foto's**: ook via kortlevende SAS-urls met lange cache-headers op de derivaten (of later een CDN met private origin). Geen publieke containers.
- **Upload-flow**: client → API (stream, limieten, magic-bytecheck) → `quarantine/{uuid}` → malwarescan → bij *clean* verplaatsen naar de doelcontainer + DB-status `Clean`; afbeeldingen daarna opnieuw encoderen (EXIF strippen) door een worker (media-job-queue in de API-app, ADR-007).
- **Malwarescan**: **(a) Defender for Storage** in Prod (en optioneel Acc). **Fallback** als het budget dit niet toelaat: (c) strikte typebeperking + re-encoding van afbeeldingen + DOCX/PDF alleen als download voor beheerders, met de kans om (b) later toe te voegen via dezelfde quarantaine-flow.
- **Versioning + soft delete** (30 dagen) aan; lifecycle: originele foto's na 90 dagen naar de Cool-tier.

## Reasoning

- Private-by-default voorkomt de meest voorkomende datalekken (open containers).
- Door de quarantaine-flow is de scanmethode verwisselbaar zonder applicatiewijziging.
- Defender is de managed, onderhoudsvrije optie; kosten zijn beperkt bij een klein volume.

## Consequences

- Downloads lopen altijd via de API (autorisatiecheck → 302 naar SAS). Voor fotogrids geeft de API batchgewijs SAS-urls uit (per album-request).
- Uploads groter dan ~25 MB (video) worden niet ondersteund.
- Scanmodus is configureerbaar (`FileScanning:Mode = Defender | None`). In Dev/Acc zonder Defender (`None`) markeert de pijplijn bestanden na de type- en magic-bytecontrole als `Clean`, zodat de flow identiek blijft. In Prod is `Defender` verplicht (health-check waarschuwt bij `None`).
- De beeldverwerking (ImageSharp) draait als worker-job in de API-app, met parallelliteit 1 om API-latency te beschermen.

## Security implications

- Geen publieke blobs; SAS kort, read-only, per blob, HTTPS-only, user-delegation (intrekbaar via key-revocatie).
- Bestandsnamen/paden worden door de server gegenereerd (geen path traversal); originele namen gesanitiseerd als metadata.
- Content-Disposition `attachment` voor documenten; content-type server-side bepaald; geen SVG/HTML.
- Malware: Defender-scanresultaat via Event Grid/blob index tags → verwerking; bij *infected*: verwijderen, uploader en beheer informeren, auditlog.

## Cost implications

- Blob-opslag ± 50 GB: ~€ 1–3/mnd; transacties verwaarloosbaar.
- Defender for Storage met malwarescanning: ~€ 10/mnd per storage-account + ~€ 0,15 per gescande GB (indicatief) → ~€ 10–12/mnd in Prod.
- ClamAV-alternatief: ~€ 0 aan licentie, maar hosting (Container Apps/Function met grote image) en beheer.
