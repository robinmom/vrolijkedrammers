using Drammers.Infrastructure.Content.Import;
using Drammers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Website.Content;

/// <summary>
/// Oude adressen van de WordPress-site (fase 21e): eerst de tabel met doorverwijzingen (gevuld door de import), dan een
/// paar vaste regels per map. Geeft het nieuwe adres of <c>null</c>.
/// </summary>
public sealed class Redirects(DrammersDbContext db)
{
    private static readonly (string Prefix, string Target)[] Prefixes =
    [
        ("/prins/", "/prinsengalerie"),
        ("/jeugdprins/", "/jeugdprinsen"),
        ("/commissielid/", "/kader"),
        ("/evenementen/", "/agenda"),
        ("/category/", "/nieuws"),
        ("/tag/", "/nieuws"),
        ("/page/", "/nieuws"),
        ("/sponsoren/", "/"),
        ("/historie/", "/historie"),
    ];

    public async Task<string?> FindAsync(string path, CancellationToken cancellationToken)
    {
        var normalized = WebsiteImporter.NormalizePath(path);
        var target = await db.WebsiteRedirects.AsNoTracking().Where(r => r.FromPath == normalized).Select(r => r.ToPath).FirstOrDefaultAsync(cancellationToken);
        if (target is not null)
        {
            return target;
        }

        if (normalized.StartsWith("/commissies/", StringComparison.Ordinal))
        {
            return $"/kader?commissie={Uri.EscapeDataString(normalized["/commissies/".Length..])}";
        }

        return Prefixes.Where(p => normalized.StartsWith(p.Prefix, StringComparison.Ordinal)).Select(p => p.Target).FirstOrDefault();
    }
}
