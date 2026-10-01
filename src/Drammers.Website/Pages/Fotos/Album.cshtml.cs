using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages.Fotos;

public sealed class AlbumModel(WebsiteReader reader) : SitePage
{
    public AlbumCard Album { get; private set; } = null!;

    public IReadOnlyList<PhotoView> Photos { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        ActiveMenu = "fotos";
        var result = await reader.AlbumAsync(id, cancellationToken);
        if (result is not { } found)
        {
            return await NotFoundOrRedirectAsync();
        }

        (Album, Photos) = (found.Album, found.Photos);
        (PageTitle, MetaDescription, ShareImage) = (Album.Title, Album.Description, Album.CoverUrl);
        return Page();
    }
}
