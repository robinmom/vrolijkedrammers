import * as Clipboard from 'expo-clipboard';
import { createEventInCalendarAsync } from 'expo-calendar/legacy';
import { Alert, Linking, Platform } from 'react-native';
import { apiBaseUrl, type EventDetail } from '../api/client';

/** De iCalendar-feed met alle openbare activiteiten (het agenda-abonnement), op dezelfde host als de API. */
export const agendaFeedUrl = `${apiBaseUrl}/api/v1/events/agenda.ics`;

/**
 * "Zet de agenda in je telefoon": abonneren op de hele verenigingsagenda, zodat nieuwe en gewijzigde activiteiten
 * vanzelf meekomen. iOS opent de webcal-link; het systeem vraagt dan om het agenda-abonnement toe te voegen.
 * Op Android is webcal-abonneren op het toestel onbetrouwbaar: lukt het openen niet, dan tonen we de https-link met
 * uitleg (Google Agenda → "Andere agenda's → Via URL") en een knop om de link te kopiëren. Zo blokkeren we niemand.
 */
export async function subscribeToAgenda(): Promise<void> {
  const https = agendaFeedUrl;
  const webcal = https.replace(/^https?:\/\//, 'webcal://');
  if (Platform.OS === 'ios') {
    await Linking.openURL(webcal);
    return;
  }

  try {
    await Linking.openURL(webcal);
  } catch {
    Alert.alert(
      'Agenda toevoegen',
      `Voeg deze link toe in Google Agenda via "Andere agenda's → Via URL":\n\n${https}`,
      [
        { text: 'Kopieer link', onPress: () => void Clipboard.setStringAsync(https) },
        { text: 'Sluiten', style: 'cancel' },
      ],
    );
  }
}

/**
 * "Toevoegen aan agenda" (Figma 06). Opent het systeemformulier van de agenda, zonder agendatoestemming:
 * de gebruiker bevestigt zelf. Lukt dat niet (bijv. geen agenda-app), dan de iCal-export van de API.
 */
export async function addToCalendar(event: EventDetail): Promise<void> {
  const location = [event.locationName, event.locationAddress].filter(Boolean).join(', ');
  const start = new Date(event.startAt);
  const end = event.endAt ? new Date(event.endAt) : new Date(start.getTime() + 2 * 60 * 60 * 1000);
  try {
    await createEventInCalendarAsync({
      title: event.title,
      startDate: start,
      endDate: end,
      allDay: event.allDay,
      location: location || undefined,
      notes: event.summary ?? undefined,
      timeZone: 'Europe/Amsterdam',
    });
  } catch {
    await Linking.openURL(`${apiBaseUrl}/api/v1/events/${event.id}/ical`);
  }
}

/** Opent een adres of coördinaat in de kaarten-app: Apple Kaarten op iOS, Google Maps op Android. */
export function openInMaps(query: string, coordinates?: { latitude: number; longitude: number }) {
  const target = encodeURIComponent(coordinates ? `${coordinates.latitude},${coordinates.longitude}` : query);
  return Linking.openURL(Platform.OS === 'ios' ? `https://maps.apple.com/?q=${target}` : `https://www.google.com/maps/search/?api=1&query=${target}`);
}
