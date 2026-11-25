namespace SportsWeek.Application.Services;

public static class Paging
{
    public const int MaxPageSize = 100;
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, MaxPageSize));
}
