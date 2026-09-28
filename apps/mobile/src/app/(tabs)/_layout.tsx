import { brand, fontFamily } from '@drammers/design-tokens';
import { Tabs } from 'expo-router';
import { Platform, StyleSheet, View } from 'react-native';
import { useParadeActionCount } from '../../features/badges';
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
  // Rood bolletje op Optocht als de optochtcommissie om een aanvulling vraagt.
  const paradeActions = useParadeActionCount();
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
            title: tab.title,
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
