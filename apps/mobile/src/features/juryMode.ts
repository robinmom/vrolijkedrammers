import { useMe } from '../api/queries';

const juryRoles = ['jury-dansgardefestival', 'hoofdjury-dansgardefestival'];

/**
 * Jury-modus (2026-11-02): een jurylid van het Dansgardefestival ziet alleen het jureren, zonder tabbalk en zonder de
 * rest van de app. Alleen een uitlogknop erbij.
 *
 * De voorwaarde is bewust tweeledig: de rol én geen gekoppeld lid. Juryleden zijn uitgenodigde accounts van buiten de
 * vereniging (een lid mág niet jureren, want de eigen dansgroepen doen mee). Zou alleen de rol tellen, dan raakt een
 * bestuurslid dat toch een juryrol krijgt zijn hele app kwijt; nu houdt zo iemand de gewone app met het jureren onder
 * Meer.
 */
export function useJuryOnly(): boolean {
  const me = useMe();
  const data = me.data;
  if (!data) {
    return false;
  }

  return data.memberId === null && data.roles.some((role) => juryRoles.includes(role.code));
}
