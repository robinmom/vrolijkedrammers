import { locatie } from '../../content/static';
import { InfoPage } from '../../features/InfoPage';
import { openInMaps } from '../../lib/calendar';
import { Button } from '../../ui';

/** Meer → Locatie (plaatshouder tot het bestuur het adres aanlevert). */
export default function LocatieScreen() {
  const query = locatie.address ?? locatie.name;
  return (
    <InfoPage staticContent title="Locatie" sections={[{ heading: locatie.address ?? undefined, paragraphs: [locatie.intro] }]}>
      <Button label="Open in kaarten" icon="locatie" variant="secondary" onPress={() => openInMaps(query, locatie.coordinates ?? undefined)} />
    </InfoPage>
  );
}
