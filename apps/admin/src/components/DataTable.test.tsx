import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DataTable, columnHelper } from './DataTable';

interface Row {
  name: string;
  count: number;
}
const helper = columnHelper<Row>();
const columns = [helper.accessor('name', { header: 'Naam' }), helper.accessor('count', { header: 'Aantal' })];
const data: Row[] = [
  { name: 'Bert', count: 2 },
  { name: 'Anna', count: 5 },
];

describe('DataTable', () => {
  it('sorteert op een kolom met aria-sort', () => {
    render(<DataTable caption="Test" columns={columns} data={data} />);
    fireEvent.click(screen.getByRole('button', { name: /Naam/ }));
    expect(screen.getByRole('columnheader', { name: /Naam/ })).toHaveAttribute('aria-sort', 'ascending');
    const rows = screen.getAllByRole('row').slice(1);
    expect(within(rows[0]!).getByText('Anna')).toBeInTheDocument();
  });

  it('kan kolommen verbergen', () => {
    render(<DataTable caption="Test" columns={columns} data={data} />);
    fireEvent.click(screen.getByRole('button', { name: 'Kolommen' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Aantal' }));
    expect(screen.queryByRole('columnheader', { name: /Aantal/ })).not.toBeInTheDocument();
  });

  it('toont een lege-staat', () => {
    render(<DataTable caption="Test" columns={columns} data={[]} />);
    expect(screen.getByText('Geen resultaten.')).toBeInTheDocument();
  });
});
