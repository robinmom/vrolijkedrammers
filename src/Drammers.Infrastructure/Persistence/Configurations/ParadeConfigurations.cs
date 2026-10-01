using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class ParadeConfiguration : IEntityTypeConfiguration<Parade>
{
    public void Configure(EntityTypeBuilder<Parade> builder)
    {
        builder.ToTable("Parade", Schemas.Parade);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.StartLocation).HasMaxLength(200);
        builder.Property(p => p.RouteDescription).HasMaxLength(2000);
        builder.Property(p => p.RouteLengthKm).HasPrecision(5, 2);
        builder.Property(p => p.DefaultSpacingMeters).HasPrecision(5, 2);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Property(p => p.InfoText).HasMaxLength(8000);
        builder.Property(p => p.ArrivalLocation).HasMaxLength(100);
        builder.OwnsMany(p => p.FixedEntries, e =>
        {
            e.ToJson("fixed_entries");
            e.Property(x => x.Name).HasMaxLength(100);
        });
        builder.HasOne<Modules.Content.CarnivalYears.CarnivalYear>().WithMany().HasForeignKey(p => p.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        // Meerdere optochten per carnavalsjaar mogelijk (fase 22a); formeel is er één per jaar.
        builder.HasIndex(p => p.CarnivalYearId);
        builder.ToTable(t => t.HasCheckConstraint("CK_Parade_registration_period", "[registration_closes_at] > [registration_opens_at]"));
    }
}

internal sealed class ParadeNumberSequenceConfiguration : IEntityTypeConfiguration<ParadeNumberSequence>
{
    public void Configure(EntityTypeBuilder<ParadeNumberSequence> builder)
    {
        builder.ToTable("ParadeNumberSequence", Schemas.Parade);
        builder.HasKey(s => s.ParadeId);
        builder.HasOne<Parade>().WithOne().HasForeignKey<ParadeNumberSequence>(s => s.ParadeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ParadeCategoryConfiguration : IEntityTypeConfiguration<ParadeCategory>
{
    public void Configure(EntityTypeBuilder<ParadeCategory> builder)
    {
        builder.ToTable("ParadeCategory", Schemas.Parade);
        builder.Property(c => c.Code).HasMaxLength(40).IsUnicode(false);
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.HasIndex(c => c.Code).IsUnique().HasFilter("[parade_id] IS NULL");
        builder.HasIndex(c => new { c.ParadeId, c.Code }).IsUnique().HasFilter("[parade_id] IS NOT NULL");
        builder.HasOne<Parade>().WithMany().HasForeignKey(c => c.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_ParadeCategory_range",
            "[minimum_participants] IS NULL OR [maximum_participants] IS NULL OR [minimum_participants] <= [maximum_participants]"));
        builder.HasData(ParadeCategory.Seed);
    }
}

internal sealed class ParadeRegistrationConfiguration : IEntityTypeConfiguration<ParadeRegistration>
{
    public void Configure(EntityTypeBuilder<ParadeRegistration> builder)
    {
        builder.ToTable("ParadeRegistration", Schemas.Parade, t =>
        {
            t.HasCheckConstraint("CK_ParadeRegistration_counts", "[children_count] >= 0 AND [adult_count] >= 0");
            t.HasCheckConstraint("CK_ParadeRegistration_start_number", "[start_number] IS NULL OR [start_number] > 0");
            t.HasCheckConstraint("CK_ParadeRegistration_estimated_length",
                "[estimated_length_meters] IS NULL OR ([estimated_length_meters] > 0 AND [estimated_length_meters] <= 100)");
            t.HasCheckConstraint("CK_ParadeRegistration_measured_length",
                "[measured_length_meters] IS NULL OR ([measured_length_meters] > 0 AND [measured_length_meters] <= 100)");
            // ADR-011: een concept heeft nooit een opgavenummer, na indienen altijd.
            t.HasCheckConstraint("CK_ParadeRegistration_number_draft", "[status] <> 'Draft' OR [registration_number] IS NULL");
            t.HasCheckConstraint("CK_ParadeRegistration_number_submitted", "[status] = 'Draft' OR [registration_number] IS NOT NULL");
        });
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.GroupName).HasMaxLength(100);
        builder.Property(r => r.ContactName).HasMaxLength(100);
        builder.Property(r => r.ContactPhone).HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.ContactEmail).HasMaxLength(254);
        builder.Property(r => r.Subject).HasMaxLength(150);
        builder.Property(r => r.SubjectDescription).HasMaxLength(2000);
        builder.Property(r => r.AdditionalInformation).HasMaxLength(4000);
        builder.Property(r => r.EstimatedLengthMeters).HasPrecision(5, 2);
        builder.Property(r => r.MeasuredLengthMeters).HasPrecision(5, 2);
        builder.Property(r => r.SpacingAfterMeters).HasPrecision(5, 2);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Property(r => r.VerificationCodeHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(r => r.StatusTokenHash).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(r => r.StatusTokenHash).IsUnique().HasFilter("[status_token_hash] IS NOT NULL");
        builder.Property<int>("TotalParticipants").HasComputedColumnSql("[children_count] + [adult_count]");
        OwnsAddress(builder, r => r.BuildAddress, "build_address");
        OwnsAddress(builder, r => r.JuryInspectionAddress, "jury_inspection_address");
        builder.HasOne<Parade>().WithMany().HasForeignKey(r => r.ParadeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParadeCategory>().WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.ParadeId, r.RegistrationNumber }).IsUnique().HasFilter("[registration_number] IS NOT NULL");
        builder.HasIndex(r => new { r.ParadeId, r.StartNumber }).IsUnique().HasFilter("[start_number] IS NOT NULL");
        builder.HasIndex(r => new { r.ParadeId, r.Status });
    }

