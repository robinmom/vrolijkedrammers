import { vereniging } from '../../content/static';
import { InfoPage } from '../../features/InfoPage';

/** Meer → Vereniging (plaatshoudertekst, content/static.ts). */
export default function VerenigingScreen() {
  return <InfoPage staticContent title="Vereniging" sections={vereniging} />;
}
