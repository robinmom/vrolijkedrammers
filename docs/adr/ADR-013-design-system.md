# ADR-013: Design system en Figma als bron

- **Status**: Voorgesteld · 2026-09-24

## Context

Er is een Figma-ontwerp ("Vrolijke Drammers – App Design") met 7 schermen in 4 varianten: iOS en Android, elk licht en donker (zie [17-design-system.md](../17-design-system.md)). De opdrachtgever wil dat de UI op deze ontwerpen gebaseerd wordt. App en beheerportal moeten als één merk voelen; iOS en Android moeten dezelfde look & feel hebben, met de platformnuances uit Figma.

## Options considered

1. **Eigen lichtgewicht componentenbibliotheek op basis van Figma-tokens** (StyleSheet + tokens-pakket), met platformvarianten via `Platform.select`.
2. UI-kit als basis (React Native Paper / Tamagui / NativeWind + gluestack) en die thematiseren.
3. Per platform native componenten (Material 3 op Android, UIKit-stijl op iOS) via bibliotheken.

## Decision

**Optie 1**: een gedeeld pakket `packages/design-tokens` (kleuren licht/donker, typografie, radius, spacing, schaduw) als de enige bron van stijlwaarden. Een kleine eigen componentenset in de app (`apps/mobile/src/ui`), met de componentinventaris uit doc 17. Het beheerportal gebruikt dezelfde tokens als CSS custom properties.

- Fonts Poppins + Inter via `expo-font`.
- Iconen: de eigen Figma-iconset (lijnstijl, 24 px, stroke 2) als SVG-componenten (`react-native-svg`), geëxporteerd uit Figma in de bouwfase.
- Licht/donker via `useColorScheme()` + een handmatige override in de instellingen.
- Figma-componenten worden via Code Connect gekoppeld zodra de componenten bestaan (optioneel).

## Reasoning

- Het Figma-ontwerp is specifiek (hero-vormen, datumblokken, tegels) en past niet naadloos op een generieke UI-kit; een kit overschrijven kost meer dan een kleine eigen set.
- Tokens delen tussen app en portal geeft merkconsistentie met minimale overhead.
- Platformverschillen uit Figma zijn beperkt (tabbar, titels, chips, toggles) en goed via varianten op te lossen.

## Consequences

- Toegankelijkheidsbevindingen (contrast van enkele tekstkleuren, zie doc 17 §7) worden als tokenaanpassing voorgesteld aan de designer vóór de implementatie.
- Ontbrekende schermen (login, QR, scanner, wizard, …) worden ontworpen met dezelfde tokens/componenten; voorkeur voor eerst een Figma-aanvulling (OQ-42).
- Visuele regressie: snapshot-tests van kerncomponenten (licht/donker).

## Security implications

- Geen directe. Scanner-resultaatschermen volgen vaste componenten (kleur + icoon + tekst), zodat er geen verwarring ontstaat die tot onterechte toegang leidt.

## Cost implications

- Geen licentiekosten (fonts OFL, eigen iconen). Iets meer initieel werk dan een UI-kit, minder onderhoud aan overrides.
