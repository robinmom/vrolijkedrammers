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
    paragraphs: ['Aanmelden via de app is binnenkort mogelijk. Tot die tijd kun je contact opnemen met het bestuur.'],
  },
];

export const optocht = {
  subtitle: 'Het kleurrijke hoogtepunt van carnaval in Loil',
  /** De optocht is op carnavalszondag: de dag na de start van carnaval. */
  dayOffset: 1,
  startTime: '13:30',
  routeLength: '3,2 km',
  timeline: [
    { time: '12:30', title: 'Opstellen deelnemers', place: 'Kerkstraat', color: 'blue' },
    { time: '13:30', title: 'Start optocht', place: 'Dorpsplein Loil', color: 'red' },
    { time: '14:15', title: 'Passage centrum', place: 'Dorpsstraat', color: 'yellow' },
    { time: '15:30', title: 'Finish & prijsuitreiking', place: 'Feesttent Loil', color: 'green' },
  ] as const,
  routeQuery: 'Dorpsplein, Loil',
};
