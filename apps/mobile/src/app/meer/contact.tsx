import { Linking } from 'react-native';
import { useAppConfig } from '../../api/queries';
import { contact } from '../../content/static';
import { InfoPage } from '../../features/InfoPage';
import { Button } from '../../ui';

/** Meer → Contact. Het e-mailadres komt uit `/app-config` (supportadres, beheerbaar in het portal). */
export default function ContactScreen() {
  const config = useAppConfig();
  const email = contact.email ?? (config.data?.supportEmail || null);
  return (
    <InfoPage staticContent title="Contact" sections={[{ paragraphs: [contact.intro] }]}>
      {email ? <Button label={`Mail ${email}`} icon="contact" onPress={() => Linking.openURL(`mailto:${email}`)} /> : null}
      <Button label="Naar de website" variant="secondary" onPress={() => Linking.openURL(contact.website)} />
    </InfoPage>
  );
}