    private static void OwnsAddress(
        EntityTypeBuilder<ParadeRegistration> builder, System.Linq.Expressions.Expression<Func<ParadeRegistration, Address?>> navigation, string prefix)
    {
        builder.OwnsOne(navigation, a =>
        {
            // Zelfde tabel: de sleutel van het owned type is de id van de inschrijving.
            a.Property<Guid>("ParadeRegistrationId").HasColumnName("id");
            a.Property(x => x.Street).HasColumnName($"{prefix}_street").HasMaxLength(100);
            a.Property(x => x.HouseNumber).HasColumnName($"{prefix}_house_number").HasMaxLength(5).IsUnicode(false);
            a.Property(x => x.Addition).HasColumnName($"{prefix}_addition").HasMaxLength(10);
            a.Property(x => x.PostalCode).HasColumnName($"{prefix}_postal_code").HasMaxLength(10).IsUnicode(false);
            a.Property(x => x.City).HasColumnName($"{prefix}_city").HasMaxLength(60);
            a.Property(x => x.Country).HasColumnName($"{prefix}_country").HasMaxLength(2).IsUnicode(false);
        });
        builder.Navigation(navigation!).IsRequired();
    }
}

internal sealed class ParadeBuildLocationConfiguration : IEntityTypeConfiguration<ParadeBuildLocation>
{
    public void Configure(EntityTypeBuilder<ParadeBuildLocation> builder)
    {
        builder.ToTable("ParadeBuildLocation", Schemas.Parade);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.OwnsOne(l => l.Address, a =>
        {
            a.Property<Guid>("ParadeBuildLocationId").HasColumnName("id");
            a.Property(x => x.Street).HasColumnName("street").HasMaxLength(100);
            a.Property(x => x.HouseNumber).HasColumnName("house_number").HasMaxLength(5).IsUnicode(false);
            a.Property(x => x.Addition).HasColumnName("addition").HasMaxLength(10);
            a.Property(x => x.PostalCode).HasColumnName("postal_code").HasMaxLength(10).IsUnicode(false);
            a.Property(x => x.City).HasColumnName("city").HasMaxLength(60);
            a.Property(x => x.Country).HasColumnName("country").HasMaxLength(2).IsUnicode(false);
        });
        builder.Navigation(l => l.Address).IsRequired();
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.UserId);
    }
}

internal sealed class ParadeRegistrationManagerConfiguration : IEntityTypeConfiguration<ParadeRegistrationManager>
{
    public void Configure(EntityTypeBuilder<ParadeRegistrationManager> builder)
    {
        builder.ToTable("ParadeRegistrationManager", Schemas.Parade);
        builder.HasKey(m => new { m.RegistrationId, m.UserId });
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(m => m.RegistrationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.UserId);
    }
}

