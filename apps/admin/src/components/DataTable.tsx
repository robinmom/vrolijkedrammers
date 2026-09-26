import {
  columnVisibilityFeature,
  createColumnHelper,
  createSortedRowModel,
  rowSortingFeature,
  tableFeatures,
  useTable,
  type ColumnDef,
  type RowData,
} from '@tanstack/react-table';
import { useId, useState, type ReactNode } from 'react';

/** Features van de generieke tabel: sorteren en kolomkeuze. Zoeken en pagineren gebeuren server-side. */
export const tableFeaturesDef = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  columnVisibilityFeature,
});

export type TableFeatures = typeof tableFeaturesDef;

export function columnHelper<TData extends RowData>() {
  return createColumnHelper<TableFeatures, TData>();
}

const sortLabels = { asc: 'oplopend', desc: 'aflopend' } as const;

/**
 * Generieke beheertabel (fase 4): sorteren per kolom (klik op de kop), kolommen tonen/verbergen, toegankelijke
 * koppen met <code>aria-sort</code>.
 */
export function DataTable<TData extends RowData>({
  caption,
  columns,
  data,
  emptyText = 'Geen resultaten.',
  header,
  footer,
  columnPicker = true,
}: {
  caption: string;
  /** Inhoud boven de tabel in dezelfde kaart, bijv. zoeken en filters (Figma "Leden"). */
  header?: ReactNode;
  /** Inhoud onder de tabel in dezelfde kaart, bijv. paginering. */
  footer?: ReactNode;
  /** Knop "Kolommen" tonen (standaard aan). */
  columnPicker?: boolean;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- kolommen met verschillende waardetypes
  columns: ColumnDef<TableFeatures, TData, any>[];
  data: TData[];
  emptyText?: string;
}) {
  const [showColumns, setShowColumns] = useState(false);
  const columnsId = useId();
  const table = useTable({ features: tableFeaturesDef, columns, data });

  return (
    <div className="table-wrapper">
      {header ? <div className="table-header">{header}</div> : null}
      {columnPicker ? (
      <div className="table-toolbar">
        <button
          type="button"
          className="button secondary small"
          aria-expanded={showColumns}
          aria-controls={columnsId}
          onClick={() => setShowColumns((v) => !v)}
        >
          Kolommen
        </button>
        {showColumns ? (
          <fieldset id={columnsId} className="column-picker">
            <legend>Zichtbare kolommen</legend>
            {table
              .getAllLeafColumns()
              .filter((column) => column.getCanHide())
              .map((column) => (
                <label key={column.id}>
                  <input
                    type="checkbox"
                    checked={column.getIsVisible()}
                    onChange={() => column.toggleVisibility()}
                  />{' '}
                  {typeof column.columnDef.header === 'string' ? column.columnDef.header : column.id}
                </label>
              ))}
          </fieldset>
        ) : null}
      </div>
      ) : null}
      <div className="table-scroll" tabIndex={0} role="region" aria-label={caption}>
        <table className="table">
          <caption className="visually-hidden">{caption}</caption>
          <thead>
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {group.headers.map((header) => {
                  const sorted = header.column.getIsSorted();
                  return (
                    <th
                      key={header.id}
                      scope="col"
                      aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
                    >
                      {header.isPlaceholder ? null : header.column.getCanSort() ? (
                        <button
                          type="button"
                          className="sort-button"
                          onClick={header.column.getToggleSortingHandler()}
                        >
                          <table.FlexRender header={header} />
                          <span aria-hidden="true">{sorted === 'asc' ? ' ▲' : sorted === 'desc' ? ' ▼' : ''}</span>
                          <span className="visually-hidden">{sorted ? `, ${sortLabels[sorted]} gesorteerd` : ''}</span>
                        </button>
                      ) : (
                        <table.FlexRender header={header} />
                      )}
                    </th>
                  );
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td colSpan={table.getAllLeafColumns().length} className="muted">
                  {emptyText}
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id}>
                      <table.FlexRender cell={cell} />
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      {footer ? <div className="table-footer">{footer}</div> : null}
    </div>
  );
}
