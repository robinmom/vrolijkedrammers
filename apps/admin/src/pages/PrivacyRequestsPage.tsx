import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { usePrivacyRequests, type PrivacyRequest } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Pagination } from '../components/Pagination';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/** Opent een verse downloadlink (15 minuten geldig) voor een export die nog te downloaden is. */
function DownloadButton({ request }: { request: PrivacyRequest }) {
  const api = useApi();
  const [error, setError] = useState<unknown>(null);
  return (
    <>
      <button
        type="button"
        className="button secondary small"
        onClick={async () => {
          setError(null);
          try {
            const { data } = await api.GET('/api/v1/admin/privacy-requests/{id}/download', { params: { path: { id: request.id } } });
            if (data?.downloadUrl) {
              window.open(data.downloadUrl, '_blank', 'noopener');
            }
          } catch (e) {
            setError(e);
          }
        }}
      >
        Downloaden
      </button>
      <ProblemAlert error={error} />
    </>
  );
}

const helper = columnHelper<PrivacyRequest>();
const columns = [
  helper.accessor('requestedAt', { header: 'Datum', cell: (info) => formatDateTime(info.getValue()) }),
  helper.accessor('type', { header: 'Soort', cell: (info) => (info.getValue() === 'Export' ? 'Inzage (export)' : 'Wissen') }),
  helper.accessor('subjectName', {
    header: 'Betreft',
    cell: (info) => (
      <Link to="/gebruikers/$id" params={{ id: info.row.original.userId }}>
        {info.getValue() ?? 'Onbekend'}
      </Link>
    ),
  }),
  helper.accessor('requestedByBoard', { header: 'Door', cell: (info) => info.getValue() ?? 'Het lid zelf (app)' }),
  helper.accessor('status', {
    header: 'Status',
    cell: (info) => <span className={`badge ${info.getValue() === 'Completed' ? 'ok' : 'warn'}`}>{info.getValue() === 'Completed' ? 'Afgerond' : 'Bezig'}</span>,
  }),
  helper.display({
    id: 'download',
    header: () => <span className="visually-hidden">Downloaden</span>,
    cell: (info) =>
      info.row.original.downloadableUntil ? (
        <>
          <DownloadButton request={info.row.original} />
          <div className="muted small-text">tot {formatDateTime(info.row.original.downloadableUntil)}</div>
        </>
      ) : null,
  }),
];

/**
 * AVG-verzoeken (fase 9b-2): inzage-exports (door leden in de app of door het bestuur) en wissingen. Een export namens
 * een lid of het wissen van alle app-gegevens start je op de pagina van de gebruiker.
 */
export function PrivacyRequestsPage() {
  const [page, setPage] = useState(1);
  const requests = usePrivacyRequests(page);
  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">AVG-verzoeken</h1>
          <p className="page-subtitle">
            Inzage en wissen van de gegevens die de app bewaart. Een verzoek namens een lid (per brief of e-mail) start je bij{' '}
            <Link to="/gebruikers">Gebruikers</Link> → de gebruiker → Privacy. De ledenadministratie in e-Boekhouden valt hierbuiten; die past
            het secretariaat zelf aan.
          </p>
        </div>
      </div>
      <ProblemAlert error={requests.error} />
      <DataTable
        caption="AVG-verzoeken"
        columns={columns}
        data={requests.data?.items ?? []}
        emptyText="Nog geen AVG-verzoeken."
        footer={
          requests.data ? (
            <Pagination page={requests.data.page} pageSize={requests.data.pageSize} totalCount={requests.data.totalCount} onPage={setPage} noun="verzoeken" />
          ) : null
        }
      />
    </>
  );
}
