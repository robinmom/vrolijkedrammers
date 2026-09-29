using Drammers.Worker.Scheduling;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Sales;

/// <summary>Instellingen <c>Sales__*</c> (fase 19).</summary>
public sealed class SalesOptions
{
    public const string SectionName = "Sales";

    /// <summary>Publiek adres van de API (voor links in e-mails die een job verstuurt), bijv. <c>https://app-dvd-api-dev.azurewebsites.net</c>.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Ruimt onbetaalde bestellingen op (fase 19). De capaciteit hangt hier niet van af: een bestelling telt na
/// <c>HoldUntil</c> al niet meer mee. De job zet de status op verlopen, kijkt eerst bij Mollie of er toch betaald is
/// (gemiste webhook) en markeert verlopen uitnodigingen van de wachtlijst. Eén keer per nacht, zodat de serverless
/// database de rest van de tijd kan pauzeren.
/// </summary>
public sealed class SaleExpiryJob(TicketSales sales, IOptions<SalesOptions> options) : IRecurringJob
{
    public const string JobName = "sale-expiry";

    public async Task ExecuteAsync(CancellationToken cancellationToken) =>
        await sales.ExpireAsync(options.Value.PublicBaseUrl?.TrimEnd('/'), cancellationToken);
}
