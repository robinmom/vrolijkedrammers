using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Content.Shared;
using Drammers.Modules.Content.Website;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Content.Import;

public sealed record WebsiteImportSummary(
    IReadOnlyList<WebsiteImportCount> Counts, IReadOnlyList<WebsiteImportFailure> Failures, bool Running, DateTime? LastActivity);

public sealed record WebsiteImportCount(WebsiteImportKind Kind, int Pending, int Done, int Skipped, int Failed);

public sealed record WebsiteImportFailure(long Id, WebsiteImportKind Kind, string? Title, string? SourceUrl, string? Error);

/// <summary>
/// Zet de oude WordPress-site over (fase 21e). Eerst een werklijst (<see cref="PlanAsync"/>), daarna in porties van
/// hooguit drie minuten (<see cref="ProcessBatchAsync"/>, ruim binnen de lock van de outbox). Elk item één keer; opnieuw
/// starten gaat verder waar het was. Oude adressen krijgen een doorverwijzing naar de nieuwe pagina.
/// </summary>
public sealed partial class WebsiteImporter(
    DrammersDbContext db, WordPressSource source, WebsiteAdministration website, ContentAdministration content, IOutbox outbox, IAuditLogger audit,
    IClock clock, ILogger<WebsiteImporter> logger)
{
    public const string MessageType = "website.import";

    /// <summary>Vanaf zoveel afbeeldingen wordt een bericht of pagina ook een fotoalbum.</summary>
    public const int GalleryThreshold = 4;

    public static readonly TimeSpan BatchBudget = TimeSpan.FromMinutes(3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Pagina's van de oude site die niet terugkomen (test, dubbel, leeg of vervangen door de nieuwe site).</summary>
    private static readonly HashSet<string> SkippedPages =
    [
        "home", "carnaval", "fotos", "fotos-jeugdpronkzittingen-testing", "facebook-test", "testpagina-aanmelden", "aanmelden-optocht",
        "aanmelden-lid", "contact", "tickets", "programma", "evenementen", "aanmelden-optocht-formulier", "optocht-2025",
    ];

    /// <summary>Vaste doorverwijzingen van oude adressen.</summary>
    private static readonly Dictionary<string, string> StaticRedirects = new()
    {
        ["/home"] = "/",
        ["/aanmelden-lid"] = "/lid-worden/",
        ["/aanmelden-optocht"] = "/optocht-inschrijven/",
        ["/evenementen-ph/optocht/aanmelden-optocht-formulier"] = "/optocht-inschrijven/",
        ["/testpagina-aanmelden"] = "/lid-worden/",
        ["/tickets"] = "/kaarten/",
        ["/programma"] = "/agenda",
        ["/evenementen"] = "/agenda",
        ["/carnaval"] = "/agenda",
        ["/prins"] = "/prinsengalerie",
        ["/jeugdprins"] = "/jeugdprinsen",
        ["/commissies"] = "/kader",
        ["/commissielid"] = "/kader",
        ["/onderscheidingen/type/drammertje"] = "/onderscheidingen?soort=drammertje",
        ["/onderscheidingen/type/verdienstelijke-didammer"] = "/onderscheidingen?soort=verdienstelijke-didammer",
        ["/onderscheidingen/type/eikenloof-van-boschslag"] = "/onderscheidingen?soort=eikenloof-van-boschslag",
        ["/fotos-jeugdpronkzittingen-testing"] = "/fotos",
        ["/facebook-test"] = "/",
        ["/sponsoren"] = "/",
        ["/optocht-2025"] = "/optocht",
    };

    // ----- Plannen -------------------------------------------------------------------------------------------------

    /// <summary>Haalt alles op wat overgezet moet worden en zet het in de werklijst (bestaande items blijven staan).</summary>
    public async Task PlanAsync(CancellationToken cancellationToken)
    {
        var existing = (await db.WebsiteImportItems.Select(i => new { i.Kind, i.SourceKey }).ToListAsync(cancellationToken))
            .Select(i => (i.Kind, i.SourceKey)).ToHashSet();
        var now = clock.UtcNow.UtcDateTime;
        void Add(WebsiteImportKind kind, string key, string? url, string? title, object? payload, WebsiteImportStatus status = WebsiteImportStatus.Pending)
        {
            if (existing.Add((kind, key)))
            {
                db.WebsiteImportItems.Add(new WebsiteImportItem
                {
                    Kind = kind,
                    SourceKey = Truncate(key, 300)!,
                    SourceUrl = Truncate(url, 500),
                    Title = Truncate(title, 300),
                    Payload = payload is null ? null : JsonSerializer.Serialize(payload, Json),
                    Status = status,
                    CreatedAt = now,
                });
            }
        }

        // Kader: per commissie de pagina van de oude site.
        foreach (var committee in await db.Committees.AsNoTracking().ToListAsync(cancellationToken))
        {
            var html = await TryGetHtmlAsync($"commissies/{committee.Slug}/", cancellationToken);
            foreach (var person in html is null ? [] : WordPressSource.ParsePeople(html))
            {
                Add(WebsiteImportKind.Kader, $"{committee.Slug}|{person.Name}", null, person.Name, new { committee.Slug, Person = person });
            }
        }

        foreach (var (kind, path) in new[] { (WebsiteImportKind.Prince, "prins/"), (WebsiteImportKind.YouthPrince, "jeugdprins/") })
        {
            var html = await TryGetHtmlAsync(path, cancellationToken);
            foreach (var person in html is null ? [] : WordPressSource.ParsePeople(html))
            {
                Add(kind, $"{person.Function}|{person.Name}", null, person.Title ?? person.Name, person);
            }
        }

        foreach (var award in await source.GetAllAsync("award", embed: false, cancellationToken))
        {
            Add(WebsiteImportKind.Award, award.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), award.Link, Decode(award.Title.Rendered), award);
        }

        foreach (var page in await source.GetAllAsync("pages", embed: true, cancellationToken))
        {
            var skip = SkippedPages.Contains(page.Slug);
            Add(WebsiteImportKind.Page, page.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), page.Link, Decode(page.Title.Rendered), page,
                skip ? WebsiteImportStatus.Skipped : WebsiteImportStatus.Pending);
        }

        foreach (var post in await source.GetAllAsync("posts", embed: true, cancellationToken))
        {
            Add(WebsiteImportKind.Post, post.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), post.Link, Decode(post.Title.Rendered), post);
        }

        foreach (var (from, to) in StaticRedirects)
        {
            await RedirectAsync(from, to, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("website.import-planned", "WebsiteImport", "wordpress"), cancellationToken);
    }

    // ----- Verwerken -----------------------------------------------------------------------------------------------

    /// <summary>Verwerkt items tot de tijd op is; plant een vervolg als er nog werk ligt. Geeft het aantal verwerkte items.</summary>
    public async Task<int> ProcessBatchAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        while (stopwatch.Elapsed < budget)
        {
            var item = await db.WebsiteImportItems.Where(i => i.Status == WebsiteImportStatus.Pending).OrderBy(i => i.Id).FirstOrDefaultAsync(cancellationToken);
            if (item is null)
            {
                break;
            }

            item.Attempts++;
            try
            {
                var (status, target) = await ProcessAsync(item, cancellationToken);
                (item.Status, item.TargetId, item.Error) = (status, target, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Import van {Kind} {SourceKey} mislukt", item.Kind, item.SourceKey);
                db.ChangeTracker.Clear();
                item = await db.WebsiteImportItems.SingleAsync(i => i.Id == item.Id, cancellationToken);
                (item.Status, item.Error) = (WebsiteImportStatus.Failed, Truncate(ex.Message, 2000));
                item.Attempts++;
            }

            item.ProcessedAt = clock.UtcNow.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            processed++;
        }

        if (await db.WebsiteImportItems.AnyAsync(i => i.Status == WebsiteImportStatus.Pending, cancellationToken))
        {
            outbox.Enqueue(MessageType, new ImportMessage("process"));
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (processed > 0)
        {
            await audit.WriteAsync(new AuditEntry("website.import-finished", "WebsiteImport", "wordpress"), cancellationToken);
        }

        return processed;
    }

    private async Task<(WebsiteImportStatus, string?)> ProcessAsync(WebsiteImportItem item, CancellationToken cancellationToken) => item.Kind switch
    {
        WebsiteImportKind.Kader => await KaderAsync(item, cancellationToken),
        WebsiteImportKind.Prince => await PrinceAsync(item, PrinceKind.Prince, cancellationToken),
        WebsiteImportKind.YouthPrince => await PrinceAsync(item, PrinceKind.YouthPrince, cancellationToken),
        WebsiteImportKind.Award => await AwardAsync(item, cancellationToken),
        WebsiteImportKind.Page => await PageAsync(item, cancellationToken),
        WebsiteImportKind.Post => await PostAsync(item, cancellationToken),
        WebsiteImportKind.GalleryPhoto => await GalleryPhotoAsync(item, cancellationToken),
        _ => (WebsiteImportStatus.Skipped, null),
    };

    private async Task<(WebsiteImportStatus, string?)> KaderAsync(WebsiteImportItem item, CancellationToken cancellationToken)
    {
        var payload = Read<KaderPayload>(item);
        var committee = await db.Committees.SingleAsync(c => c.Slug == payload.Slug, cancellationToken);
        var person = payload.Person;
        if (await db.CommitteeMembers.AnyAsync(m => m.CommitteeId == committee.Id && m.Name == person.Name, cancellationToken))
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        // Koppelen aan de ledenlijst als er precies één lid met deze naam is.
        var members = await db.Members.Where(m => m.FullName == person.Name).Select(m => m.Id).Take(2).ToListAsync(cancellationToken);
        var id = await website.AddCommitteeMemberAsync(
            new CommitteeMemberInput(committee.Id, members.Count == 1 ? members[0] : null, person.Name, Truncate(person.Function, 100),
                await ImageAsync(person.ImageUrl, cancellationToken)), cancellationToken);
        return (WebsiteImportStatus.Done, id.ToString());
    }

    private async Task<(WebsiteImportStatus, string?)> PrinceAsync(WebsiteImportItem item, PrinceKind kind, CancellationToken cancellationToken)
    {
        var person = Read<WpPerson>(item);
        if (!int.TryParse(person.Function?.Trim(), out var year) || year < 1958)
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        var princeName = Truncate(person.Title ?? person.Name, 100)!;
        if (await db.Princes.AnyAsync(p => p.Kind == kind && p.Year == year && p.PrinceName == princeName, cancellationToken))
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        var id = await website.CreatePrinceAsync(new PrinceInput(kind, year, princeName, Truncate(person.Name, 150), Truncate(person.Quote, 500),
            await ImageAsync(person.ImageUrl, cancellationToken)), cancellationToken);
        return (WebsiteImportStatus.Done, id.ToString());
    }

    private async Task<(WebsiteImportStatus, string?)> AwardAsync(WebsiteImportItem item, CancellationToken cancellationToken)
    {
        var wp = Read<WpItem>(item);
        var detail = WordPressSource.ParseAward(await source.GetHtmlAsync(wp.Link, cancellationToken));
        var type = (wp.ClassList ?? []).FirstOrDefault(c => c.StartsWith("award_type-", StringComparison.Ordinal)) switch
        {
            "award_type-drammertje" => AwardType.Drammertje,
            "award_type-eikenloof-van-boschslag" => AwardType.EikenloofVanBoschslag,
            _ => AwardType.VerdienstelijkeDidammer,
        };
        var recipient = Truncate(string.IsNullOrWhiteSpace(detail.Recipient) ? Decode(wp.Title.Rendered) : detail.Recipient, 150)!;
        var year = detail.Year ?? wp.DateUtc.Year;
        var body = HtmlToMarkdown.Convert(detail.ContentHtml).Markdown;
        var id = await website.CreateAwardAsync(new AwardInput(type, year, recipient, body.Length == 0 ? null : body,
            await ImageAsync(detail.ImageUrl, cancellationToken), IsPublished: true), cancellationToken);
        var slug = await db.Awards.Where(a => a.Id == id).Select(a => a.Slug).SingleAsync(cancellationToken);
        await RedirectAsync(PathOf(wp.Link), $"/onderscheidingen/{slug}", cancellationToken);
        return (WebsiteImportStatus.Done, id.ToString());
    }

    private async Task<(WebsiteImportStatus, string?)> PageAsync(WebsiteImportItem item, CancellationToken cancellationToken)
    {
        var wp = Read<WpItem>(item);
        var title = Decode(wp.Title.Rendered);
        var converted = HtmlToMarkdown.Convert(wp.Content?.Rendered);
        var images = converted.Images.Where(source.IsOwnHost).ToList();
        if (images.Count >= GalleryThreshold)
        {
            var album = await GalleryAsync(item, title, DateOnly.FromDateTime(wp.DateUtc), PlainIntro(converted.Markdown), images, cancellationToken);
            if (converted.Markdown.Length >= 300)
            {
                // Een verslag bij de foto's (bijvoorbeeld de pronkzitting): ook als nieuwsbericht, met een link naar het album.
                await NewsAsync(title, wp.Slug, wp.DateUtc, converted.Markdown + $"\n\n[Bekijk de foto's](/fotos/{album:N})", null, [], images[0], cancellationToken);
            }

            await RedirectAsync(PathOf(wp.Link), $"/fotos/{album:N}", cancellationToken);
            return (WebsiteImportStatus.Done, album.ToString());
        }

        if (converted.Markdown.Length == 0)
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        var slug = Slugs.From(wp.Slug);
        if (await db.WebsitePages.AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        var image = images.Count > 0 ? await ImageAsync(images[0], cancellationToken) : await ImageAsync(wp.FeaturedImage, cancellationToken);
        var id = await website.CreatePageAsync(new WebsitePageInput(slug, Truncate(title, 200)!, null, converted.Markdown, image, IsPublished: true, SortOrder: 100), cancellationToken);
        await RedirectAsync(PathOf(wp.Link), $"/{slug}", cancellationToken);
        return (WebsiteImportStatus.Done, id.ToString());
    }

    private async Task<(WebsiteImportStatus, string?)> PostAsync(WebsiteImportItem item, CancellationToken cancellationToken)
    {
        var wp = Read<WpItem>(item);
        var title = Decode(wp.Title.Rendered);
        var converted = HtmlToMarkdown.Convert(wp.Content?.Rendered);
        var images = converted.Images.Where(source.IsOwnHost).ToList();
        var body = converted.Markdown;
        if (images.Count >= GalleryThreshold)
        {
            var album = await GalleryAsync(item, title, DateOnly.FromDateTime(wp.DateUtc), PlainIntro(body), images, cancellationToken);
            body = (body.Length == 0 ? "" : body + "\n\n") + $"[Bekijk de foto's](/fotos/{album:N})";
        }

        var featured = wp.FeaturedImage is { } f && source.IsOwnHost(f) ? f : images.FirstOrDefault();
        var slug = await NewsAsync(title, wp.Slug, wp.DateUtc, body, HtmlToMarkdown.PlainText(wp.Excerpt?.Rendered, 500).Replace("[…]", "", StringComparison.Ordinal).Trim(),
            wp.Categories, featured, cancellationToken);
        await RedirectAsync(PathOf(wp.Link), $"/nieuws/{slug}", cancellationToken);
        return (WebsiteImportStatus.Done, slug);
    }

    private async Task<(WebsiteImportStatus, string?)> GalleryPhotoAsync(WebsiteImportItem item, CancellationToken cancellationToken)
    {
        var payload = Read<GalleryPhotoPayload>(item);
        var bytes = await source.GetImageAsync(payload.Url, cancellationToken);
        if (bytes is null)
        {
            return (WebsiteImportStatus.Skipped, null);
        }

        var ids = await content.UploadPhotosAsync(payload.AlbumId, [new UploadedFile(Path.GetFileName(new Uri(payload.Url).AbsolutePath), bytes.Length, () => new MemoryStream(bytes))], cancellationToken);
        return (WebsiteImportStatus.Done, ids[0].ToString());
    }

    // ----- Hulpfuncties --------------------------------------------------------------------------------------------

    private async Task<Guid> GalleryAsync(
        WebsiteImportItem item, string title, DateOnly date, string? description, IReadOnlyList<string> images, CancellationToken cancellationToken)
    {
        var albumId = await content.CreateAlbumAsync(new AlbumInput(Truncate(title, 200)!, date, Truncate(description, 2000), null,
            new PublicationInput(ContentVisibility.Public, [], PublicationStatus.Published, null), CategoryOf(title)), cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        foreach (var url in images)
        {
            db.WebsiteImportItems.Add(new WebsiteImportItem
            {
                Kind = WebsiteImportKind.GalleryPhoto,
                SourceKey = Truncate($"{item.Kind}:{item.SourceKey}|{url}", 300)!,
                SourceUrl = Truncate(url, 500),
                Title = Truncate(title, 300),
                Payload = JsonSerializer.Serialize(new GalleryPhotoPayload(albumId, url), Json),
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return albumId;
    }

    /// <summary>Maakt een openbaar nieuwsbericht dat ook op de website staat; geeft het webadres terug.</summary>
    private async Task<string> NewsAsync(
        string title, string wpSlug, DateTime publishedAt, string body, string? summary, IEnumerable<string> categories, string? imageUrl,
        CancellationToken cancellationToken)
    {
        var slug = await Slugs.UniqueAsync(Slugs.From(wpSlug), s => db.News.AnyAsync(n => n.Slug == s, cancellationToken));
        var category = categories.FirstOrDefault(c => c is not ("Geen categorie" or "Home" or "Uncategorized"));
        var id = await content.CreateNewsAsync(new NewsInput(
            Truncate(title, 200)!, string.IsNullOrWhiteSpace(summary) ? null : Truncate(summary, 500), body.Length == 0 ? title : body, Truncate(category, 50),
            null, new PublicationInput(ContentVisibility.Public, [], PublicationStatus.Published, publishedAt), PushOnPublish: false,
            ShowOnWebsite: true, Slug: slug, Image: await ImageAsync(imageUrl, cancellationToken)), cancellationToken);
        return await db.News.Where(n => n.Id == id).Select(n => n.Slug!).SingleAsync(cancellationToken);
    }

    /// <summary>Downloadt en verwerkt een afbeelding (virusscan, herschalen); <c>null</c> als hij niet te krijgen is.</summary>
    private async Task<string?> ImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (url is null || !source.IsOwnHost(url))
        {
            return null;
        }

        var bytes = await source.GetImageAsync(url, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        var stored = await website.StoreImageAsync(new UploadedFile(Path.GetFileName(new Uri(url).AbsolutePath), bytes.Length, () => new MemoryStream(bytes)), cancellationToken);
        return stored.Path;
    }

    private async Task<string?> TryGetHtmlAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await source.GetHtmlAsync(path, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task RedirectAsync(string from, string to, CancellationToken cancellationToken)
    {
        var path = NormalizePath(from);
        if (path.Length <= 1 || path == NormalizePath(to))
        {
            return;
        }

        var existing = await db.WebsiteRedirects.FindAsync([path], cancellationToken);
        if (existing is null)
        {
            db.WebsiteRedirects.Add(new WebsiteRedirect { FromPath = path, ToPath = to, CreatedAt = clock.UtcNow.UtcDateTime });
        }
        else
        {
            existing.ToPath = to;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Pad in kleine letters, zonder slash aan het eind; zo worden oude adressen opgezocht.</summary>
    public static string NormalizePath(string pathOrUrl)
    {
        var path = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : pathOrUrl.Split('?', '#')[0];
        path = Uri.UnescapeDataString(path).ToLowerInvariant().TrimEnd('/');
        return path.Length == 0 ? "/" : path.StartsWith('/') ? path : "/" + path;
    }

    private static string PathOf(string link) => NormalizePath(link);

    public static PhotoCategory CategoryOf(string title)
    {
        var t = title.ToLowerInvariant();
        return t switch
        {
            _ when t.Contains("jeugdpronk", StringComparison.Ordinal) || t.Contains("kinder", StringComparison.Ordinal) || t.Contains("jeugd", StringComparison.Ordinal) => PhotoCategory.Youth,
            _ when t.Contains("pronkzitting", StringComparison.Ordinal) => PhotoCategory.Pronkzitting,
            _ when t.Contains("optoch", StringComparison.Ordinal) => PhotoCategory.Parade,
            _ when t.Contains("dansgarde", StringComparison.Ordinal) => PhotoCategory.Dansgarde,
            _ when t.Contains("carnaval", StringComparison.Ordinal) || t.Contains("prins", StringComparison.Ordinal) || t.Contains("sleutel", StringComparison.Ordinal)
                || t.Contains("prijsuitreiking", StringComparison.Ordinal) => PhotoCategory.Carnival,
            _ => PhotoCategory.Other,
        };
    }

    private static string? PlainIntro(string markdown)
    {
        var text = markdown.Replace("*", "", StringComparison.Ordinal).Replace("#", "", StringComparison.Ordinal).Trim();
        return text.Length == 0 ? null : text.Length <= 300 ? text : text[..text.LastIndexOf(' ', 300)] + "…";
    }

    private static string Decode(string? html) => HtmlToMarkdown.PlainText(html);

    private static string? Truncate(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    private static T Read<T>(WebsiteImportItem item) => JsonSerializer.Deserialize<T>(item.Payload!, Json)!;

    public sealed record ImportMessage(string Step);

    private sealed record KaderPayload(string Slug, WpPerson Person);

    private sealed record GalleryPhotoPayload(Guid AlbumId, string Url);
}

/// <summary>Voert de import uit in de worker: eerst plannen, daarna porties verwerken (fase 21e).</summary>
public sealed class WebsiteImportHandler(WebsiteImporter importer) : IOutboxMessageHandler
{
    public string Type => WebsiteImporter.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var step = JsonSerializer.Deserialize<WebsiteImporter.ImportMessage>(message.Payload, JsonSerializerOptions.Web)?.Step;
        if (step == "plan")
        {
            await importer.PlanAsync(cancellationToken);
        }

        await importer.ProcessBatchAsync(WebsiteImporter.BatchBudget, cancellationToken);
    }
}
