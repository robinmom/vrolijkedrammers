using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Tasks;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Tasks;

public sealed record TaskCategoryView(Guid Id, string Name, int SortOrder, bool Active, int TaskCount);

public sealed record TaskPerson(Guid MemberId, string Name);

public sealed record TaskView(
    Guid Id, Guid CategoryId, string CategoryName, string Title, string? Description, CarnivalTaskStatus Status, int Progress, int? EffectiveProgress,
    DateOnly? StartedOn, DateOnly? DueOn, DateOnly? CompletedOn, bool Overdue, IReadOnlyList<TaskPerson> Assignees, DateTime? UpdatedAt);

/// <summary>Totalen van een takenlijst; <c>Percent</c> is de gemiddelde voortgang van alle taken behalve geannuleerde.</summary>
public sealed record TaskSummary(int Total, int Done, int InProgress, int NotStarted, int Cancelled, int Overdue, int Percent);

public sealed record TaskYearOption(int Id, string Name);

public sealed record TaskYearView(
    int YearId, string YearName, DateOnly CarnivalStartDate, int DaysUntilCarnival, TaskSummary Summary, IReadOnlyList<TaskView> Tasks,
    IReadOnlyList<TaskYearOption> Years, TaskYearOption? NextYear);

public sealed record TaskInput(
    Guid CategoryId, string Title, string? Description, CarnivalTaskStatus Status, int Progress, DateOnly? DueOn, IReadOnlyList<Guid> MemberIds);

public sealed record TaskReportRow(string Name, int Total, int Done, int InProgress, int NotStarted, int Cancelled, int Overdue, int Percent);

public sealed record TaskReport(int YearId, string YearName, TaskSummary Summary, IReadOnlyList<TaskReportRow> PerCategory, IReadOnlyList<TaskReportRow> PerPerson);

