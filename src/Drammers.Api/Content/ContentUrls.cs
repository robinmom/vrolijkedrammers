using Drammers.Infrastructure.Files;

namespace Drammers.Api.Content;

/// <summary>Kortlevende leeslinks (SAS ≤ 15 min) voor content-bestanden; <c>null</c> als er geen bestand is.</summary>
public sealed class ContentUrls(IFileStore files)
{
    public async Task<string?> ForAsync(string container, string? path, CancellationToken cancellationToken) =>
        path is null ? null : (await files.GetReadUriAsync(container, path, cancellationToken)).ToString();
}
