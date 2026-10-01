// Native modules die in Jest niet bestaan, vervangen door de mocks van de pakketten zelf of een minimale stub.
jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);
jest.mock('@react-native-community/netinfo', () => require('@react-native-community/netinfo/jest/netinfo-mock.js'));
// Native slider (jureren): in tests een gewone View; de toegankelijke bediening zit in ScoreSlider zelf.
jest.mock('@react-native-community/slider', () => {
  const { View } = require('react-native');
  function NativeSlider(props: Record<string, unknown>) {
    return require('react').createElement(View, { testID: 'native-slider', ...props });
  }
  return { __esModule: true, default: NativeSlider };
});
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

// Push (fase 10b): geen native module in Jest; toestemming en token per test aan te passen via de mock.
jest.mock('expo-notifications', () => ({
  setNotificationHandler: jest.fn(),
  setNotificationChannelAsync: jest.fn(async () => null),
  getPermissionsAsync: jest.fn(async () => ({ status: 'undetermined' })),
  requestPermissionsAsync: jest.fn(async () => ({ status: 'granted' })),
  getExpoPushTokenAsync: jest.fn(async () => ({ data: 'ExponentPushToken[test-token-1234567890]' })),
  useLastNotificationResponse: jest.fn(() => undefined),
  addNotificationReceivedListener: jest.fn(() => ({ remove: jest.fn() })),
  setBadgeCountAsync: jest.fn(async () => true),
  DEFAULT_ACTION_IDENTIFIER: 'expo.modules.notifications.actions.DEFAULT',
  AndroidImportance: { MIN: 1, LOW: 2, DEFAULT: 3, HIGH: 4, MAX: 5 },
}));

// Mijn QR (fase 13b): helderheid, schermafdrukken en de QR-weergave zonder native code.
jest.mock('expo-brightness', () => ({
  setBrightnessAsync: jest.fn(async () => undefined),
  restoreSystemBrightnessAsync: jest.fn(async () => undefined),
}));
jest.mock('expo-screen-capture', () => ({
  usePreventScreenCapture: jest.fn(),
  useScreenshotListener: jest.fn(),
}));
jest.mock('react-native-qrcode-svg', () => {
  const { createElement } = require('react');
  const { Text } = require('react-native');
  return ({ value }: { value: string }) => createElement(Text, { testID: 'qr-code' }, value);
});

// Scannen (fase 14b): de camera als knop die een QR "scant" (tests roepen onBarcodeScanned aan), trillen als mock.
jest.mock('expo-camera', () => {
  const { createElement } = require('react');
  const { Pressable, Text } = require('react-native');
  return {
    CameraView: ({ onBarcodeScanned }: { onBarcodeScanned: (r: { data: string }) => void }) =>
      createElement(
        Pressable,
        {
          testID: 'camera',
          onPress: () => onBarcodeScanned({ data: (globalThis as { __qr?: string }).__qr ?? 'CODE' }),
        },
        createElement(Text, null, 'camera'),
      ),
    useCameraPermissions: () => [{ granted: true, canAskAgain: true }, jest.fn()],
  };
});
jest.mock('expo-haptics', () => ({
  notificationAsync: jest.fn(async () => undefined),
  NotificationFeedbackType: { Success: 'success', Warning: 'warning', Error: 'error' },
}));
