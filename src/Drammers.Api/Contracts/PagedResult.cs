namespace Drammers.Api.Contracts;

/// <summary>Pagineringsconventie van de API (docs/05 §1): <c>?page=1&amp;pageSize=25</c>, maximaal 100.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, MaxPageSize));
}
