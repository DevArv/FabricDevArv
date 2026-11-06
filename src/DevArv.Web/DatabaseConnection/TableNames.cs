namespace DevArv.Web.DatabaseConnection;

/// <summary>
/// Nombres de las tablas, <c>Modulo_Tabla</c>. Una sola fuente para el modelo de EF Core, el renombrado de bases
/// antiguas (<see cref="DbInitializer"/>) y el SQL escrito a mano.
/// </summary>
public static class TableNames
{
    public const string PROJECTS = "Projects_Projects";
    public const string STAGES = "Workflow_Stages";
    public const string TASKS = "Tasks_Tasks";
    public const string TASK_ATTACHMENTS = "Tasks_Attachments";
    public const string TASK_RUNS = "Tasks_Runs";
    public const string TASK_COMMENTS = "Tasks_Comments";
    public const string TASK_EVENTS = "Tasks_Events";
    public const string WORKERS = "Workers_Workers";

    public const string IDENTITY_USERS = "Identity_Users";
    public const string IDENTITY_ROLES = "Identity_Roles";
    public const string IDENTITY_USER_ROLES = "Identity_UserRoles";
    public const string IDENTITY_USER_CLAIMS = "Identity_UserClaims";
    public const string IDENTITY_USER_LOGINS = "Identity_UserLogins";
    public const string IDENTITY_USER_TOKENS = "Identity_UserTokens";
    public const string IDENTITY_ROLE_CLAIMS = "Identity_RoleClaims";
}
