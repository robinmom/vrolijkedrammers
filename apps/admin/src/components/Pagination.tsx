export function Pagination({
  page,
  pageSize,
  totalCount,
  onPage,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  return (
    <nav className="pagination" aria-label="Paginering">
      <button type="button" className="button secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>
        Vorige
      </button>
      <span>
        Pagina {page} van {pages} ({totalCount} totaal)
      </span>
      <button type="button" className="button secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>
        Volgende
      </button>
    </nav>
  );
}
