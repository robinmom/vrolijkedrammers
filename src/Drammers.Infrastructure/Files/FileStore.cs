using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Drammers.SharedKernel.Time;

namespace Drammers.Infrastructure.Files;

/// <summary>Blob-containers (docs/08 §3). Alle containers zijn privé; lezen gaat via kortlevende SAS-links.</summary>
public static class FileContainers
{
    public const string Quarantine = "quarantine";
    public const string PhotosOriginal = "photos-original";
    public const string PhotosDerived = "photos-derived";
    public const string Content = "content";

    public static readonly string[] All = [Quarantine, PhotosOriginal, PhotosDerived, Content];
}

/// <summary>Opslag van bestanden; paden worden altijd door de server bepaald, nooit door de uploader.</summary>
public interface IFileStore
{
    Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string container, string path, CancellationToken cancellationToken);

    Task CopyAsync(string fromContainer, string fromPath, string toContainer, string toPath, CancellationToken cancellationToken);

    Task DeleteAsync(string container, string path, CancellationToken cancellationToken);

    /// <summary>Alleen-lezen link voor één blob, maximaal <see cref="BlobFileStore.MaxSasLifetime"/> geldig.</summary>
    Task<Uri> GetReadUriAsync(string container, string path, CancellationToken cancellationToken);
}

internal sealed class BlobFileStore(BlobServiceClient blobs, IClock clock) : IFileStore
{
    /// <summary>ADR-008: SAS ≤ 15 minuten, alleen lezen, per blob.</summary>
    public static readonly TimeSpan MaxSasLifetime = TimeSpan.FromMinutes(15);

    private UserDelegationKey? _delegationKey;
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    public async Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(path);
        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string container, string path, CancellationToken cancellationToken) =>
        await blobs.GetBlobContainerClient(container).GetBlobClient(path).OpenReadAsync(cancellationToken: cancellationToken);

    public async Task CopyAsync(string fromContainer, string fromPath, string toContainer, string toPath, CancellationToken cancellationToken)
    {
        var source = blobs.GetBlobContainerClient(fromContainer).GetBlobClient(fromPath);
        var properties = await source.GetPropertiesAsync(cancellationToken: cancellationToken);
        await using var stream = await source.OpenReadAsync(cancellationToken: cancellationToken);
        await UploadAsync(toContainer, toPath, stream, properties.Value.ContentType, cancellationToken);
    }

    public Task DeleteAsync(string container, string path, CancellationToken cancellationToken) =>
        blobs.GetBlobContainerClient(container).GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task<Uri> GetReadUriAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(container).GetBlobClient(path);
        var now = clock.UtcNow;
        var builder = new BlobSasBuilder(BlobSasPermissions.Read, now.Add(MaxSasLifetime))
        {
            BlobContainerName = container,
            BlobName = path,
            Resource = "b",
            StartsOn = now.AddMinutes(-5),
        };

        // Met een account-key (Azurite, tests) een service-SAS; in Azure een user-delegation-SAS via de managed identity.
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(builder);
        }

        var key = await GetDelegationKeyAsync(now, cancellationToken);
        var sas = builder.ToSasQueryParameters(key, blobs.AccountName);
        return new UriBuilder(blob.Uri) { Query = sas.ToString() }.Uri;
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (_delegationKey is null || _delegationKey.SignedExpiresOn < now.Add(MaxSasLifetime).AddMinutes(5))
            {
                _delegationKey = (await blobs.GetUserDelegationKeyAsync(now.AddMinutes(-5), now.AddHours(2), cancellationToken)).Value;
            }

            return _delegationKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}

/// <summary>Zonder Blob Storage (lokaal zonder Azurite): lezen geeft geen links, schrijven faalt duidelijk.</summary>
internal sealed class UnconfiguredFileStore : IFileStore
{
    private static InvalidOperationException NotConfigured() =>
        new("Blob Storage is niet geconfigureerd (Azure__BlobEndpoint of ConnectionStrings__Blob).");

    public Task UploadAsync(string container, string path, Stream content, string contentType, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<Stream> OpenReadAsync(string container, string path, CancellationToken cancellationToken) => throw NotConfigured();

    public Task CopyAsync(string fromContainer, string fromPath, string toContainer, string toPath, CancellationToken cancellationToken) => throw NotConfigured();

    public Task DeleteAsync(string container, string path, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Uri> GetReadUriAsync(string container, string path, CancellationToken cancellationToken) => throw NotConfigured();
}
