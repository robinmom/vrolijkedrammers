// Native modules die in Jest niet bestaan, vervangen door de mocks van de pakketten zelf of een minimale stub.
jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);
jest.mock('@react-native-community/netinfo', () => require('@react-native-community/netinfo/jest/netinfo-mock.js'));
jest.mock('expo-calendar/legacy', () => ({ createEventInCalendarAsync: jest.fn(async () => ({ action: 'saved' })) }));

// De API-client leest `fetch` bij het aanmaken; tests geven per geval antwoorden via `mockApi` (src/test/render.tsx).
globalThis.fetch = jest.fn(async () => {
  throw new Error('Onverwachte netwerkaanroep in een test: gebruik mockApi().');
}) as unknown as typeof fetch;

// Keychain/Keystore als geheugenopslag; tests lezen en wissen hem via `__store`.
jest.mock('expo-secure-store', () => {
  const store = new Map<string, string>();
  return {
    __store: store,
    AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY: 1,
    getItemAsync: jest.fn(async (key: string) => store.get(key) ?? null),
    setItemAsync: jest.fn(async (key: string, value: string) => void store.set(key, value)),
    deleteItemAsync: jest.fn(async (key: string) => void store.delete(key)),
  };
});
jest.mock('expo-web-browser', () => ({
  openAuthSessionAsync: jest.fn(async () => ({ type: 'cancel' })),
  openBrowserAsync: jest.fn(async () => ({ type: 'opened' })),
  maybeCompleteAuthSession: jest.fn(),
}));
jest.mock('expo-crypto', () => ({ randomUUID: () => require('node:crypto').randomUUID() }));
