namespace DevArv.Web.Constants;

/// <summary>Modos de permisos que acepta el CLI de Claude Code (--permission-mode).</summary>
public static class PermissionModes
{
    public static readonly string[] ALL = ["default", "acceptEdits", "plan", "dontAsk", "bypassPermissions"];
}
