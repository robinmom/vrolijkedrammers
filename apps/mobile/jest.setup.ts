// Native modules die in Jest niet bestaan, vervangen door de mocks van de pakketten zelf of een minimale stub.
jest.mock('@react-native-async-storage/async-storage', () => require('@react-native-async-storage/async-storage/jest/async-storage-mock'));
jest.mock('@react-native-community/netinfo', () => require('@react-native-community/netinfo/jest/netinfo-mock.js'));
jest.mock('expo-calendar/legacy', () => ({ createEventInCalendarAsync: jest.fn(async () => ({ action: 'saved' })) }));

// De API-client leest `fetch` bij het aanmaken; tests geven per geval antwoorden via `mockApi` (src/test/api.ts).
globalThis.fetch = jest.fn(async () => {
  throw new Error('Onverwachte netwerkaanroep in een test: gebruik mockApi().');
}) as unknown as typeof fetch;
