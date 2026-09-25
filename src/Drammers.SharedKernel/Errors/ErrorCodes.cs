namespace Drammers.SharedKernel.Errors;

/// <summary>
/// Machineleesbare foutcodes voor de <c>code</c>-extensie van ProblemDetails (docs/05 §1 en §10).
/// </summary>
public static class ErrorCodes
{
    public const string Unexpected = "UNEXPECTED_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string RoleNotFound = "ROLE_NOT_FOUND";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string RoleCodeTaken = "ROLE_CODE_TAKEN";
    public const string SystemRoleProtected = "SYSTEM_ROLE_PROTECTED";
    public const string UnknownPermission = "UNKNOWN_PERMISSION";
    public const string LockoutPrevented = "LOCKOUT_PREVENTED";
    public const string ProvisioningFailed = "PROVISIONING_FAILED";
    public const string CarnivalYearNotFound = "CARNIVAL_YEAR_NOT_FOUND";
    public const string CarnivalYearInvalidDates = "CARNIVAL_YEAR_INVALID_DATES";
    public const string CarnivalYearNameTaken = "CARNIVAL_YEAR_NAME_TAKEN";
    public const string ConfigKeyUnknown = "CONFIG_KEY_UNKNOWN";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileTypeNotAllowed = "FILE_TYPE_NOT_ALLOWED";
    public const string FileInfected = "FILE_INFECTED";
    public const string ContentNotFound = "CONTENT_NOT_FOUND";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string NoActiveCarnivalYear = "NO_ACTIVE_CARNIVAL_YEAR";
    public const string MemberNotFound = "MEMBER_NOT_FOUND";
    public const string MemberFieldFromSource = "MEMBER_FIELD_FROM_SOURCE";
    public const string SyncAlreadyRunning = "SYNC_ALREADY_RUNNING";
    public const string PurgeNotAllowed = "PURGE_NOT_ALLOWED";
}
