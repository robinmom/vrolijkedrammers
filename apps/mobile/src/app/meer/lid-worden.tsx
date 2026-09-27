import { router } from 'expo-router';
import { lidWorden } from '../../content/static';
import { InfoPage } from '../../features/InfoPage';
import { Button } from '../../ui';

/** "Word ook een Drammer!": informatie en het aanmeldformulier (fase 9b). */
export default function LidWordenScreen() {
  return (
    <InfoPage staticContent title="Lid worden" sections={lidWorden}>
      <Button label="Aanmelden" onPress={() => router.push('/meer/aanmelden')} />
      <Button label="Neem contact op" icon="contact" variant="secondary" onPress={() => router.push('/meer/contact')} />
    </InfoPage>
  );
}
