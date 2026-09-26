import { useQuery } from '@tanstack/react-query';
import { act, render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { QueryProvider } from '../api/QueryProvider';
import * as SecureStoreModule from 'expo-secure-store';
import { api } from '../api/client';
import { getStatus, setSessionForTest } from '../auth/session';
import { createTestQueryClient, mockApi } from '../test/render';

const SecureStore = SecureStoreModule as unknown as { __store: Map<string, string> };

let calls = 0;
function News() {
  const { data } = useQuery({ queryKey: ['news'], queryFn: async () => `nieuws ${++calls}` });
  return <Text>{data ?? 'leeg'}</Text>;
}

it('na inloggen haalt een open scherm zijn gegevens opnieuw op (met token) in plaats van leeg te blijven', async () => {
  setSessionForTest('signedOut', null);
  await render(
    <QueryProvider client={createTestQueryClient()}>
      <News />
    </QueryProvider>,
  );
  expect(await screen.findByText('nieuws 1')).toBeTruthy();

  await act(async () => setSessionForTest('signedIn'));

  expect(await screen.findByText('nieuws 2')).toBeTruthy();
});

it('als het vernieuwen van het token mislukt, wordt publieke content anoniem opgehaald', async () => {
  // Ingelogd, access-token verlopen; de aanmeldinstellingen zijn niet te laden (geen netwerk naar de API-config).
  SecureStore.__store.set('dvd.refreshToken', 'refresh-1');
  setSessionForTest('signedIn', null);
  mockApi({
    '/api/v1/news': { items: [], page: 1, pageSize: 5, totalCount: 0 },
    '/api/v1/app-auth-config': { status: 503 },
  });

  const { response } = await api.GET('/api/v1/news');

  expect(response.status).toBe(200);
  const request = (globalThis.fetch as jest.Mock).mock.calls.at(-1)![0] as Request;
  expect(request.headers.get('authorization')).toBeNull();
  // De sessie blijft staan; bij een volgende aanroep wordt het token opnieuw geprobeerd.
  expect(getStatus()).toBe('signedIn');
});
