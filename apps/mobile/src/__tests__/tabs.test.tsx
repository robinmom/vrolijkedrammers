import { renderRouter } from 'expo-router/testing-library';
import TabLayout from '../app/(tabs)/_layout';
import HomeScreen from '../app/(tabs)/index';
import MeerScreen from '../app/(tabs)/meer';
import NieuwsScreen from '../app/(tabs)/nieuws';
import OptochtScreen from '../app/(tabs)/optocht';
import ProgrammaScreen from '../app/(tabs)/programma';

describe('tabnavigatie', () => {
  it('toont de 5 tabs uit Figma in de juiste volgorde', async () => {
    // RNTL 14 rendert asynchroon; renderRouter geeft daarom een Promise terug.
    const view = await renderRouter(
      {
        '(tabs)/_layout': TabLayout,
        '(tabs)/index': HomeScreen,
        '(tabs)/programma': ProgrammaScreen,
        '(tabs)/optocht': OptochtScreen,
        '(tabs)/nieuws': NieuwsScreen,
        '(tabs)/meer': MeerScreen,
      },
      { initialUrl: '/' },
    );
    const labels = ['Home', 'Programma', 'Optocht', 'Nieuws', 'Meer'];
    for (const label of labels) {
      expect(await view.findAllByText(label)).not.toHaveLength(0);
    }
    expect(view.getByText('De Vrolijke Drammers')).toBeTruthy();
  });
});
