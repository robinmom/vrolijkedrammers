import { useState } from 'react';
import { useAuditLog, type AuditEntry, type AuditFilters } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

const helper = columnHelper<AuditEntry>();
const columns = [
  helper.accessor('occurredAt', { header: 'Tijdstip', cell: (info) => formatDateTime(info.getValue()) }),
  helper.accessor('actorName', { header: 'Door', cell: (info) => info.getValue() ?? info.row.original.actorType }),
  helper.accessor('action', { header: 'Actie', cell: (info) => <code>{info.getValue()}</code> }),
  helper.accessor('entityType', { header: 'Object', cell: (info) => `${info.getValue()} ${info.row.original.entityId}` }),
  helper.display({
    id: 'details',
    header: 'Details',
    cell: (info) =>
      info.row.original.oldValues || info.row.original.newValues ? (
        <details>
          <summary>Toon</summary>
          {info.row.original.oldValues ? <pre aria-label="Oud">{info.row.original.oldValues}</pre> : null}
          {info.row.original.newValues ? <pre aria-label="Nieuw">{info.row.original.newValues}</pre> : null}
        </details>
      ) : (
        '—'
      ),
  }),
];

const EMPTY: AuditEntry[] = [];
const noFilters: AuditFilters = { action: '', entityType: '', from: '', to: '' };

/** Auditlog (read-only); gevoelige velden zijn door de API gemaskeerd. */
export function AuditLogPage() {
  const [draft, setDraft] = useState(noFilters);
  const [filters, setFilters] = useState(noFilters);
  const [page, setPage] = useState(1);
  const log = useAuditLog(filters, page);

  return (
    <>
      <h1>Auditlog</h1>
      <form
        className="toolbar"
        aria-label="Filters"
        onSubmit={(e) => {
          e.preventDefault();
          setPage(1);
          setFilters(draft);
        }}
      >
        <Field label="Actie begint met" value={draft.action} onChange={(e) => setDraft({ ...draft, action: e.target.value })} />
        <Field label="Objecttype" value={draft.entityType} onChange={(e) => setDraft({ ...draft, entityType: e.target.value })} />
        <Field label="Vanaf" type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
        <Field label="Tot" type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
        <button type="submit" className="button secondary">
          Filteren
        </button>
      </form>
      <ProblemAlert error={log.error} />
      <DataTable caption="Auditregels" columns={columns} data={log.data?.items ?? EMPTY} />
      {log.data ? <Pagination page={log.data.page} pageSize={log.data.pageSize} totalCount={log.data.totalCount} onPage={setPage} /> : null}
    </>
  );
}
