import { Alert, type AlertButton, type AlertOptions } from 'react-native';
import { appText } from '../content/texts';

/**
 * `Alert.alert` met de teksten uit het portal. Een melding van het toestel gaat niet door AppText, dus de vervanging
 * (zie content/texts.tsx) gebeurt hier. Een melding komt uit een knop en niet uit het tekenen, dus hij leest de
 * laatste stand via `appText` in plaats van de hook.
 *
 * De aanroep blijft verder precies die van `Alert.alert`, ook in het aantal argumenten: een melding zonder knoppen
 * krijgt er hier geen lege bij.
 */
export function showAlert(title: string, message?: string, buttons?: AlertButton[], options?: AlertOptions) {
  const heading = appText(title);
  const body = message === undefined ? undefined : appText(message);
  const choices = buttons?.map((button) => (button.text === undefined ? button : { ...button, text: appText(button.text) }));
  if (options !== undefined) {
    Alert.alert(heading, body, choices, options);
  } else if (choices !== undefined) {
    Alert.alert(heading, body, choices);
  } else {
    Alert.alert(heading, body);
  }
}
