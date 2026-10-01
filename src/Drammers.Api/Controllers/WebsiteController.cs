using Drammers.Api.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Website;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Openbare website-inhoud (fase 21a): de hero, ook voor het beginscherm van de app.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/website")]
public sealed class WebsiteController(DrammersDbContext db, ContentUrls urls) : ControllerBase
{
    [HttpGet("hero")]
    [PublicCache]
    [ProducesResponseType<WebsiteHeroResponse>(StatusCodes.Status200OK)]
    public async Task<WebsiteHeroResponse> Hero(CancellationToken cancellationToken)
    {
        var s = await db.WebsiteSettings.AsNoTracking().SingleAsync(cancellationToken);
        return new WebsiteHeroResponse(s.HeroEyebrow, s.HeroTitle, s.HeroSubtitle,
            await urls.ForAsync(FileContainers.Content, s.HeroImageBlobPath, cancellationToken),
            s.HeroPrimaryLabel is null ? null : new WebsiteHeroButton(s.HeroPrimaryLabel, s.HeroPrimaryLink!.Value),
            s.HeroSecondaryLabel is null ? null : new WebsiteHeroButton(s.HeroSecondaryLabel, s.HeroSecondaryLink!.Value));
    }
}

public sealed record WebsiteHeroButton(string Label, WebsiteLink Link);

public sealed record WebsiteHeroResponse(
    string? Eyebrow, string Title, string? Subtitle, string? ImageUrl, WebsiteHeroButton? Primary, WebsiteHeroButton? Secondary);
