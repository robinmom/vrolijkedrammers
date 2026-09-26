import { brand, radius } from '@drammers/design-tokens';
import { router } from 'expo-router';
import { Image, Pressable, StyleSheet, View } from 'react-native';
import { useMe } from '../../api/queries';
import { useSessionStatus } from '../../auth/useSession';
import { themeLabels } from '../../theme/labels';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, Card, Icon, LargeTitleHeader, Screen, SettingsList, ShortcutTile, type SettingsItem } from '../../ui';

const logo = require('../../../assets/images/logo.png');
const mascotte = require('../../../assets/images/mascotte.png');

/**
 * 05 Meer (Figma 5:391). De schakelaars voor pushmeldingen en herinneringen volgen in fase 10;
 * tot die tijd staat hier de weergave-instelling (licht/donker). Fase 9: inloggen of Mijn gegevens bovenaan.
 */
export default function MeerScreen() {
  const { colors, preference } = useTheme();
  const status = useSessionStatus();
  const me = useMe();
  const settings: SettingsItem[] = [
    {
      type: 'link',
      key: 'weergave',
      label: `Weergave: ${themeLabels[preference]}`,
      onPress: () => router.push('/meer/weergave'),
    },
    { type: 'link', key: 'over', label: 'Over de app', onPress: () => router.push('/meer/over') },
  ];
  if (__DEV__) {
    settings.push({
      type: 'link',
      key: 'componenten',
      label: 'Componenten (ontwikkeling)',
      onPress: () => router.push('/componenten'),
    });
  }

  return (
    <Screen>
      <LargeTitleHeader
        title="Meer"
        actions={[{ icon: 'meldingen', accessibilityLabel: 'Meldingen', onPress: () => router.push('/meldingen') }]}
      />
      <View style={styles.content}>
        <AccountCard signedIn={status === 'signedIn'} name={me.data?.displayName} />
        {status === 'signedIn' ? null : (
          <Pressable
            onPress={() => router.push('/meer/lid-worden')}
            accessibilityRole="button"
            accessibilityLabel="Word ook een Drammer! Lid worden"
          >
            <View style={styles.join}>
              <View style={styles.joinText}>
                <AppText variant="cardTitle" color="#FFFFFF">
                  Word ook een Drammer!
                </AppText>
                <AppText variant="caption" color="rgba(255,255,255,0.9)" style={styles.joinBody}>
                  Doe mee met de gezelligste carnavalsvereniging van Loil.
                </AppText>
                <View style={styles.joinButton}>
                  <AppText variant="link" color={colors.textOnLightButton} style={styles.joinButtonText}>
                    Lid worden
                  </AppText>
                </View>
              </View>
              <Image source={mascotte} style={styles.mascotte} accessibilityIgnoresInvertColors />
            </View>
          </Pressable>
        )}

        <View style={styles.menu}>
          <ShortcutTile
            size="regular"
            icon="vereniging"
            label="Vereniging"
            tint="red"
            onPress={() => router.push('/meer/vereniging')}
          />
          <ShortcutTile size="regular" icon="fotos" label="Foto's" tint="blue" onPress={() => router.push('/fotos')} />
          <ShortcutTile
            size="regular"
            icon="uitslagen"
            label="Uitslagen"
            tint="yellow"
            onPress={() => router.push('/uitslagen')}
          />
          <ShortcutTile
            size="regular"
            icon="megafoon"
            label="Meldingen"
            tint="red"
            onPress={() => router.push('/meldingen')}
          />
          <ShortcutTile
            size="regular"
            icon="locatie"
            label="Locatie"
            tint="green"
            onPress={() => router.push('/meer/locatie')}
          />
          <ShortcutTile
            size="regular"
            icon="contact"
            label="Contact"
            tint="blue"
            onPress={() => router.push('/meer/contact')}
          />
        </View>

        <AppText variant="sectionHeader" accessibilityRole="header">
          Instellingen
        </AppText>
        <SettingsList items={settings} />

        <View style={styles.footer}>
          <Image source={logo} style={styles.logo} resizeMode="contain" accessibilityIgnoresInvertColors />
          <AppText variant="label" color={colors.textSecondary} style={styles.regular}>
            CV De Vrolijke Drammers · Loil · sinds 1958
          </AppText>
        </View>
      </View>
    </Screen>
  );
}

/** Ingelogd: naar Mijn gegevens; anders inloggen voor leden. */
function AccountCard({ signedIn, name }: { signedIn: boolean; name?: string }) {
  const { colors } = useTheme();
  const title = signedIn ? (name ?? 'Mijn gegevens') : 'Inloggen voor leden';
  const detail = signedIn ? 'Mijn gegevens, apparaten en account' : 'Bekijk ledennieuws en je eigen gegevens';
  return (
    <Pressable
      onPress={() => router.push(signedIn ? '/account' : '/meer/inloggen')}
      accessibilityRole="button"
      accessibilityLabel={`${title}. ${detail}`}
    >
      <Card style={styles.account}>
        <View style={[styles.accountIcon, { backgroundColor: colors.tintBlue }]}>
          <Icon name="vereniging" size={22} color={colors.linkText} />
        </View>
        <View style={styles.accountText}>
          <AppText variant="bodyStrong">{title}</AppText>
          <AppText variant="caption" color={colors.textSecondary}>
            {detail}
          </AppText>
        </View>
        <Icon name="chevron" size={20} color={colors.textTertiary} />
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  account: { padding: 14, flexDirection: 'row', alignItems: 'center', gap: 12 },
  accountIcon: { width: 44, height: 44, borderRadius: radius.sm, alignItems: 'center', justifyContent: 'center' },
  accountText: { flex: 1, gap: 2 },
  content: { paddingHorizontal: 20, paddingTop: 4, gap: 16 },
  join: {
    backgroundColor: brand.blue,
    borderRadius: radius.lg,
    padding: 16,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
  },
  joinText: { flex: 1, gap: 6 },
  joinBody: { lineHeight: 18 },
  joinButton: {
    alignSelf: 'flex-start',
    backgroundColor: '#FFFFFF',
    borderRadius: radius.pill,
    paddingHorizontal: 14,
    paddingVertical: 8,
  },
  joinButtonText: { fontSize: 13 },
  mascotte: { width: 84, height: 84, borderRadius: 42, borderWidth: 3, borderColor: 'rgba(255,255,255,0.35)' },
  // Drie tegels van 111 pt per rij; op smallere toestellen lopen ze netjes door naar de volgende rij.
  menu: { flexDirection: 'row', flexWrap: 'wrap', gap: 10, justifyContent: 'space-between' },
  footer: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 10, paddingTop: 4 },
  logo: { width: 36, height: 36 },
  regular: { fontFamily: 'Inter_400Regular' },
});
