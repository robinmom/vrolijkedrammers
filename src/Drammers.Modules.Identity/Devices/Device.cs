namespace Drammers.Modules.Identity.Devices;

public enum DevicePlatform
{
    Ios,
    Android,
}

public enum DeviceStatus
{
    Active,
    Revoked,
}

/// <summary>
/// Een installatie van de app waarop een gebruiker is ingelogd (docs/04 §3, fase 9). De app stuurt de
/// <see cref="InstallationId"/> mee in <c>X-Device-Id</c>; een afgemeld apparaat krijgt daarna 401 en logt uit.
/// Sleutel, attestatie en scannervertrouwen worden in fase 13/14 gebruikt (ADR-005, OQ-68).
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Willekeurige id die de app bij de eerste aanmelding maakt en in de Keychain/Keystore bewaart.</summary>
    public required string InstallationId { get; set; }

    public DevicePlatform Platform { get; set; }

    public string? Model { get; set; }

    /// <summary>Door de gebruiker te wijzigen, standaard het model.</summary>
    public required string Name { get; set; }

    public string? AppVersion { get; set; }

    public DeviceStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    /// <summary>Publieke sleutel uit de Secure Enclave/StrongBox (fase 13); nog niet gebruikt.</summary>
    public string? PublicKey { get; set; }

    public string? AttestationStatus { get; set; }

    public bool TrustedScanner { get; set; }
}
