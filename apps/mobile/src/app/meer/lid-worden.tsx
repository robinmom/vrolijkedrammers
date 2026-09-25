import { router } from 'expo-router';
import { lidWorden } from '../../content/static';
import { InfoPage } from '../../features/InfoPage';
import { Button } from '../../ui';

/** "Word ook een Drammer!": informatiepagina; het aanmeldformulier volgt in fase 9. */
export default function LidWordenScreen() {
  return (
    <InfoPage staticContent title="Lid worden" sections={lidWorden}>
      <Button label="Neem contact op" icon="contact" variant="secondary" onPress={() => router.push('/meer/contact')} />
    </InfoPage>
  );
}
