namespace Drammers.Infrastructure;

/// <summary>Adressen van de Azure-resources van de omgeving (app settings <c>Azure__*</c>, gezet door Bicep).</summary>
public sealed class AzureOptions
{
    public const string SectionName = "Azure";

    public Uri? KeyVaultUri { get; set; }

    public Uri? BlobEndpoint { get; set; }

    /// <summary>Key Vault-sleutel die de Data Protection-sleutelring beschermt (push-tokens, docs/06).</summary>
    public Uri? DataProtectionKeyUri { get; set; }
}
