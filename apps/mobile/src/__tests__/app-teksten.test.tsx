import { QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react-native';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactNode } from 'react';
import { Alert } from 'react-native';
import VerenigingScreen from '../app/meer/vereniging';
import { TextsProvider } from '../content/texts';
import { showAlert } from '../lib/alert';
import { createTestQueryClient, mockApi } from '../test/render';
import { ThemeProvider } from '../theme/ThemeProvider';

/**
 * Teksten uit het portal in de plaats van die uit de bundel (Beheer → App-teksten), zodat het bestuur de teksten kan
 * wijzigen zonder nieuwe app in de store.
 */
async function renderVereniging(texts: Record<string, string>) {
  mockApi({ '/api/v1/app-texts': { texts } });
  const client = createTestQueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <ThemeProvider mode="light">
      <QueryClientProvider client={client}>
        <TextsProvider>{children}</TextsProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
  return await renderRouter({ 'meer/vereniging': VerenigingScreen }, { initialUrl: '/meer/vereniging', wrapper });
}

describe('App-teksten uit het portal', () => {
  it('zet de aangepaste tekst in de plaats van die uit de bundel', async () => {
    await renderVereniging({ Bestuur: 'Het bestuur', Geschiedenis: 'Onze geschiedenis' });
    expect(await screen.findByText('Het bestuur')).toBeTruthy();
    expect(await screen.findByText('Onze geschiedenis')).toBeTruthy();
    expect(screen.queryByText('Bestuur')).toBeNull();
  });

  it('laat de tekst uit de bundel staan als er niets is aangepast', async () => {
    await renderVereniging({});
    expect(await screen.findByText('Bestuur')).toBeTruthy();
    expect(screen.queryByText('Het bestuur')).toBeNull();
  });

  it('gebruikt de aangepaste tekst ook in een melding van het toestel', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    await renderVereniging({ Bestuur: 'Het bestuur', Uitloggen: 'Afmelden' });
    // De melding komt uit een knop, dus pas nadat de teksten binnen zijn: daarom eerst wachten op een tekst die
    // alleen uit het portal kan komen.
    expect(await screen.findByText('Het bestuur')).toBeTruthy();
    showAlert('Uitloggen', 'Weet je het zeker?', [{ text: 'Uitloggen' }]);
    await waitFor(() => expect(alert).toHaveBeenCalledWith('Afmelden', 'Weet je het zeker?', [{ text: 'Afmelden' }]));
    alert.mockRestore();
  });
});
