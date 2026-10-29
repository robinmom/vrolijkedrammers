import { radius } from '@drammers/design-tokens';
import { Image } from 'expo-image';
import * as ImagePicker from 'expo-image-picker';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Linking, Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../theme/ThemeProvider';
import { AppText, Card } from '../ui';
import { pendingPhotoUri, queuePhoto } from './juryPhotos';

/**
 * Eén foto per wagen of groep bij het jureren (fase 22e). De foto komt eerst op het toestel en wordt daarna in de
 * achtergrond verstuurd, want op de route is er vaak geen bereik. Een nieuwe foto vervangt de vorige. De commissie ziet
 * hem in het portal; publiek wordt hij alleen als de commissie hem bewust naar het uitslagalbum verplaatst.
 */
export function JuryPhotoCard({
  userId,
  paradeId,
  registrationId,
  /** Miniatuur van de foto die de server al heeft, als die er is. */
  serverThumbnailUrl,
  serverPending,
  onQueued,
  disabled = false,
}: {
  userId: string;
  paradeId: string;
  registrationId: string;
  serverThumbnailUrl?: string | null;
  serverPending?: boolean;
  /** Versturen van de wachtrij; wordt afgewacht zodat de kaart daarna de juiste status kan tonen. */
  onQueued?: () => void | Promise<void>;
  disabled?: boolean;
}) {
  const { colors } = useTheme();
  const [localUri, setLocalUri] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Ook opnieuw kijken als de serverstatus verandert: na een geslaagde upload is het bestand weg, en dan moet "wacht op
  // internet" verdwijnen. Zonder die afhankelijkheid bleef dat label staan en wees de voorvertoning naar een
  // verwijderd bestand.
  useEffect(() => {
    let active = true;
    void pendingPhotoUri(userId, paradeId, registrationId).then((uri) => {
      if (active) setLocalUri(uri);
    });
    return () => {
      active = false;
    };
  }, [userId, paradeId, registrationId, serverThumbnailUrl, serverPending]);

  const save = useCallback(
    async (uri: string) => {
      setBusy(true);
      setError(null);
      try {
        await queuePhoto(userId, paradeId, registrationId, uri);
        // Eerst de foto van het toestel tonen, dan versturen, en daarna opnieuw kijken: is hij weg, dan is hij binnen.
        setLocalUri(await pendingPhotoUri(userId, paradeId, registrationId));
        await onQueued?.();
        setLocalUri(await pendingPhotoUri(userId, paradeId, registrationId));
      } catch {
        setError('De foto kon niet worden opgeslagen. Probeer het opnieuw.');
      } finally {
        setBusy(false);
      }
    },
    [onQueued, paradeId, registrationId, userId],
  );

  /** Toegang geweigerd: niet opnieuw vragen (dat doet het systeem niet), maar naar de instellingen wijzen. */
  const denied = (what: string) =>
    Alert.alert(`Geen toegang tot ${what}`, `Geef in de instellingen toegang tot ${what} om een foto toe te voegen.`, [
      { text: 'Niet nu', style: 'cancel' },
      { text: 'Open instellingen', onPress: () => void Linking.openSettings() },
    ]);

  const take = useCallback(async () => {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      denied('de camera');
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ quality: 0.7, mediaTypes: ['images'] });
    if (!result.canceled && result.assets[0]) await save(result.assets[0].uri);
  }, [save]);

  const choose = useCallback(async () => {
    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      denied('je foto’s');
      return;
    }
    const result = await ImagePicker.launchImageLibraryAsync({ quality: 0.7, mediaTypes: ['images'] });
    if (!result.canceled && result.assets[0]) await save(result.assets[0].uri);
  }, [save]);

  const shown = localUri ?? serverThumbnailUrl ?? null;
  const has = shown !== null || serverPending === true;

  return (
    <Card style={styles.card}>
      <View style={styles.head}>
        <AppText variant="overline" color={colors.textSecondary}>
          Foto
        </AppText>
        {localUri ? (
          <AppText variant="caption" color={colors.accentText}>
            wacht op internet
          </AppText>
        ) : serverPending ? (
          <AppText variant="caption" color={colors.textSecondary}>
            wordt verwerkt
          </AppText>
        ) : null}
      </View>
      {shown ? (
        <Image source={{ uri: shown }} style={styles.preview} contentFit="cover" accessibilityLabel="Foto van deze wagen of groep" />
      ) : serverPending ? (
        <AppText variant="caption" color={colors.textSecondary}>
          Je foto is verstuurd en wordt nog verwerkt; hij verschijnt hier zodra dat klaar is.
        </AppText>
      ) : (
        <AppText variant="caption" color={colors.textSecondary}>
          Nog geen foto. Handig om later terug te zien welke wagen dit was; alleen de optochtcommissie ziet hem.
        </AppText>
      )}
      {error ? (
        <AppText variant="caption" color={colors.accentText} accessibilityRole="alert">
          {error}
        </AppText>
      ) : null}
      {busy ? (
        <ActivityIndicator />
      ) : (
        <View style={styles.row}>
          <Pressable
            onPress={() => void take()}
            disabled={disabled}
            accessibilityRole="button"
            accessibilityLabel={has ? 'Nieuwe foto maken' : 'Foto maken'}
            accessibilityState={{ disabled }}
            style={[styles.action, { backgroundColor: colors.tintBlue }, disabled && styles.dim]}
          >
            <AppText variant="link" color={colors.linkText}>
              {has ? 'Opnieuw maken' : 'Foto maken'}
            </AppText>
          </Pressable>
          <Pressable
            onPress={() => void choose()}
            disabled={disabled}
            accessibilityRole="button"
            accessibilityLabel="Foto kiezen uit je fotogalerij"
            accessibilityState={{ disabled }}
            style={[styles.action, { backgroundColor: colors.surfaceMuted }, disabled && styles.dim]}
          >
            <AppText variant="link" color={colors.textSecondary}>
              Kiezen
            </AppText>
          </Pressable>
        </View>
      )}
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { gap: 10, padding: 16, borderRadius: radius.md },
  head: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  preview: { width: '100%', height: 180, borderRadius: radius.sm },
  row: { flexDirection: 'row', gap: 8 },
  action: { flex: 1, alignItems: 'center', paddingVertical: 11, borderRadius: radius.sm },
  dim: { opacity: 0.5 },
});
