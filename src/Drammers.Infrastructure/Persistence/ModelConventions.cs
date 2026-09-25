using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Drammers.Infrastructure.Persistence;

/// <summary>
/// Datamodel-conventies uit docs/04 §1: kolommen in snake_case, tijden als <c>datetime2(3)</c> in UTC,
/// enums als <c>varchar(40)</c> met een CHECK-constraint.
/// </summary>
internal static class ModelConventions
{
    public static void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime2(3)");
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>().HaveColumnType("datetime2(3)");
    }

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.GetSchema() is null)
            {
                throw new InvalidOperationException($"Entiteit {entityType.ClrType.Name} heeft geen schema (docs/04 §2).");
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
                ApplyEnumConvention(entityType, property);
            }

            foreach (var key in entityType.GetKeys())
            {
                key.SetName(key.GetDefaultName());
            }

            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName(index.GetDefaultDatabaseName());
            }
        }
    }

    private static void ApplyEnumConvention(IMutableEntityType entityType, IMutableProperty property)
    {
        var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!enumType.IsEnum)
        {
            return;
        }

        property.SetProviderClrType(typeof(string));
        property.SetMaxLength(40);
        property.SetIsUnicode(false);

        var column = property.GetColumnName();
        var values = string.Join(", ", Enum.GetNames(enumType).Select(name => $"'{name}'"));
        entityType.AddCheckConstraint($"CK_{entityType.GetTableName()}_{column}", $"[{column}] IN ({values})");
    }

    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue ? (value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime()) : value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);
}
