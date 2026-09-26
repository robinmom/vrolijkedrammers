export function Pagination({
  page,
  pageSize,
  totalCount,
  onPage,
  noun,
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  onPage: (page: number) => void;
  /** Bijv. "leden": toont links "1–50 van 452 leden" (Figma). */
  noun?: string;
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(totalCount, page * pageSize);
  return (
    <nav className="pagination" aria-label="Paginering">
      {noun ? (
        <span className="pagination-info">
          {from}–{to} van {totalCount} {noun}
        </span>
      ) : null}
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
