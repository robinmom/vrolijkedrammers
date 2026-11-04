import * as ScreenOrientation from 'expo-screen-orientation';
import { useEffect } from 'react';
import { Dimensions, Platform } from 'react-native';

/**
 * Grens waarboven we een toestel als tablet zien: de kortste zijde van het scherm in dp. Dit is dezelfde
 * grens die Android zelf gebruikt (`sw600dp`). Hier kijken we bewust naar het toestel en niet naar de
 * beschikbare breedte, want de schermstand hangt aan het toestel: draaien verandert de breedte, dus daarop
 * reageren zou rondjes draaien. De layout kijkt wél naar de breedte, zie `WIDE_BREAKPOINT`.
 */
const TABLET_MIN_SHORT_SIDE = 600;

function isTabletSized() {
  const { width, height } = Dimensions.get('screen');
  return Math.min(width, height) >= TABLET_MIN_SHORT_SIDE;
}

/**
 * Houdt een telefoon in portret en laat een tablet draaien.
 *
 * Alleen op Android. iOS regelt dit per toestel in Info.plist (`UISupportedInterfaceOrientations` voor de
 * iPhone, `UISupportedInterfaceOrientations~ipad` voor de iPad), wat vanaf het eerste frame klopt en geen
 * code nodig heeft. Op Android kan dat niet: `android:screenOrientation` staat op de MainActivity en geldt
 * voor elk toestel, dus daar zetten we het hier.
 *
 * Voor breed draaien we `unlockAsync()` en niet `lockAsync(ALL)`: `ALL` is volgens de Expo-documentatie
 * ongeldig op toestellen die geen omgekeerd portret ondersteunen.
 */
export function useOrientationLock() {
  useEffect(() => {
    if (Platform.OS !== 'android') {
      return;
    }

    const apply = () => {
      const promise = isTabletSized()
        ? ScreenOrientation.unlockAsync()
        : ScreenOrientation.lockAsync(ScreenOrientation.OrientationLock.PORTRAIT_UP);
      // Mislukken mag nooit de app tegenhouden; dan blijft de stand zoals het systeem hem had.
      promise.catch(() => undefined);
    };

    apply();
    // Een vouwbare telefoon verandert van schermformaat als je hem open- of dichtvouwt.
    const subscription = Dimensions.addEventListener('change', apply);
    return () => subscription.remove();
  }, []);
}
