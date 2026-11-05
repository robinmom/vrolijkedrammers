import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { isTestBuildOnProduction } from '../api/config';
import { AppText } from './AppText';

/**
 * Alarmbalk als een testversie met de productieomgeving praat. Dat hoort nooit: dan sta je met een testapp in de
 * echte ledenadministratie.
 *
 * Op 5 november 2026 gebeurde dat een halve ochtend zonder dat iemand het zag — een update zonder de juiste
 * omgevingsvariabele liet de dev-app terugvallen op productie. De fout zelf is afgedekt (zie api/config.ts en de
 * runbook app-uitrollen), maar het ontbreken van elk signaal was het echte probleem. Vandaar deze balk: hij hoort er
 * nooit te staan, en staat hij er wel, dan weet je het meteen.
 *
 * In de gewone winkelversie wordt hier niets getoond.
 */
export function EnvironmentBanner() {
  const insets = useSafeAreaInsets();
  if (!isTestBuildOnProduction) {
    return null;
  }
  return (
    <View style={[styles.banner, { paddingTop: insets.top + 6 }]} accessibilityRole="alert" accessibilityLiveRegion="assertive">
      <AppText variant="label" color="#FFFFFF" style={styles.text}>
        LET OP · deze testversie praat met PRODUCTIE
      </AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  // Fel rood, niet de huisstijlkleur: dit is geen onderdeel van het ontwerp maar een noodsignaal.
  banner: { position: 'absolute', top: 0, left: 0, right: 0, backgroundColor: '#B3001B', paddingBottom: 8, paddingHorizontal: 20 },
  text: { textAlign: 'center' },
});
