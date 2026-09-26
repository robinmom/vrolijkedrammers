import { useQueryClient } from '@tanstack/react-query';
import { Redirect } from 'expo-router';
import { Alert, StyleSheet, View } from 'react-native';
import { api } from '../../api/client';
import { queryKeys, useMyDevices } from '../../api/queries';
import { useRefresh } from '../../api/useRefresh';
import { useSessionStatus } from '../../auth/useSession';
import { formatDateOnly } from '../../lib/dates';
import { useTheme } from '../../theme/ThemeProvider';
import { AppText, BackLink, Button, Card, LargeTitleHeader, QueryState, Screen } from '../../ui';

/**
 * Mijn apparaten (fase 9): waar je bent ingelogd. Een ander apparaat afmelden logt dat apparaat direct uit
 * (de API weigert daarna elke aanroep ervan). Dit apparaat meld je af met Uitloggen.
 */
export default function ApparatenScreen() {
  const status = useSessionStatus();
  const { colors } = useTheme();
  const devices = useMyDevices();
  const client = useQueryClient();
  const { refreshing, onRefresh } = useRefresh([queryKeys.myDevices]);

  if (status === 'signedOut') {
    return <Redirect href="/meer/inloggen" />;
  }

  function confirmRevoke(id: string, name: string) {
    Alert.alert(`${name} afmelden?`, 'Je wordt op dat apparaat uitgelogd.', [
      { text: 'Annuleren', style: 'cancel' },
      {
        text: 'Afmelden',
        style: 'destructive',
        onPress: async () => {
          await api.DELETE('/api/v1/me/devices/{id}', { params: { path: { id } } });
          await client.invalidateQueries({ queryKey: queryKeys.myDevices });
        },
      },
    ]);
  }

  return (
    <Screen onRefresh={onRefresh} refreshing={refreshing}>
      <BackLink label="Mijn gegevens" />
      <LargeTitleHeader title="Mijn apparaten" />
      <View style={styles.content}>
        <QueryState query={devices} />
        {devices.data?.map((device) => (
          <Card key={device.id} style={styles.card}>
            <AppText variant="bodyStrong">
              {device.name}
              {device.current ? ' (dit apparaat)' : ''}
            </AppText>
            <AppText variant="caption" color={colors.textSecondary}>
              {device.platform === 'Ios' ? 'iOS' : 'Android'} · laatst gebruikt{' '}
              {formatDateOnly(device.lastSeenAt.slice(0, 10))}
            </AppText>
            {device.current ? null : (
              <Button label="Afmelden" variant="secondary" onPress={() => confirmRevoke(device.id, device.name)} />
            )}
          </Card>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { paddingHorizontal: 20, gap: 12 },
  card: { padding: 16, gap: 8 },
});
