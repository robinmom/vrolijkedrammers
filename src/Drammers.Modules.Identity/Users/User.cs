using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Identity.Users;

public enum AccountStatus
{
    Active,
    Disabled,
    Blocked,
    Deleted,
}

/// <summary>
/// Account van een lid of ouder/verzorger (docs/04 §3). Bestaat alleen via de provisioning (ADR-014): een token van een
/// onbekende <see cref="ExternalObjectId"/> krijgt 403 (geen just-in-time aanmaken).
/// </summary>
public sealed class User : IAuditable
{
    public Guid Id { get; set; }

    /// <summary><c>oid</c> uit Entra External ID.</summary>
    public required string ExternalObjectId { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Gekoppeld lid; de FK naar <c>membership.Member</c> volgt in fase 8.</summary>
    public Guid? MemberId { get; set; }

    public AccountStatus AccountStatus { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Opgehoogd bij elke rolwijziging; maakt de permission-cache ongeldig.</summary>
    public int PermissionsVersion { get; set; }

    public List<UserRole> Roles { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Rol van een gebruiker, optioneel tijdelijk geldig (bijv. scanner tijdens carnaval).</summary>
public sealed class UserRole
{
    public Guid UserId { get; set; }

    public int RoleId { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public Guid? AssignedBy { get; set; }

    public DateTime AssignedAt { get; set; }

    public bool IsValidOn(DateOnly date) =>
        (ValidFrom is null || ValidFrom <= date) && (ValidTo is null || ValidTo >= date);
}

public enum LoginResult
{
    Success,
    Failed,
    Locked,
}

/// <summary>Aanmeldhistorie (docs/04 §3); IP en e-mail alleen gehasht. Bewaartermijn 12 maanden.</summary>
public sealed class LoginHistory
{
    public long Id { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Hash van de onbekende <c>oid</c> bij een mislukte aanmelding zonder gebruiker.</summary>
    public string? SubjectHash { get; set; }

    public DateTime OccurredAt { get; set; }

    public LoginResult Result { get; set; }

    public string? Reason { get; set; }

    public string? IpHash { get; set; }

    public string? UserAgent { get; set; }
}
