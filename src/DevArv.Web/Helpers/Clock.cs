using System.Globalization;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Helpers;

/// <summary>Convierte fechas UTC a la zona horaria configurada (App:TimeZone).</summary>
public sealed class Clock(IOptions<AppOptions> Options)
{
    private readonly TimeZoneInfo _Zone = ResolveZone(Options.Value.TimeZone);

    public DateTimeOffset Local(DateTimeOffset Utc) => TimeZoneInfo.ConvertTime(Utc, _Zone);

    public string Format(DateTimeOffset? Utc, string Format = "dd/MM/yyyy HH:mm") =>
        Utc is null ? "" : Local(Utc.Value).ToString(Format, CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveZone(string? ID)
    {
        if (string.IsNullOrWhiteSpace(ID)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(ID); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
