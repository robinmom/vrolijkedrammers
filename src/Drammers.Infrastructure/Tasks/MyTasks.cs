using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Content.Tasks;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Tasks;

/// <summary>Een categorie om een nieuwe taak onder te hangen; alleen de actieve categorieën van de takenlijst carnaval.</summary>
public sealed record MyTaskCategory(Guid Id, string Name);

/// <summary>
/// Eén taak van het lid zelf. <paramref name="People"/> is de leesregel "samen met ..." (de andere leden en de losse
/// namen) of <c>null</c> als het lid er alleen op staat.
/// </summary>
public sealed record MyTask(
    Guid Id, Guid CategoryId, string CategoryName, string Title, string? Description, CarnivalTaskStatus Status, int Progress,
    DateOnly? StartedOn, DateOnly? DueOn, DateOnly? CompletedOn, bool Overdue, string? Note, string? People);

/// <summary><paramref name="Percent"/> is de gemiddelde voortgang; geannuleerde taken tellen nergens in mee.</summary>
public sealed record MyTaskSummary(int Total, int Done, int Overdue, int Percent);

/// <summary>
/// <paramref name="Allowed"/> is <c>false</c> voor een gewoon lid: de app laat de knop dan niet zien en de lijst is leeg.
/// </summary>
public sealed record MyTaskList(
    bool Allowed, int YearId, string YearName, DateOnly? CarnivalStartDate, int DaysUntilCarnival, MyTaskSummary Summary,
    IReadOnlyList<MyTask> Items, IReadOnlyList<MyTaskCategory> Categories);

/// <summary>Een taak die een lid zelf aanmaakt in de app; hij komt op zijn eigen naam te staan.</summary>
public sealed record NewTaskInput(Guid CategoryId, string Title, string? Description, DateOnly? DueOn, CarnivalTaskStatus Status, int Progress);

/// <summary>
/// De takenlijst carnaval in de app (2026-11-12). Een lid ziet <b>alleen zijn eigen taken</b> van het lopende
/// carnavalsjaar, zet er de stand van bij, zet er een opmerking bij en kan er zelf een taak aan toevoegen onder een
/// bestaande categorie. Verwijderen kan niet; dat blijft in het portal (<see cref="CarnivalTasks"/>).
///
/// Wie het mag gebruiken (besluit 2026-11-12): je staat op de <b>kaderlijst</b>, of je hebt het recht
/// <see cref="Permissions.TaskViewOwn"/> via een rol, <b>of</b> het bestuur heeft je op minstens één taak gezet. Dat
/// laatste lekt niets naar gewone leden: zonder taak blijft de knop weg, en met een taak is het juist de bedoeling dat
/// je hem ziet. Dezelfde afscherming als bij de adverteerders, plus die derde ingang.
/// </summary>
public sealed class MyTasks(DrammersDbContext db, IClock clock, IAuditLogger audit)
{
    /// <summary>Alleen de takenlijst van carnaval; die van het Dansgardefestival blijft in het portal.</summary>
    private const TaskList List = TaskList.Carnival;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>Of dit lid de takenlijst in de app mag gebruiken; zie de toelichting bij de klasse.</summary>
    public async Task<bool> MayUseAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (await db.CommitteeMembers.AnyAsync(c => c.MemberId == memberId, cancellationToken))
        {
            return true;
        }

