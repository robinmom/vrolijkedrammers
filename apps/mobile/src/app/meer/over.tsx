import { installedVersion } from '../../shell/AppGate';
import { InfoPage } from '../../features/InfoPage';

/** Meer → Over de app. */
export default function OverScreen() {
  return (
    <InfoPage
      title="Over de app"
      sections={[
        { paragraphs: ['De officiële app van CV De Vrolijke Drammers uit Loil: het programma, nieuws, foto\'s en alles over de optocht.'] },
        { heading: 'Versie', paragraphs: [installedVersion] },
        { heading: 'Privacy', paragraphs: ['Zonder account verzamelt de app geen persoonsgegevens. Opgeslagen content blijft alleen op dit toestel.'] },
      ]}
    />
  );
}
