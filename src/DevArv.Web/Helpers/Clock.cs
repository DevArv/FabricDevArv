using System.Globalization;

namespace DevArv.Web.Helpers;

/// <summary>Convierte fechas UTC a la zona horaria configurada (App:TimeZone).</summary>
public sealed class Clock(IConfiguration config)
{
    private readonly TimeZoneInfo _zone = ResolveZone(config["App:TimeZone"]);

    public DateTimeOffset Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _zone);

    public string Format(DateTimeOffset? utc, string format = "dd/MM/yyyy HH:mm") =>
        utc is null ? "" : Local(utc.Value).ToString(format, CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
