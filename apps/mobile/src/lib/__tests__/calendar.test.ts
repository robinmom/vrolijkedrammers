import * as Clipboard from 'expo-clipboard';
import { Alert, Linking, Platform } from 'react-native';
import { agendaFeedUrl, subscribeToAgenda } from '../calendar';

// Agenda-abonnement: de knop "Zet de agenda in je telefoon" opent de webcal-feed (iOS) of valt op Android terug op
// de https-link met een kopieerknop.
describe('Agenda-abonnement', () => {
  const originalOs = Platform.OS;
  const webcal = agendaFeedUrl.replace(/^https?:\/\//, 'webcal://');
  let openURL: jest.SpyInstance;

  beforeEach(() => {
    openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined as never);
    (Clipboard.setStringAsync as jest.Mock).mockClear();
  });

  afterEach(() => {
    openURL.mockRestore();
    (Platform as { OS: string }).OS = originalOs;
  });

  it('opent op iOS de webcal-feed zodat het systeem een agenda-abonnement voorstelt', async () => {
    (Platform as { OS: string }).OS = 'ios';
    await subscribeToAgenda();
    expect(openURL).toHaveBeenCalledWith(webcal);
    expect(webcal).toMatch(/^webcal:\/\/.+\/api\/v1\/events\/agenda\.ics$/);
  });

  it('valt op Android terug op de https-link met uitleg en een "Kopieer link"-knop', async () => {
    (Platform as { OS: string }).OS = 'android';
    openURL.mockRejectedValueOnce(new Error('geen app voor webcal'));
    const alert = jest.spyOn(Alert, 'alert');

    await subscribeToAgenda();

    // Eerst nog geprobeerd te openen, daarna de nette terugval.
    expect(openURL).toHaveBeenCalledWith(webcal);
    const [title, message, buttons] = alert.mock.calls[0]!;
    expect(title).toBe('Agenda toevoegen');
    expect(message).toContain(agendaFeedUrl);
    buttons!.find((b) => b.text === 'Kopieer link')!.onPress!();
    expect(Clipboard.setStringAsync).toHaveBeenCalledWith(agendaFeedUrl);
    alert.mockRestore();
  });
});
