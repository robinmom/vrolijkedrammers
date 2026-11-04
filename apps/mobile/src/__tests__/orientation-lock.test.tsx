import { render } from '@testing-library/react-native';
import { Dimensions, Platform } from 'react-native';
import { useOrientationLock } from '../features/useOrientationLock';

const mockLockAsync = jest.fn().mockResolvedValue(undefined);
const mockUnlockAsync = jest.fn().mockResolvedValue(undefined);

jest.mock('expo-screen-orientation', () => ({
  lockAsync: (...args: unknown[]) => mockLockAsync(...args),
  unlockAsync: () => mockUnlockAsync(),
  OrientationLock: { PORTRAIT_UP: 3 },
}));

function Probe() {
  useOrientationLock();
  return null;
}

function withScreen(width: number, height: number) {
  return jest.spyOn(Dimensions, 'get').mockReturnValue({ width, height, scale: 2, fontScale: 1 });
}

describe('useOrientationLock', () => {
  const original = Platform.OS;

  afterEach(() => {
    Object.defineProperty(Platform, 'OS', { value: original, configurable: true });
    jest.restoreAllMocks();
    mockLockAsync.mockClear();
    mockUnlockAsync.mockClear();
  });

  function on(os: 'android' | 'ios') {
    Object.defineProperty(Platform, 'OS', { value: os, configurable: true });
  }

  it('houdt een Android-telefoon in portret', async () => {
    on('android');
    withScreen(412, 915);
    await render(<Probe />);
    expect(mockLockAsync).toHaveBeenCalledWith(3);
    expect(mockUnlockAsync).not.toHaveBeenCalled();
  });

  it('laat een Android-tablet draaien', async () => {
    on('android');
    withScreen(800, 1280);
    await render(<Probe />);
    expect(mockUnlockAsync).toHaveBeenCalled();
    expect(mockLockAsync).not.toHaveBeenCalled();
  });

  /** Een tablet in landschap: de kortste zijde beslist, niet de huidige breedte. */
  it('kijkt naar de kortste zijde en niet naar de huidige breedte', async () => {
    on('android');
    withScreen(1280, 800);
    await render(<Probe />);
    expect(mockUnlockAsync).toHaveBeenCalled();
  });

  /**
   * Op iOS regelt Info.plist dit per toestel. Zouden we hier tóch locken, dan werkt Split View op een
   * iPad niet meer (zie de Expo-documentatie bij screen-orientation).
   */
  it('doet op iOS niets', async () => {
    on('ios');
    withScreen(412, 915);
    await render(<Probe />);
    expect(mockLockAsync).not.toHaveBeenCalled();
    expect(mockUnlockAsync).not.toHaveBeenCalled();
  });
});
