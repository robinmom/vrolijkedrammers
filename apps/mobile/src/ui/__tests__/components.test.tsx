import { fireEvent, render, screen } from '@testing-library/react-native';
import type { ReactElement } from 'react';
import { ThemeProvider, type ThemeMode } from '../../theme/ThemeProvider';
import { Button, EventCard, FilterChips, SettingsList, ShortcutTile, tintSvg } from '../index';
import { icons } from '../icons.generated';

async function renderIn(mode: ThemeMode, ui: ReactElement) {
  return await render(<ThemeProvider mode={mode}>{ui}</ThemeProvider>);
}

describe.each<ThemeMode>(['light', 'dark'])('snapshots in thema %s', (mode) => {
  it('Button primair en secundair', async () => {
    const tree = await renderIn(
      mode,
      <>
        <Button label="Groep inschrijven" icon="plus" />
        <Button label="Route" icon="locatie" variant="secondary" />
      </>,
    );
    expect(tree.toJSON()).toMatchSnapshot();
  });

  it('EventCard met badge en hoogtepunt', async () => {
    const tree = await renderIn(
      mode,
      <EventCard
        highlighted
        title="Optocht Loil"
        date={{ weekday: 'ZO', day: '07', month: 'FEB' }}
        time="13:30 uur"
        location="Centrum Loil"
        badge={{ label: 'Hoogtepunt', variant: 'highlight' }}
      />,
    );
    expect(tree.toJSON()).toMatchSnapshot();
  });

  it('ShortcutTile', async () => {
    const tree = await renderIn(mode, <ShortcutTile icon="fotos" label="Foto's" tint="blue" />);
    expect(tree.toJSON()).toMatchSnapshot();
  });
});

describe('EventCard', () => {
  it('heeft één toegankelijk label met titel, datum, tijd en locatie', async () => {
    await renderIn('light', <EventCard title="Prinsenbal" date={{ weekday: 'ZA', day: '21', month: 'NOV' }} time="20:00 uur" location="Feestzaal De Drammer" />);
    expect(screen.getByRole('button', { name: 'Prinsenbal, ZA 21 NOV, 20:00 uur, Feestzaal De Drammer' })).toBeTruthy();
  });
});

describe('FilterChips', () => {
  it('markeert de actieve chip en meldt een wijziging', async () => {
    const onChange = jest.fn();
    await renderIn(
      'light',
      <FilterChips
        accessibilityLabel="Filter"
        selected="alle"
        onChange={onChange}
        options={[
          { value: 'alle', label: 'Alle' },
          { value: 'jeugd', label: 'Jeugd' },
        ]}
      />,
    );
    expect(screen.getByRole('button', { name: 'Alle', selected: true })).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Jeugd' }));
    expect(onChange).toHaveBeenCalledWith('jeugd');
  });
});

describe('SettingsList', () => {
  it('schakelt een toggle en opent een link', async () => {
    const onToggle = jest.fn();
    const onPress = jest.fn();
    await renderIn(
      'light',
      <SettingsList
        items={[
          { type: 'toggle', key: 'push', label: 'Pushmeldingen', value: true, onValueChange: onToggle },
          { type: 'link', key: 'over', label: 'Over de app', onPress },
        ]}
      />,
    );
    await fireEvent(screen.getByLabelText('Pushmeldingen'), 'valueChange', false);
    await fireEvent.press(screen.getByRole('button', { name: 'Over de app' }));
    expect(onToggle).toHaveBeenCalledWith(false);
    expect(onPress).toHaveBeenCalled();
  });
});

describe('tintSvg', () => {
  it('vervangt alle stroke-kleuren en verwijdert stroke-opacity, zonder de bron te wijzigen', async () => {
    const original = icons.programma;
    const tinted = tintSvg(original, '#D4000F');
    expect(tinted).not.toMatch(/stroke="#123047"/);
    expect(tinted).not.toContain('stroke-opacity');
    expect(tinted.match(/stroke="#D4000F"/g)?.length).toBe(original.match(/stroke="/g)?.length);
    expect(icons.programma).toBe(original);
  });

  it('bevat alle iconen die de componenten gebruiken', async () => {
    for (const name of ['home', 'programma', 'optocht', 'nieuws', 'meer', 'chevron', 'klok', 'locatie', 'plus'] as const) {
      expect(icons[name]).toContain('<svg');
    }
  });
});
