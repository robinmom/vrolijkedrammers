namespace Drammers.Infrastructure.Persistence;

/// <summary>Databaseschema per module (ADR-003, docs/04 §2).</summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Membership = "membership";
    public const string Content = "content";
    public const string Notification = "notification";
    public const string Ticketing = "ticketing";
    public const string Payments = "payments";
    public const string Parade = "parade";
    public const string Import = "import";
    public const string Audit = "audit";
    public const string Config = "config";
    public const string Reporting = "reporting";

    /// <summary>Schema's waarin de runtime-identiteit gegevens mag wijzigen (niet <c>audit</c> en <c>reporting</c>).</summary>
    public static readonly string[] RuntimeWritable =
        [Identity, Membership, Content, Notification, Ticketing, Payments, Parade, Import, Config];

    public static readonly string[] All = [.. RuntimeWritable, Audit, Reporting];
}
