import { render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';
import { CardColumns, Columns, MAX_CONTENT_WIDTH, MAX_READING_WIDTH, Screen } from '../ui';
import { PHONE_SIZE, setWindowSize, TABLET_SIZE } from '../test/render';
import { ThemeProvider } from '../theme/ThemeProvider';

/**
 * De tabletindeling van de kaartenschermen (2026-11-05). Op een iPad in landschap stond alles in een band van 700
 * punten met veel leegte ernaast; kaarten hebben die leesbeperking niet nodig.
 */

const wrap = (node: React.ReactNode) => render(<ThemeProvider mode="light">{node}</ThemeProvider>);

afterEach(() => setWindowSize(PHONE_SIZE));

describe('Tabletindeling', () => {
  it('een kaartenscherm mag breder zijn dan een leesscherm', async () => {
    await wrap(
      <Screen wide>
        <Text testID="inhoud">Kaarten</Text>
      </Screen>,
    );
    expect(screen.getByTestId('screen-column').props.style).toMatchObject({ maxWidth: MAX_CONTENT_WIDTH });
  });

  it('zonder die keuze blijft de leesbreedte gelden, ook op een tablet', async () => {
    setWindowSize(TABLET_SIZE);
    await wrap(
      <Screen>
        <Text>Lopende tekst</Text>
      </Screen>,
    );
    expect(screen.getByTestId('screen-column').props.style).toMatchObject({ maxWidth: MAX_READING_WIDTH });
  });

  it('Columns zet de blokken naast elkaar op een tablet', async () => {
    setWindowSize(TABLET_SIZE);
    await wrap(<Columns left={<Text testID="links">A</Text>} right={<Text testID="rechts">B</Text>} />);
    expect(screen.getByTestId('columns-row')).toBeTruthy();
    expect(screen.getByTestId('links')).toBeTruthy();
    expect(screen.getByTestId('rechts')).toBeTruthy();
  });

  it('op een telefoon komt er geen enkele laag omheen, dus de indeling blijft zoals hij was', async () => {
    await wrap(<Columns left={<Text testID="links">A</Text>} right={<Text testID="rechts">B</Text>} />);
    // Geen rij eromheen: anders zouden de afstanden op elke telefoon verschuiven.
    expect(screen.queryByTestId('columns-row')).toBeNull();
    expect(screen.getByTestId('links')).toBeTruthy();
  });

  it('CardColumns verdeelt op volgorde: eerste helft links', async () => {
    setWindowSize(TABLET_SIZE);
    await wrap(
      <CardColumns>
        {[1, 2, 3, 4, 5].map((n) => (
          <Text key={n} testID={`kaart-${n}`}>
            {n}
          </Text>
        ))}
      </CardColumns>,
    );
    const kolomVan = (n: number) => screen.getByTestId(`kaart-${n}`).parent;
    // 5 kaarten: 1-3 links, 4-5 rechts. Zo blijft de leesvolgorde links-boven naar rechts-onder.
    expect(kolomVan(1)).toBe(kolomVan(3));
    expect(kolomVan(4)).toBe(kolomVan(5));
    expect(kolomVan(1)).not.toBe(kolomVan(4));
  });

  it('CardColumns laat één kaart met rust', async () => {
    setWindowSize(TABLET_SIZE);
    await wrap(
      <CardColumns>
        <Text testID="kaart-1">1</Text>
      </CardColumns>,
    );
    expect(screen.getByTestId('kaart-1')).toBeTruthy();
  });
});
