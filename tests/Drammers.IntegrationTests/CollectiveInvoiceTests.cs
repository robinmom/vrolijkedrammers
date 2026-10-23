using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 4d: verzamelfacturen per onderdeel en carnavalsjaar, met de delta (alleen nieuw-betaalde regels) en de PDF.</summary>
[Collection(SqlServerCollection.Name)]
public class CollectiveInvoiceTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur-vf@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task SeedContributionRunAsync(DateOnly date, params (string Number, string Name, decimal Amount)[] lines)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var run = new CollectionRun
        {
            Id = IdGenerator.NewId(),
            Kind = CollectionKind.Contribution,
            CollectionDate = date,
            Description = "Contributie-incasso",
            MessageId = $"MSG-{Guid.NewGuid():N}"[..20],
            LineCount = lines.Length,
            Total = lines.Sum(l => l.Amount),
        };
        db.CollectionRuns.Add(run);
        foreach (var (number, name, amount) in lines)
        {
            db.CollectionRunLines.Add(new CollectionRunLine
            {
                RunId = run.Id,
                MemberNumber = number,
                DebtorName = name,
                Amount = amount,
                MandateReference = $"M-{number}",
                IbanLast4 = "1234",
                IbanProtected = "x",
                EndToEndId = $"E-{number}",
                Description = "Contributie",
                SequenceType = SequenceType.Rcur,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Delta_factuur_pakt_alleen_nieuw_betaalde_regels_en_levert_een_pdf()
    {
        // Twee geïnde contributieregels binnen het seizoen 2026/2027.
        await SeedContributionRunAsync(new DateOnly(2026, 12, 15), ("1001", "Piet Pietersen", 50m), ("1002", "Klaas Klaassen", 75m));

        // De preview toont beide regels; er is nog geen factuur.
        var before = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/collective-invoices?source=Contribution");
        Assert.Empty(before.GetProperty("invoices").EnumerateArray());
        Assert.Equal(2, before.GetProperty("preview").GetProperty("newLines").GetArrayLength());
        Assert.Equal(125m, before.GetProperty("preview").GetProperty("total").GetDecimal());

        // Factuur maken.
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/finance/collective-invoices", new { carnivalYearId = (int?)null, source = "Contribution" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Nu staat er één factuur en is de delta leeg.
        var afterFirst = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/collective-invoices?source=Contribution");
        Assert.Single(afterFirst.GetProperty("invoices").EnumerateArray());
        Assert.Empty(afterFirst.GetProperty("preview").GetProperty("newLines").EnumerateArray());

        // Nog een keer maken kan niet (niets nieuws) → 409.
        var again = await _bestuur.PostAsJsonAsync("/api/v1/admin/finance/collective-invoices", new { carnivalYearId = (int?)null, source = "Contribution" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // De detailregels en de PDF.
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/finance/collective-invoices/{id}");
        Assert.Equal(2, detail.GetProperty("lines").GetArrayLength());
        var pdf = await _bestuur.GetAsync($"/api/v1/admin/finance/collective-invoices/{id}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes, 0, 4), StringComparison.Ordinal);

        // Een nieuwe geïnde regel → de delta pakt alleen die; de tweede factuur heeft één regel.
        await SeedContributionRunAsync(new DateOnly(2027, 1, 20), ("1003", "Jan Jansen", 60m));
        var delta = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/collective-invoices?source=Contribution");
        Assert.Equal(1, delta.GetProperty("preview").GetProperty("newLines").GetArrayLength());
        var second = await _bestuur.PostAsJsonAsync("/api/v1/admin/finance/collective-invoices", new { carnivalYearId = (int?)null, source = "Contribution" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var afterSecond = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/collective-invoices?source=Contribution");
        Assert.Equal(2, afterSecond.GetProperty("invoices").GetArrayLength());
    }
}
