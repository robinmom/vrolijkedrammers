namespace Drammers.Modules.Identity.Roles;

/// <summary>Configureerbare bundel permissions (docs/07 §3).</summary>
public sealed class Role
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Systeemrollen (Lid, Ouder/verzorger, Bestuur, Beheerder IT) zijn niet te verwijderen.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Mag door koppelingen (e-Boekhouden-sync, provisioning) worden toegekend.</summary>
    public bool IsAssignableBySync { get; set; }

    public int SortOrder { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}

public sealed class Permission
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Description { get; set; }

    public required string Category { get; set; }
}

public sealed class RolePermission
{
    public int RoleId { get; set; }

    public int PermissionId { get; set; }
}
