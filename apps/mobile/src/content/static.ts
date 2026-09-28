/**
 * Vaste teksten voor de Meer-pagina's en de Optocht-tab (fase 6).
 *
 * PLAATSHOUDERS: het bestuur levert de definitieve teksten, adressen en tijden aan. Ze moeten vóór de publieke
 * lancering (fase 7) vervangen zijn; zolang `placeholder` true is, tonen de pagina's "Voorlopige informatie".
 * Volledige optochtdata (deelnemers, route) komt in fase 11 uit de API.
 */
export const placeholder = true;

export interface Section {
  heading?: string;
  paragraphs: string[];
}

export const vereniging: Section[] = [
  {
    paragraphs: [
      'CV De Vrolijke Drammers is sinds 1958 de carnavalsvereniging van Loil. Elk jaar zorgen we samen met de Prins, de jeugdraad, de dansgardes en vele vrijwilligers voor een onvergetelijk carnaval.',
    ],
  },
  { heading: 'Bestuur', paragraphs: ['Hier komt binnenkort een overzicht van het bestuur en de commissies.'] },
  { heading: 'Geschiedenis', paragraphs: ['Hier komt binnenkort de geschiedenis van de vereniging.'] },
];

export const locatie = {
  name: 'Loil',
  address: null as string | null,
  intro: 'Hier komt binnenkort de thuisbasis van De Vrolijke Drammers, met adres en routebeschrijving.',
  coordinates: null as { latitude: number; longitude: number } | null,
};

export const contact = {
  intro: 'Vragen over de vereniging, de optocht of een activiteit? Neem gerust contact met ons op.',
  /** Leeg = het supportadres uit `/app-config` (beheerbaar in het portal). */
  email: null as string | null,
  website: 'https://www.vrolijkedrammers.nl',
};

export const lidWorden: Section[] = [
  {
    paragraphs: [
      'Word ook een Drammer! Als lid help je mee aan het gezelligste carnaval van Loil en ben je welkom bij alle activiteiten van de vereniging.',
    ],
  },
  {
    heading: 'Aanmelden',
    paragraphs: [
      'Lid worden kan vanaf 5 jaar, bijvoorbeeld bij de dansgarde. Ben je jonger dan 16, dan meldt je ouder of verzorger je aan. Het bestuur beoordeelt elke aanmelding; daarna hoor je van ons.',
    ],
  },
];

/** Tekst van de doorlopende SEPA-machtiging (gelijk aan de webpagina /lid-worden). */
export const mandaatTekst =
  'Ik geef CV De Vrolijke Drammers toestemming om doorlopende incasso-opdrachten te sturen naar mijn bank om de contributie van mijn rekening af te schrijven, en mijn bank om doorlopend een bedrag van mijn rekening af te schrijven overeenkomstig de opdracht van CV De Vrolijke Drammers. Ben ik het niet eens met een afschrijving, dan kan ik die binnen 8 weken via mijn bank laten terugboeken.';

export const privacyTekst =
  'Ik ga akkoord met de verwerking van deze gegevens voor het lidmaatschap, zoals beschreven in de privacyverklaring van de vereniging.';

export const fotoTekst =
  "Foto's waarop het lid te zien is, mogen in de app en op de website van de vereniging worden getoond.";

export const optocht = {
  subtitle: 'Het kleurrijke hoogtepunt van carnaval in Loil',
  /** De optocht is op carnavalszondag: de dag na de start van carnaval. */
  dayOffset: 1,
  /** Kaartzoekopdracht zolang er in het portal geen startlocatie is ingevuld. */
  routeQuery: 'Dorpsplein, Loil',
};
