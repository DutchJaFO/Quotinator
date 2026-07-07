namespace Quotinator.Constants.Routes;

/// <summary>Route strings for all Quotinator endpoints and UI paths.</summary>
public static class ApiRoutes
{
    public const string Health           = "/api/v1/health";
    public const string Version          = "/api/v1/version";
    public const string DatabaseSeedPreview = "/api/v1/admin/database/seed/preview";
    public const string DatabaseReseed      = "/api/v1/admin/database/reseed";
    public const string DatabaseReset       = "/api/v1/admin/database/reset";
    public const string AuditLog            = "/api/v1/admin/audit";
    public const string Import              = "/api/v1/import";
    public const string ImportPreview       = "/api/v1/import/preview";
    public const string ImportConflicts       = "/api/v1/import/conflicts";
    public const string ImportConflictsDecide = "/api/v1/import/conflicts/{id}/decide";
    public const string ImportConflictsUndo   = "/api/v1/import/conflicts/{id}/undo";
    public const string ImportConflictsApply  = "/api/v1/import/conflicts/apply";
    public const string CultureSet       = "/Culture/Set";
    public const string ScalarUi         = "/scalar/v1";
    public const string OpenApiSpec      = "/openapi/v1.json";
}
