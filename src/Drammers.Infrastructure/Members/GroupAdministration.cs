using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Groups;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record GroupInput(string Name, string? Description, GroupType Type, int? CarnivalYearId, bool Active);

public sealed record GroupMemberInput(GroupFunction Function, DateOnly? ValidFrom, DateOnly? ValidTo);

/// <summary>Groepen als doelgroep voor content (fase 8): aanmaken, wijzigen, leden toevoegen en verwijderen; alles geaudit.</summary>
public sealed class GroupAdministration(DrammersDbContext db, IAuditLogger audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(GroupInput input, CancellationToken cancellationToken)
    {
        var group = new Group { Id = IdGenerator.NewId(), Name = string.Empty };
        await ApplyAsync(group, input, cancellationToken);
        db.Groups.Add(group);
        await SaveAsync("group.created", group.Id, input, cancellationToken);
        return group.Id;
    }

    public async Task UpdateAsync(Guid id, GroupInput input, CancellationToken cancellationToken)
    {
        var group = await FindAsync(id, cancellationToken);
        await ApplyAsync(group, input, cancellationToken);
        await SaveAsync("group.updated", id, input, cancellationToken);
    }

    /// <summary>Verwijdert de groep en haar lidmaatschappen; content met deze groep als doelgroep is daarna voor niemand via die groep zichtbaar.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await FindAsync(id, cancellationToken);
        db.Groups.Remove(group);
        await SaveAsync("group.deleted", id, new { group.Name }, cancellationToken);
    }

    public async Task SetMemberAsync(Guid groupId, Guid memberId, GroupMemberInput input, CancellationToken cancellationToken)
    {
        await FindAsync(groupId, cancellationToken);
        if (!await db.Members.AnyAsync(m => m.Id == memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        }

        if (input.ValidFrom is { } from && input.ValidTo is { } to && to < from)
        {
            throw new DomainException(ErrorCodes.Validation, "De einddatum ligt vóór de begindatum.");
        }

        var membership = await db.GroupMemberships.SingleOrDefaultAsync(m => m.GroupId == groupId && m.MemberId == memberId, cancellationToken);
        if (membership is null)
        {
            membership = new GroupMembership { GroupId = groupId, MemberId = memberId };
            db.GroupMemberships.Add(membership);
        }

        (membership.Function, membership.ValidFrom, membership.ValidTo) = (input.Function, input.ValidFrom, input.ValidTo);
        await SaveAsync("group.member-set", groupId, new { memberId, function = input.Function.ToString(), input.ValidFrom, input.ValidTo }, cancellationToken);
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var membership = await db.GroupMemberships.SingleOrDefaultAsync(m => m.GroupId == groupId && m.MemberId == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Dit lid zit niet in de groep.", DomainErrorKind.NotFound);
        db.GroupMemberships.Remove(membership);
        await SaveAsync("group.member-removed", groupId, new { memberId }, cancellationToken);
    }

    private async Task ApplyAsync(Group group, GroupInput input, CancellationToken cancellationToken)
    {
        var name = input.Name.Trim();
        if (name.Length is < 2 or > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef de groep een naam van 2 tot 100 tekens.");
        }

        if (await db.Groups.AnyAsync(g => g.Name == name && g.Id != group.Id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.GroupNameTaken, $"Er bestaat al een groep '{name}'.", DomainErrorKind.Conflict);
        }

        if (input.CarnivalYearId is { } yearId && !await db.CarnivalYears.AnyAsync(y => y.Id == yearId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CarnivalYearNotFound, "Carnavalsjaar niet gevonden.");
        }

        (group.Name, group.Description, group.Type, group.CarnivalYearId, group.Active) =
            (name, string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), input.Type, input.CarnivalYearId, input.Active);
    }

    private async Task<Group> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.GroupNotFound, "Groep niet gevonden.", DomainErrorKind.NotFound);

    private async Task SaveAsync(string action, Guid id, object values, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, "Group", id.ToString(), null, JsonSerializer.Serialize(values, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
