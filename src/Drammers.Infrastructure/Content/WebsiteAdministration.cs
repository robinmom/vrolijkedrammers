using System.Text.Json;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Website;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Content;

public sealed record WebsiteSettingsInput(
    string? HeroEyebrow, string HeroTitle, string? HeroSubtitle, string? HeroPrimaryLabel, WebsiteLink? HeroPrimaryLink,
    string? HeroSecondaryLabel, WebsiteLink? HeroSecondaryLink, string? HeroImage, string? FacebookPageUrl, string? InstagramUrl,
    bool ShowYouthPrinces);

public sealed record WebsitePageInput(
    string? Slug, string Title, string? Intro, string Body, string? Image, bool IsPublished, int SortOrder, WebsiteMenu Menu = WebsiteMenu.None,
    Guid? PhotoAlbumId = null);

public sealed record CommitteeInput(string Name, int SortOrder);

public sealed record CommitteeMemberInput(int CommitteeId, Guid? MemberId, string Name, string? Function, string? Photo);

public sealed record PrinceInput(PrinceKind Kind, int Year, string PrinceName, string? Name, string? Motto, string? Photo);

public sealed record AwardInput(AwardType Type, int Year, string Recipient, string? Body, string? Photo, bool IsPublished);

/// <summary>
/// Websitebeheer (fase 21a): de hero en instellingen, vaste pagina's, het kader, de prinsen en de onderscheidingen.
/// Afbeeldingen komen vooraf binnen via <see cref="StoreImageAsync"/>; elke wijziging wordt geaudit.
/// </summary>
public sealed class WebsiteAdministration(DrammersDbContext db, ContentFiles contentFiles, IFileStore files, IAuditLogger audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Uploadt een afbeelding (typecontrole, virusscan, herschalen, zonder metadata) en geeft het pad terug.</summary>
    public async Task<StoredFile> StoreImageAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.Open();
        return await contentFiles.StoreImageAsync(stream, file.Length, UploadedImages.Folder, cancellationToken);
    }

    // ----- Instellingen en hero ------------------------------------------------------------------------------------

    public async Task<WebsiteSettings> GetSettingsAsync(CancellationToken cancellationToken) =>
        await db.WebsiteSettings.AsNoTracking().SingleAsync(cancellationToken);

    public async Task UpdateSettingsAsync(WebsiteSettingsInput input, CancellationToken cancellationToken)
    {
        ValidateLink(input.HeroPrimaryLabel, input.HeroPrimaryLink);
        ValidateLink(input.HeroSecondaryLabel, input.HeroSecondaryLink);
        var s = await db.WebsiteSettings.SingleAsync(cancellationToken);
        (s.HeroEyebrow, s.HeroTitle, s.HeroSubtitle) = (Clean(input.HeroEyebrow), input.HeroTitle.Trim(), Clean(input.HeroSubtitle));
        (s.HeroPrimaryLabel, s.HeroPrimaryLink) = Link(input.HeroPrimaryLabel, input.HeroPrimaryLink);
        (s.HeroSecondaryLabel, s.HeroSecondaryLink) = Link(input.HeroSecondaryLabel, input.HeroSecondaryLink);
        (s.FacebookPageUrl, s.InstagramUrl) = (ValidUrl(input.FacebookPageUrl, "Facebook"), ValidUrl(input.InstagramUrl, "Instagram"));
        s.ShowYouthPrinces = input.ShowYouthPrinces;
        var (image, obsolete) = UploadedImages.Resolve(s.HeroImageBlobPath, input.HeroImage);
        s.HeroImageBlobPath = image;
        await SaveAsync("website.settings-updated", "WebsiteSettings", WebsiteSettings.SingletonId.ToString(), input with { HeroImage = null }, cancellationToken);
        await DeleteAsync(obsolete, cancellationToken);
    }

    // ----- Pagina's ------------------------------------------------------------------------------------------------

    public async Task<Guid> CreatePageAsync(WebsitePageInput input, CancellationToken cancellationToken)
    {
        var page = new WebsitePage { Id = IdGenerator.NewId(), Slug = "", Title = input.Title, Body = input.Body };
        db.WebsitePages.Add(page);
        await ApplyAsync(page, input, cancellationToken);
        await SaveAsync("website.page-created", "WebsitePage", page.Id.ToString(), input with { Body = "…" }, cancellationToken);
        return page.Id;
    }

    public async Task UpdatePageAsync(Guid id, WebsitePageInput input, CancellationToken cancellationToken)
    {
        var page = await db.WebsitePages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound();
        var obsolete = await ApplyAsync(page, input, cancellationToken);
        await SaveAsync("website.page-updated", "WebsitePage", id.ToString(), input with { Body = "…" }, cancellationToken);
        await DeleteAsync(obsolete, cancellationToken);
    }

    public async Task DeletePageAsync(Guid id, CancellationToken cancellationToken)
    {
        var page = await db.WebsitePages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound();
        db.WebsitePages.Remove(page);
        await SaveAsync("website.page-deleted", "WebsitePage", id.ToString(), new { page.Title, page.Slug }, cancellationToken);
        await DeleteAsync(page.ImageBlobPath, cancellationToken);
    }

    // ----- Kader ---------------------------------------------------------------------------------------------------

    public async Task<int> CreateCommitteeAsync(CommitteeInput input, CancellationToken cancellationToken)
    {
        var slug = await Slugs.UniqueAsync(Slugs.From(input.Name), s => db.Committees.AnyAsync(c => c.Slug == s, cancellationToken));
        var committee = new Committee { Name = input.Name.Trim(), Slug = slug, SortOrder = input.SortOrder };
        db.Committees.Add(committee);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("website.committee-created", "Committee", committee.Id.ToString(), null, Serialize(input)), cancellationToken);
        return committee.Id;
    }

    public async Task UpdateCommitteeAsync(int id, CommitteeInput input, CancellationToken cancellationToken)
    {
        var committee = await db.Committees.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw NotFound();
        (committee.Name, committee.SortOrder) = (input.Name.Trim(), input.SortOrder);
        await SaveAsync("website.committee-updated", "Committee", id.ToString(), input, cancellationToken);
    }

    public async Task DeleteCommitteeAsync(int id, CancellationToken cancellationToken)
    {
        var committee = await db.Committees.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw NotFound();
        if (await db.CommitteeMembers.AnyAsync(m => m.CommitteeId == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Haal eerst alle kaderleden uit deze commissie.");
        }

        db.Committees.Remove(committee);
        await SaveAsync("website.committee-deleted", "Committee", id.ToString(), new { committee.Name }, cancellationToken);
    }

    public async Task<Guid> AddCommitteeMemberAsync(CommitteeMemberInput input, CancellationToken cancellationToken)
    {
        await EnsureCommitteeAsync(input.CommitteeId, cancellationToken);
        var sortOrder = await db.CommitteeMembers.Where(m => m.CommitteeId == input.CommitteeId).MaxAsync(m => (int?)m.SortOrder, cancellationToken) ?? 0;
        var member = new CommitteeMember { Id = IdGenerator.NewId(), CommitteeId = input.CommitteeId, Name = "", SortOrder = sortOrder + 10 };
        db.CommitteeMembers.Add(member);
        await ApplyAsync(member, input, cancellationToken);
        await SaveAsync("website.kader-added", "CommitteeMember", member.Id.ToString(), input with { Photo = null }, cancellationToken);
        return member.Id;
    }

    public async Task UpdateCommitteeMemberAsync(Guid id, CommitteeMemberInput input, CancellationToken cancellationToken)
    {
        var member = await db.CommitteeMembers.SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw NotFound();
        await EnsureCommitteeAsync(input.CommitteeId, cancellationToken);
        var obsolete = await ApplyAsync(member, input, cancellationToken);
        await SaveAsync("website.kader-updated", "CommitteeMember", id.ToString(), input with { Photo = null }, cancellationToken);
        await DeleteAsync(obsolete, cancellationToken);
    }

    public async Task RemoveCommitteeMemberAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await db.CommitteeMembers.SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw NotFound();
        db.CommitteeMembers.Remove(member);
        await SaveAsync("website.kader-removed", "CommitteeMember", id.ToString(), new { member.Name, member.CommitteeId }, cancellationToken);
        await DeleteAsync(member.PhotoBlobPath, cancellationToken);
    }

    /// <summary>Nieuwe volgorde binnen een commissie; <paramref name="ids"/> bevat precies alle kaderleden van die commissie.</summary>
    public async Task ReorderCommitteeAsync(int committeeId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var members = await db.CommitteeMembers.Where(m => m.CommitteeId == committeeId).ToListAsync(cancellationToken);
        if (members.Count != ids.Count || ids.Distinct().Count() != ids.Count || members.Any(m => !ids.Contains(m.Id)))
        {
            throw new DomainException(ErrorCodes.Validation, "De volgorde moet precies alle kaderleden van deze commissie bevatten.");
        }

        foreach (var m in members)
        {
            m.SortOrder = (ids.ToList().IndexOf(m.Id) + 1) * 10;
        }

        await SaveAsync("website.kader-reordered", "Committee", committeeId.ToString(), null, cancellationToken);
    }

    // ----- Prinsen -------------------------------------------------------------------------------------------------

    public async Task<Guid> CreatePrinceAsync(PrinceInput input, CancellationToken cancellationToken)
    {
        var prince = new Prince { Id = IdGenerator.NewId(), PrinceName = "" };
        db.Princes.Add(prince);
        Apply(prince, input);
        await SaveAsync("website.prince-created", "Prince", prince.Id.ToString(), input with { Photo = null }, cancellationToken);
        return prince.Id;
    }

    public async Task UpdatePrinceAsync(Guid id, PrinceInput input, CancellationToken cancellationToken)
    {
        var prince = await db.Princes.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound();
        var obsolete = Apply(prince, input);
        await SaveAsync("website.prince-updated", "Prince", id.ToString(), input with { Photo = null }, cancellationToken);
        await DeleteAsync(obsolete, cancellationToken);
    }

    public async Task DeletePrinceAsync(Guid id, CancellationToken cancellationToken)
    {
        var prince = await db.Princes.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw NotFound();
        db.Princes.Remove(prince);
        await SaveAsync("website.prince-deleted", "Prince", id.ToString(), new { prince.PrinceName, prince.Year }, cancellationToken);
        await DeleteAsync(prince.PhotoBlobPath, cancellationToken);
    }

    // ----- Onderscheidingen ----------------------------------------------------------------------------------------

    public async Task<Guid> CreateAwardAsync(AwardInput input, CancellationToken cancellationToken)
    {
        var award = new Award { Id = IdGenerator.NewId(), Recipient = "", Slug = "" };
        db.Awards.Add(award);
        await ApplyAsync(award, input, cancellationToken);
        await SaveAsync("website.award-created", "Award", award.Id.ToString(), input with { Body = null, Photo = null }, cancellationToken);
        return award.Id;
    }

    public async Task UpdateAwardAsync(Guid id, AwardInput input, CancellationToken cancellationToken)
    {
        var award = await db.Awards.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        var obsolete = await ApplyAsync(award, input, cancellationToken);
        await SaveAsync("website.award-updated", "Award", id.ToString(), input with { Body = null, Photo = null }, cancellationToken);
        await DeleteAsync(obsolete, cancellationToken);
    }

    public async Task DeleteAwardAsync(Guid id, CancellationToken cancellationToken)
    {
        var award = await db.Awards.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        db.Awards.Remove(award);
        await SaveAsync("website.award-deleted", "Award", id.ToString(), new { award.Recipient, award.Year, award.Type }, cancellationToken);
        await DeleteAsync(award.PhotoBlobPath, cancellationToken);
    }

    // ----- Hulpfuncties --------------------------------------------------------------------------------------------

    private async Task<string?> ApplyAsync(WebsitePage page, WebsitePageInput input, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(input.Slug) ? Slugs.From(input.Title) : Slugs.Validate(input.Slug.Trim());
        if (slug != page.Slug)
        {
            if (await db.WebsitePages.AnyAsync(p => p.Slug == slug && p.Id != page.Id, cancellationToken))
            {
                throw new DomainException(ErrorCodes.Validation, $"Het webadres '{slug}' is al in gebruik.");
            }

            page.Slug = slug;
        }

        if (input.PhotoAlbumId is { } albumId && !await db.PhotoAlbums.AnyAsync(a => a.Id == albumId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Dit fotoalbum bestaat niet (meer).");
        }

        (page.Title, page.Intro, page.Body, page.IsPublished, page.SortOrder, page.Menu, page.PhotoAlbumId) =
            (input.Title.Trim(), Clean(input.Intro), input.Body, input.IsPublished, input.SortOrder, input.Menu, input.PhotoAlbumId);
        var (image, obsolete) = UploadedImages.Resolve(page.ImageBlobPath, input.Image);
        page.ImageBlobPath = image;
        return obsolete;
    }

    private async Task<string?> ApplyAsync(CommitteeMember member, CommitteeMemberInput input, CancellationToken cancellationToken)
    {
        if (input.MemberId is { } memberId && !await db.Members.AnyAsync(m => m.Id == memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Dit lid bestaat niet (meer) in de ledenlijst.");
        }

        (member.CommitteeId, member.MemberId, member.Name, member.Function) = (input.CommitteeId, input.MemberId, input.Name.Trim(), Clean(input.Function));
        var (photo, obsolete) = UploadedImages.Resolve(member.PhotoBlobPath, input.Photo);
        member.PhotoBlobPath = photo;
        return obsolete;
    }

    private static string? Apply(Prince prince, PrinceInput input)
    {
        (prince.Kind, prince.Year, prince.PrinceName, prince.Name, prince.Motto) =
            (input.Kind, ValidYear(input.Year), input.PrinceName.Trim(), Clean(input.Name), Clean(input.Motto));
        var (photo, obsolete) = UploadedImages.Resolve(prince.PhotoBlobPath, input.Photo);
        prince.PhotoBlobPath = photo;
        return obsolete;
    }

    private async Task<string?> ApplyAsync(Award award, AwardInput input, CancellationToken cancellationToken)
    {
        (award.Type, award.Year, award.Recipient, award.Body, award.IsPublished) =
            (input.Type, ValidYear(input.Year), input.Recipient.Trim(), Clean(input.Body), input.IsPublished);
        var slug = Slugs.From($"{award.Recipient} {award.Year}");
        if (slug != award.Slug)
        {
            award.Slug = await Slugs.UniqueAsync(slug, s => db.Awards.AnyAsync(a => a.Slug == s && a.Id != award.Id, cancellationToken));
        }

        var (photo, obsolete) = UploadedImages.Resolve(award.PhotoBlobPath, input.Photo);
        award.PhotoBlobPath = photo;
        return obsolete;
    }

    private async Task EnsureCommitteeAsync(int id, CancellationToken cancellationToken)
    {
        if (!await db.Committees.AnyAsync(c => c.Id == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een bestaande commissie.");
        }
    }

    private static int ValidYear(int year) =>
        year is >= 1958 and <= 2100 ? year : throw new DomainException(ErrorCodes.Validation, "Kies een jaar vanaf 1958.");

    private static void ValidateLink(string? label, WebsiteLink? link)
    {
        if (!string.IsNullOrWhiteSpace(label) && link is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies waar de knop naartoe gaat.");
        }
    }

    private static (string?, WebsiteLink?) Link(string? label, WebsiteLink? link) =>
        string.IsNullOrWhiteSpace(label) ? (null, null) : (label.Trim(), link);

    private static string? ValidUrl(string? url, string name)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : throw new DomainException(ErrorCodes.Validation, $"Vul een volledig adres in voor {name}, beginnend met https://.");
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Serialize(object? values) => values is null ? null : JsonSerializer.Serialize(values, Json);

    private async Task SaveAsync(string action, string entityType, string id, object? values, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, entityType, id, null, Serialize(values)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task DeleteAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is not null)
        {
            await files.DeleteAsync(FileContainers.Content, path, cancellationToken);
        }
    }

    private static DomainException NotFound() => new(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
}