internal sealed class ParadeStatusHistoryConfiguration : IEntityTypeConfiguration<ParadeStatusHistory>
{
    public void Configure(EntityTypeBuilder<ParadeStatusHistory> builder)
    {
        builder.ToTable("ParadeStatusHistory", Schemas.Parade);
        builder.Property(h => h.Reason).HasMaxLength(1000);
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(h => h.RegistrationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(h => h.RegistrationId);
    }
}

internal sealed class ParadeRegistrationHistoryConfiguration : IEntityTypeConfiguration<ParadeRegistrationHistory>
{
    public void Configure(EntityTypeBuilder<ParadeRegistrationHistory> builder)
    {
        builder.ToTable("ParadeRegistrationHistory", Schemas.Parade);
        builder.Property(h => h.FieldName).HasMaxLength(60).IsUnicode(false);
        builder.Property(h => h.FieldLabel).HasMaxLength(100);
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(h => h.RegistrationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(h => new { h.RegistrationId, h.ChangedAt });
    }
}

internal sealed class ParadeDocumentConfiguration : IEntityTypeConfiguration<ParadeDocument>
{
    public void Configure(EntityTypeBuilder<ParadeDocument> builder)
    {
        builder.ToTable("ParadeDocument", Schemas.Parade);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.FileName).HasMaxLength(200);
        builder.Property(d => d.ContentType).HasMaxLength(100).IsUnicode(false);
        builder.Property(d => d.BlobPath).HasMaxLength(300);
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(d => d.RegistrationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.RegistrationId);
    }
}

internal sealed class ParadeStatusEditPolicyConfiguration : IEntityTypeConfiguration<ParadeStatusEditPolicy>
{
    public void Configure(EntityTypeBuilder<ParadeStatusEditPolicy> builder)
    {
        builder.ToTable("ParadeStatusEditPolicy", Schemas.Parade);
        builder.Property(p => p.EditableFields).HasMaxLength(1000).IsUnicode(false);
        builder.HasOne<Parade>().WithMany().HasForeignKey(p => p.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.ParadeId, p.Status, p.ActorScope }).IsUnique();
        builder.HasData(DefaultEditPolicy.Seed);
    }
}

internal sealed class ParadeJudgingCategoryConfiguration : IEntityTypeConfiguration<Modules.Parade.Judging.ParadeJudgingCategory>
{
    public void Configure(EntityTypeBuilder<Modules.Parade.Judging.ParadeJudgingCategory> builder)
    {
        builder.ToTable("ParadeJudgingCategory", Schemas.Parade, t => t.HasCheckConstraint("CK_ParadeJudgingCategory_weights",
            "[weight_originality] BETWEEN 0 AND 5 AND [weight_carnivalesque] BETWEEN 0 AND 5 AND [weight_quality] BETWEEN 0 AND 5 AND [weight_overall] BETWEEN 0 AND 5"));
        builder.HasKey(c => new { c.ParadeId, c.CategoryId });
        builder.HasOne<Parade>().WithMany().HasForeignKey(c => c.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ParadeCategory>().WithMany().HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ParadeJurorAssignmentConfiguration : IEntityTypeConfiguration<Modules.Parade.Judging.ParadeJurorAssignment>
{
    public void Configure(EntityTypeBuilder<Modules.Parade.Judging.ParadeJurorAssignment> builder)
    {
        builder.ToTable("ParadeJurorAssignment", Schemas.Parade);
        builder.HasKey(a => new { a.ParadeId, a.UserId, a.CategoryId });
        builder.HasOne<Parade>().WithMany().HasForeignKey(a => a.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ParadeCategory>().WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.ParadeId, a.CategoryId });
    }
}

internal sealed class JudgingScoreConfiguration : IEntityTypeConfiguration<Modules.Parade.Judging.JudgingScore>
{
    public void Configure(EntityTypeBuilder<Modules.Parade.Judging.JudgingScore> builder)
    {
        builder.ToTable("JudgingScore", Schemas.Parade, t =>
        {
            t.HasCheckConstraint("CK_JudgingScore_value", "[value] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_JudgingScore_pass", "[pass] BETWEEN 1 AND 3");
        });
        builder.HasKey(s => new { s.RegistrationId, s.UserId, s.Pass, s.Criterion });
        builder.Property(s => s.Criterion).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(s => s.RegistrationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.ParadeId, s.UserId });
    }
}

internal sealed class JudgingSubmissionConfiguration : IEntityTypeConfiguration<Modules.Parade.Judging.JudgingSubmission>
{
    public void Configure(EntityTypeBuilder<Modules.Parade.Judging.JudgingSubmission> builder)
    {
        builder.ToTable("JudgingSubmission", Schemas.Parade);
        builder.HasKey(s => new { s.ParadeId, s.UserId });
        builder.HasOne<Parade>().WithMany().HasForeignKey(s => s.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JudgingOutsideReviewConfiguration : IEntityTypeConfiguration<Modules.Parade.Judging.JudgingOutsideReview>
{
    public void Configure(EntityTypeBuilder<Modules.Parade.Judging.JudgingOutsideReview> builder)
    {
        builder.ToTable("JudgingOutsideReview", Schemas.Parade);
        builder.HasKey(r => new { r.ParadeId, r.UserId, r.RegistrationId });
        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Parade>().WithMany().HasForeignKey(r => r.ParadeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ParadeRegistration>().WithMany().HasForeignKey(r => r.RegistrationId).OnDelete(DeleteBehavior.NoAction);
    }
}
