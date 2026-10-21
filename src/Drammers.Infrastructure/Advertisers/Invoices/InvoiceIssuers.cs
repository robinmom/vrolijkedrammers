using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Advertisers.Invoices;

/// <summary>
/// De gegevens van de vereniging voor op een factuur-PDF (naam en IBAN uit de incasso, adres/KvK/btw/telefoon/website uit
/// de factuurinstellingen). Gedeeld door de adverteerders-facturen en het dansgardefestival.
/// </summary>
public sealed class InvoiceIssuers(DrammersDbContext db, SepaCollections collections)
{
    public async Task<InvoiceIssuer> IssuerAsync(string email, CancellationToken cancellationToken)
    {
        var creditor = await collections.GetCreditorAsync(cancellationToken);
        string[] keys =
        [
            AppConfigurationKeys.InvoiceAddress, AppConfigurationKeys.InvoiceKvk, AppConfigurationKeys.InvoiceVatNumber,
            AppConfigurationKeys.InvoicePhone, AppConfigurationKeys.InvoiceWebsite,
        ];
        var values = await db.AppConfiguration.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        var lines = (values.GetValueOrDefault(AppConfigurationKeys.InvoiceAddress) ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new InvoiceIssuer(
            creditor.Name ?? "Carnavalsvereniging De Vrolijke Drammers",
            creditor.Iban,
            lines,
            values.TryGetValue(AppConfigurationKeys.InvoiceKvk, out var kvk) ? kvk : AdvertiserInvoices.DefaultKvk,
            email,
            values.GetValueOrDefault(AppConfigurationKeys.InvoiceVatNumber),
            values.GetValueOrDefault(AppConfigurationKeys.InvoicePhone),
            values.GetValueOrDefault(AppConfigurationKeys.InvoiceWebsite));
    }
}
