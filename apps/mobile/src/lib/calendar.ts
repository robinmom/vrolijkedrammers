import { createEventInCalendarAsync } from 'expo-calendar/legacy';
import { Linking, Platform } from 'react-native';
import { apiBaseUrl, type EventDetail } from '../api/client';

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