/// <summary>
/// Takenlijst voor de voorbereiding van carnaval (2026-10-15): taken per carnavalsjaar in zelf beheerde categorieën, met
/// leden, status, voortgang, gestart op en verwacht klaar; elk jaar kopieert het bestuur de taken die terugkomen.
/// </summary>
public sealed class CarnivalTasks(DrammersDbContext db, IClock clock, IAuditLogger audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    // ---- Categorieën ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TaskCategoryView>> CategoriesAsync(CancellationToken cancellationToken) =>
        await db.TaskCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new TaskCategoryView(c.Id, c.Name, c.SortOrder, c.Active, db.CarnivalTasks.Count(t => t.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);

    public async Task<Guid> SaveCategoryAsync(Guid? id, string name, int sortOrder, bool active, CancellationToken cancellationToken)
    {
        var clean = name.Trim();
        if (clean.Length is 0 or > 80)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef de categorie een naam (maximaal 80 tekens).");
        }

        if (await db.TaskCategories.AnyAsync(c => c.Name == clean && c.Id != id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, $"De categorie \"{clean}\" bestaat al.");
        }

        var category = id is { } existing
            ? await db.TaskCategories.SingleOrDefaultAsync(c => c.Id == existing, cancellationToken)
                ?? throw new DomainException(ErrorCodes.NotFound, "Categorie niet gevonden.", DomainErrorKind.NotFound)
            : db.TaskCategories.Add(new TaskCategory { Id = IdGenerator.NewId(), Name = clean }).Entity;
        (category.Name, category.SortOrder, category.Active) = (clean, sortOrder, active);
        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }

    /// <summary>Alleen een lege categorie kan weg; anders op "niet actief" zetten.</summary>
    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.CarnivalTasks.AnyAsync(t => t.CategoryId == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze categorie heeft taken; zet hem op niet actief in plaats van te verwijderen.", DomainErrorKind.Conflict);
        }

        await db.TaskCategories.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    // ---- Taken ------------------------------------------------------------------------------------------------------

    /// <summary>De takenlijst van een carnavalsjaar (zonder jaar: het actieve).</summary>
    public async Task<TaskYearView> YearAsync(int? yearId, CancellationToken cancellationToken)
    {
        var years = await db.CarnivalYears.AsNoTracking().OrderBy(y => y.StartDate).ToListAsync(cancellationToken);
        var year = (yearId is { } id ? years.SingleOrDefault(y => y.Id == id) : years.SingleOrDefault(y => y.Active) ?? years.LastOrDefault())
            ?? throw new DomainException(ErrorCodes.NotFound, "Er is geen carnavalsjaar. Maak er eerst een aan (Configuratie).", DomainErrorKind.NotFound);
        var next = years.FirstOrDefault(y => y.StartDate > year.StartDate);
        var tasks = await TasksAsync(year.Id, cancellationToken);
        return new TaskYearView(
            year.Id, year.Name, year.CarnivalStartDate, Math.Max(0, year.CarnivalStartDate.DayNumber - Today.DayNumber), Summarize(tasks), tasks,
            [.. years.Select(y => new TaskYearOption(y.Id, y.Name))], next is null ? null : new TaskYearOption(next.Id, next.Name));
    }

    public async Task<Guid> CreateAsync(int yearId, TaskInput input, CancellationToken cancellationToken)
    {
        if (!await db.CarnivalYears.AnyAsync(y => y.Id == yearId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.NotFound, "Carnavalsjaar niet gevonden.", DomainErrorKind.NotFound);
        }

        var task = new CarnivalTask { Id = IdGenerator.NewId(), CarnivalYearId = yearId, Title = "" };
        await ApplyAsync(task, input, cancellationToken);
        db.CarnivalTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("task.created", "CarnivalTask", task.Id.ToString(), null, task.Title), cancellationToken);
        return task.Id;
    }

    public async Task UpdateAsync(Guid id, TaskInput input, CancellationToken cancellationToken)
    {
        var task = await db.CarnivalTasks.Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Taak niet gevonden.", DomainErrorKind.NotFound);
        var before = $"{task.Status} {task.Progress}%";
        await ApplyAsync(task, input, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("task.updated", "CarnivalTask", task.Id.ToString(), before, $"{task.Status} {task.Progress}%"), cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var title = await db.CarnivalTasks.Where(t => t.Id == id).Select(t => t.Title).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Taak niet gevonden.", DomainErrorKind.NotFound);
        await db.CarnivalTasks.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("task.deleted", "CarnivalTask", id.ToString(), title, null), cancellationToken);
    }

    /// <summary>
    /// De aangevinkte taken naar het volgende carnavalsjaar: weer "nog niet gestart", met dezelfde leden; "verwacht klaar"
    /// een jaar later. Een taak die al gekopieerd is, komt er niet nog een keer bij. Geeft het aantal nieuwe taken.
    /// </summary>
    public async Task<int> CopyToNextYearAsync(int fromYearId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        var years = await db.CarnivalYears.AsNoTracking().OrderBy(y => y.StartDate).ToListAsync(cancellationToken);
        var from = years.SingleOrDefault(y => y.Id == fromYearId)
            ?? throw new DomainException(ErrorCodes.NotFound, "Carnavalsjaar niet gevonden.", DomainErrorKind.NotFound);
        var to = years.FirstOrDefault(y => y.StartDate > from.StartDate)
            ?? throw new DomainException(ErrorCodes.Validation, "Maak eerst het volgende carnavalsjaar aan (Configuratie → Carnavalsjaren).", DomainErrorKind.Conflict);
        var already = await db.CarnivalTasks.Where(t => t.CarnivalYearId == to.Id && t.CopiedFromId != null).Select(t => t.CopiedFromId!.Value)
            .ToListAsync(cancellationToken);
        var sources = await db.CarnivalTasks.AsNoTracking().Include(t => t.Assignees)
            .Where(t => t.CarnivalYearId == from.Id && taskIds.Contains(t.Id) && !already.Contains(t.Id)).ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            db.CarnivalTasks.Add(new CarnivalTask
            {
                Id = IdGenerator.NewId(),
                CarnivalYearId = to.Id,
                CategoryId = source.CategoryId,
                Title = source.Title,
                Description = source.Description,
                Status = CarnivalTaskStatus.NotStarted,
                DueOn = source.DueOn?.AddYears(1),
                CopiedFromId = source.Id,
                Assignees = [.. source.Assignees.Select(a => new CarnivalTaskAssignee { MemberId = a.MemberId })],
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("task.copied", "CarnivalYear", to.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), from.Name,
            JsonSerializer.Serialize(new { to = to.Name, count = sources.Count }, JsonSerializerOptions.Web)), cancellationToken);
        return sources.Count;
    }

    public async Task<TaskReport> ReportAsync(int? yearId, CancellationToken cancellationToken)
    {
        var year = await YearAsync(yearId, cancellationToken);
        var perCategory = year.Tasks.GroupBy(t => t.CategoryName).Select(g => Row(g.Key, [.. g])).OrderBy(r => r.Name).ToList();
        var perPerson = year.Tasks.SelectMany(t => t.Assignees.Select(a => (a.Name, Task: t))).GroupBy(x => x.Name)
            .Select(g => Row(g.Key, [.. g.Select(x => x.Task)])).OrderBy(r => r.Percent).ThenBy(r => r.Name).ToList();
        return new TaskReport(year.YearId, year.YearName, year.Summary, perCategory, perPerson);
    }

    /// <summary>Actieve leden om aan een taak te koppelen (zonder het recht om leden in te zien).</summary>
    public async Task<IReadOnlyList<TaskPerson>> SearchMembersAsync(string search, CancellationToken cancellationToken)
    {
        var term = search.Trim();
        if (term.Length < 2)
        {
            return [];
        }

        return (await db.Members.AsNoTracking().Where(m => m.FullName.Contains(term) || m.MemberNumber == term)
                .OrderBy(m => m.FullName).Take(40).ToListAsync(cancellationToken))
            .Where(m => m.EffectiveStatus == MembershipStatus.Active).Take(15).Select(m => new TaskPerson(m.Id, m.FullName)).ToList();
    }

    private async Task<List<TaskView>> TasksAsync(int yearId, CancellationToken cancellationToken)
    {
        var tasks = await db.CarnivalTasks.AsNoTracking().Include(t => t.Assignees).Where(t => t.CarnivalYearId == yearId).ToListAsync(cancellationToken);
        var categories = await db.TaskCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var memberIds = tasks.SelectMany(t => t.Assignees.Select(a => a.MemberId)).Distinct().ToList();
        var names = await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.FullName, cancellationToken);
        var today = Today;
        return [.. tasks
            .OrderBy(t => categories.TryGetValue(t.CategoryId, out var c) ? c.SortOrder : int.MaxValue)
            .ThenBy(t => categories.TryGetValue(t.CategoryId, out var c) ? c.Name : "")
            .ThenBy(t => t.Status == CarnivalTaskStatus.Cancelled)
            .ThenBy(t => t.DueOn ?? DateOnly.MaxValue).ThenBy(t => t.Title)
            .Select(t => new TaskView(
                t.Id, t.CategoryId, categories.TryGetValue(t.CategoryId, out var c) ? c.Name : "—", t.Title, t.Description, t.Status, t.Progress,
                t.EffectiveProgress, t.StartedOn, t.DueOn, t.CompletedOn,
                t.DueOn < today && t.Status is CarnivalTaskStatus.NotStarted or CarnivalTaskStatus.InProgress,
                [.. t.Assignees.Where(a => names.ContainsKey(a.MemberId)).Select(a => new TaskPerson(a.MemberId, names[a.MemberId])).OrderBy(p => p.Name)],
                t.UpdatedAt ?? t.CreatedAt))];
    }

    private async Task ApplyAsync(CarnivalTask task, TaskInput input, CancellationToken cancellationToken)
    {
        var title = input.Title.Trim();
        if (title.Length is 0 or > 200 || input.Description?.Trim().Length > 2000 || input.Progress is < 0 or > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef de taak een titel (maximaal 200 tekens) en een voortgang tussen 0 en 100%.");
        }

        if (!await db.TaskCategories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een categorie.");
        }

        var memberIds = input.MemberIds.Distinct().ToList();
        if (await db.Members.CountAsync(m => memberIds.Contains(m.Id), cancellationToken) != memberIds.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Een van de gekozen leden bestaat niet (meer).");
        }

        var today = Today;
        task.Title = title;
        task.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        task.CategoryId = input.CategoryId;
        task.DueOn = input.DueOn;
        task.Status = input.Status;
        switch (input.Status)
        {
            case CarnivalTaskStatus.NotStarted:
                (task.Progress, task.StartedOn, task.CompletedOn) = (0, null, null);
                break;
            case CarnivalTaskStatus.InProgress:
                task.Progress = input.Progress;
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

        task.Assignees.RemoveAll(a => !memberIds.Contains(a.MemberId));
        foreach (var memberId in memberIds.Where(m => task.Assignees.All(a => a.MemberId != m)))
        {
            task.Assignees.Add(new CarnivalTaskAssignee { TaskId = task.Id, MemberId = memberId });
        }
    }

    private static TaskSummary Summarize(IReadOnlyCollection<TaskView> tasks)
    {
        var counted = tasks.Where(t => t.EffectiveProgress is not null).ToList();
        return new TaskSummary(
            tasks.Count, tasks.Count(t => t.Status == CarnivalTaskStatus.Done), tasks.Count(t => t.Status == CarnivalTaskStatus.InProgress),
            tasks.Count(t => t.Status == CarnivalTaskStatus.NotStarted), tasks.Count(t => t.Status == CarnivalTaskStatus.Cancelled), tasks.Count(t => t.Overdue),
            counted.Count == 0 ? 0 : (int)Math.Round(counted.Average(t => t.EffectiveProgress!.Value)));
    }

    private static TaskReportRow Row(string name, IReadOnlyCollection<TaskView> tasks)
    {
        var s = Summarize(tasks);
        return new TaskReportRow(name, s.Total, s.Done, s.InProgress, s.NotStarted, s.Cancelled, s.Overdue, s.Percent);
    }
}
