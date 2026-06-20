namespace Quotinator.Constants.Routes;

/// <summary>Route strings for all Quotinator endpoints and UI paths.</summary>
public static class ApiRoutes
{
    public const string Health           = "/api/v1/health";
    public const string Version          = "/api/v1/version";
    public const string DatabaseReseed   = "/api/v1/admin/database/reseed";
    public const string DatabaseReset    = "/api/v1/admin/database/reset";
    public const string CultureSet       = "/Culture/Set";
    public const string ScalarUi         = "/scalar/v1";
    public const string OpenApiSpec      = "/openapi/v1.json";
}