        var permission = DefaultRoles.PermissionId(Permissions.TaskViewOwn);
        var today = Today;
        var viaRole =
            from u in db.Users
            where u.MemberId == memberId && u.AccountStatus == AccountStatus.Active
            join ur in db.UserRoles on u.Id equals ur.UserId
            where (ur.ValidFrom == null || ur.ValidFrom <= today) && (ur.ValidTo == null || ur.ValidTo >= today)
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            where rp.PermissionId == permission
            select u.Id;
        return await viaRole.AnyAsync(cancellationToken)
            || await db.CarnivalTasks.AnyAsync(t => t.Assignees.Any(a => a.MemberId == memberId), cancellationToken);
    }

    /// <summary>De eigen taken van het lopende carnavalsjaar. Mag het lid dit niet, dan komt er een lege lijst terug.</summary>
    public async Task<MyTaskList> MineAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var year = await YearAsync(cancellationToken);
        if (year is null || !await MayUseAsync(memberId, cancellationToken))
        {
            return new MyTaskList(false, year?.Id ?? 0, year?.Name ?? "", year?.CarnivalStartDate, 0, new MyTaskSummary(0, 0, 0, 0), [], []);
        }

        var categories = await db.TaskCategories.AsNoTracking().Where(c => c.List == List).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var categoryIds = categories.Select(c => c.Id).ToList();
        var tasks = await db.CarnivalTasks.AsNoTracking().Include(t => t.Assignees)
            .Where(t => t.CarnivalYearId == year.Id && categoryIds.Contains(t.CategoryId) && t.Assignees.Any(a => a.MemberId == memberId))
            .ToListAsync(cancellationToken);

        // De namen van de mede-uitvoerders; het lid zelf staat er niet bij ("samen met ...").
        var others = tasks.SelectMany(t => t.Assignees.Select(a => a.MemberId)).Where(id => id != memberId).Distinct().ToList();
        var names = await db.Members.AsNoTracking().Where(m => others.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.FullName, cancellationToken);

        var today = Today;
        var byId = categories.ToDictionary(c => c.Id);
        var items = tasks
            .OrderBy(t => byId.TryGetValue(t.CategoryId, out var c) ? c.SortOrder : int.MaxValue)
            .ThenBy(t => byId.TryGetValue(t.CategoryId, out var c) ? c.Name : "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.DueOn ?? DateOnly.MaxValue).ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new MyTask(
                t.Id, t.CategoryId, byId.TryGetValue(t.CategoryId, out var c) ? c.Name : "—", t.Title, t.Description, t.Status, t.Progress,
                t.StartedOn, t.DueOn, t.CompletedOn,
                t.DueOn < today && t.Status is CarnivalTaskStatus.NotStarted or CarnivalTaskStatus.InProgress,
                t.Note, People(t, memberId, names)))
            .ToList();

        var counted = items.Where(t => t.Status != CarnivalTaskStatus.Cancelled).ToList();
        var summary = new MyTaskSummary(
            counted.Count,
            counted.Count(t => t.Status == CarnivalTaskStatus.Done),
            counted.Count(t => t.Overdue),
            counted.Count == 0 ? 0 : (int)Math.Round(counted.Average(t => t.Status == CarnivalTaskStatus.Done ? 100 : t.Status == CarnivalTaskStatus.InProgress ? t.Progress : 0)));

        return new MyTaskList(
            true, year.Id, year.Name, year.CarnivalStartDate, Math.Max(0, year.CarnivalStartDate.DayNumber - today.DayNumber), summary, items,
            [.. categories.Where(c => c.Active).Select(c => new MyTaskCategory(c.Id, c.Name))]);
    }

    /// <summary>De stand van een eigen taak bijwerken: status, voortgang en de opmerking.</summary>
    public async Task UpdateMineAsync(
        Guid memberId, Guid taskId, CarnivalTaskStatus status, int progress, string? note, CancellationToken cancellationToken)
    {
        if (progress is < 0 or > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "De voortgang moet tussen 0 en 100% liggen.");
        }

        if (note?.Trim().Length > 500)
        {
            throw new DomainException(ErrorCodes.Validation, "Opmerking: maximaal 500 tekens.");
        }

        var year = await YearAsync(cancellationToken) ?? throw NotFound();
        var task = await db.CarnivalTasks.SingleOrDefaultAsync(
            t => t.Id == taskId && t.CarnivalYearId == year.Id && t.Assignees.Any(a => a.MemberId == memberId)
                && db.TaskCategories.Any(c => c.Id == t.CategoryId && c.List == List), cancellationToken)
            ?? throw NotFound();

        var before = $"{task.Status} {task.Progress}%";
        var today = Today;
        task.Status = status;
        task.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        switch (status)
        {
            case CarnivalTaskStatus.NotStarted:
                (task.Progress, task.StartedOn, task.CompletedOn) = (0, null, null);
                break;
            case CarnivalTaskStatus.InProgress:
                task.Progress = progress;
                task.StartedOn ??= today;
                task.CompletedOn = null;
                break;
            case CarnivalTaskStatus.Done:
                task.Progress = 100;
                task.StartedOn ??= today;
                task.CompletedOn ??= today;
                break;
            case CarnivalTaskStatus.Cancelled:
                task.CompletedOn = null;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("task.updated-in-app", "CarnivalTask", task.Id.ToString(), before, $"{task.Status} {task.Progress}%"), cancellationToken);
    }

    /// <summary>Een taak die het lid zelf toevoegt; hij komt op zijn eigen naam in het lopende carnavalsjaar.</summary>
    public async Task<Guid> AddAsync(Guid memberId, NewTaskInput input, CancellationToken cancellationToken)
    {
        if (!await MayUseAsync(memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Alleen het kader kan taken bijhouden in de app.", DomainErrorKind.Forbidden);
        }

        var title = input.Title.Trim();
        if (title.Length is 0 or > 200)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef de taak een titel (maximaal 200 tekens).");
        }

        if (input.Description?.Trim().Length > 2000)
        {
            throw new DomainException(ErrorCodes.Validation, "Omschrijving: maximaal 2000 tekens.");
        }

        if (input.Progress is < 0 or > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "De voortgang moet tussen 0 en 100% liggen.");
        }

        var year = await YearAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Er is nog geen carnavalsjaar.", DomainErrorKind.NotFound);
        if (!await db.TaskCategories.AnyAsync(c => c.Id == input.CategoryId && c.List == List && c.Active, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een categorie uit de lijst.");
        }

        var today = Today;
        var task = new CarnivalTask
        {
            Id = IdGenerator.NewId(),
            CarnivalYearId = year.Id,
            CategoryId = input.CategoryId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            DueOn = input.DueOn,
            Status = input.Status,
            Progress = input.Status switch
            {
                CarnivalTaskStatus.Done => 100,
                CarnivalTaskStatus.InProgress => input.Progress,
                _ => 0,
            },
            StartedOn = input.Status is CarnivalTaskStatus.InProgress or CarnivalTaskStatus.Done ? today : null,
            CompletedOn = input.Status == CarnivalTaskStatus.Done ? today : null,
            Assignees = [new CarnivalTaskAssignee { MemberId = memberId }],
        };
        db.CarnivalTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("task.created-in-app", "CarnivalTask", task.Id.ToString(), null,
                JsonSerializer.Serialize(new { task.Title, year = year.Name }, JsonSerializerOptions.Web)),
            cancellationToken);
        return task.Id;
    }

    /// <summary>Het actieve carnavalsjaar, of anders het laatste; <c>null</c> als er nog geen jaar is.</summary>
    private async Task<Modules.Content.CarnivalYears.CarnivalYear?> YearAsync(CancellationToken cancellationToken)
    {
        var years = await db.CarnivalYears.AsNoTracking().OrderBy(y => y.StartDate).ToListAsync(cancellationToken);
        return years.SingleOrDefault(y => y.Active) ?? years.LastOrDefault();
    }

    /// <summary>"Samen met Ferry Stienissen · en de Raad van Elf", of <c>null</c> als het lid er alleen op staat.</summary>
    private static string? People(CarnivalTask task, Guid memberId, IReadOnlyDictionary<Guid, string> names)
    {
        var people = task.Assignees.Where(a => a.MemberId != memberId && names.ContainsKey(a.MemberId)).Select(a => names[a.MemberId])
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(task.OtherPeople))
        {
            people.Add(task.OtherPeople.Trim());
        }

        return people.Count == 0 ? null : $"Samen met {string.Join(" · ", people)}";
    }

    private static DomainException NotFound() =>
        new(ErrorCodes.NotFound, "Deze taak staat niet (meer) op jouw naam.", DomainErrorKind.NotFound);
}
