import { brand, fontFamily } from '@drammers/design-tokens';
import { Redirect, Tabs } from 'expo-router';
import { Platform, StyleSheet, View } from 'react-native';
import { useMe } from '../../api/queries';
import { useSessionStatus } from '../../auth/useSession';
import { useText } from '../../content/texts';
import { useParadeActionCount } from '../../features/badges';
import { useJuryOnly } from '../../features/juryMode';
import { useTheme } from '../../theme/ThemeProvider';
import { Icon, type IconName } from '../../ui';

function TabIcon({ name, color, focused }: { name: IconName; color: string; focused: boolean }) {
  const { colors } = useTheme();
  // Android (Material 3, Figma): actieve tab krijgt een pill-indicator achter het icoon.
  if (Platform.OS === 'android') {
    return (
      <View style={[styles.indicator, focused && { backgroundColor: colors.tintRed }]}>
        <Icon name={name} size={24} color={color} />
      </View>
    );
  }
  return <Icon name={name} size={24} color={color} />;
}

const tabs: { name: string; title: string; icon: IconName }[] = [
  { name: 'index', title: 'Home', icon: 'home' },
  { name: 'programma', title: 'Programma', icon: 'programma' },
  { name: 'optocht', title: 'Optocht', icon: 'optocht' },
  { name: 'nieuws', title: 'Nieuws', icon: 'nieuws' },
  { name: 'meer', title: 'Meer', icon: 'meer' },
];

export default function TabLayout() {
  const { colors } = useTheme();
  const text = useText();
  // Rood bolletje op Optocht als de optochtcommissie om een aanvulling vraagt.
  const paradeActions = useParadeActionCount();
  // Jury-modus: een jurylid van het Dansgardefestival krijgt geen tabbalk maar meteen het jureerscherm.
  const juryOnly = useJuryOnly();
  const me = useMe();
  const signedIn = useSessionStatus() === 'signedIn';

  // Heel even wachten op /me, anders flitst de hele app voorbij voordat een jurylid op zijn eigen scherm staat. Die
  // query wordt bewust niet bewaard tussen starts (QueryProvider), dus hij is er bij een koude start nog niet. Offline
  // staat hij op `paused` en na een fout op `error`: dan tonen we gewoon de tabbalk, zodat de app nooit leeg blijft.
  if (signedIn && me.isPending && me.fetchStatus === 'fetching') {
    return null;
  }

  if (juryOnly) {
    return <Redirect href="/dansgardefestival-jury" />;
  }

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.accentText,
        tabBarInactiveTintColor: colors.textTertiary,
        tabBarStyle: { backgroundColor: colors.tabBar, borderTopColor: colors.border },
        tabBarLabelStyle: { fontFamily: fontFamily.body.medium, fontSize: 10 },
      }}
    >
      {tabs.map((tab) => (
        <Tabs.Screen
          key={tab.name}
          name={tab.name}
          options={{
            // De tabbalk tekent zijn label zelf, dus hier de tekst uit het portal (content/texts.tsx).
            title: text(tab.title),
            ...(tab.name === 'optocht' && paradeActions > 0
              ? { tabBarBadge: paradeActions, tabBarBadgeStyle: { backgroundColor: brand.red, color: '#FFFFFF', fontFamily: fontFamily.body.semibold } }
              : {}),
            // De tintkleuren komen uit onze tokens en zijn dus altijd strings.
            tabBarIcon: ({ color, focused }) => (
              <TabIcon name={tab.icon} color={typeof color === 'string' ? color : colors.textTertiary} focused={focused} />
            ),
          }}
        />
      ))}
    </Tabs>
  );
}

const styles = StyleSheet.create({
  indicator: { borderRadius: 16, paddingHorizontal: 20, paddingVertical: 4 },
});
