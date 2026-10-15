using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Tasks;
using Drammers.Modules.Membership.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class TaskCategoryConfiguration : IEntityTypeConfiguration<TaskCategory>
{
    public void Configure(EntityTypeBuilder<TaskCategory> builder)
    {
        builder.ToTable("TaskCategory", Schemas.Content);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(80);
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class CarnivalTaskConfiguration : IEntityTypeConfiguration<CarnivalTask>
{
    public void Configure(EntityTypeBuilder<CarnivalTask> builder)
    {
        builder.ToTable("CarnivalTask", Schemas.Content, t => t.HasCheckConstraint("CK_CarnivalTask_progress", "[progress] >= 0 AND [progress] <= 100"));
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.Ignore(t => t.EffectiveProgress);
        builder.HasIndex(t => new { t.CarnivalYearId, t.CategoryId });
        builder.HasOne<CarnivalYear>().WithMany().HasForeignKey(t => t.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskCategory>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Assignees).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CarnivalTaskAssigneeConfiguration : IEntityTypeConfiguration<CarnivalTaskAssignee>
{
    public void Configure(EntityTypeBuilder<CarnivalTaskAssignee> builder)
    {
        builder.ToTable("CarnivalTaskAssignee", Schemas.Content);
        builder.HasKey(a => new { a.TaskId, a.MemberId });
        builder.HasIndex(a => a.MemberId);
        builder.HasOne<Member>().WithMany().HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}
