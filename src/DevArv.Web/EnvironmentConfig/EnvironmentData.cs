using Npgsql;

namespace DevArv.Web.EnvironmentConfig;

/// <summary>Datos de conexión a la base de datos (sección EnvironmentData del appsettings).</summary>
public class EnvironmentData
{
    public string Host { get; set; } = "";
    public string Port { get; set; } = "";
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Construye y retorna el connection string de PostgreSQL (escapa los valores).</summary>
    public string GetConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = int.Parse(Port),
        Database = Database,
        Username = Username,
        Password = Password,
    }.ConnectionString;
}
