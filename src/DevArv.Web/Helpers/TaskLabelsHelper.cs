namespace DevArv.Web.Helpers;

public static class TaskLabelsHelper
{
    /// <summary>"a, b ,a" → "a, b" (sin repetidos, máx. 500 caracteres); null si no queda ninguna etiqueta.</summary>
    public static string? Normalize(string? labels)
    {
        if (string.IsNullOrWhiteSpace(labels)) return null;
        var parts = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined[..Math.Min(joined.Length, 500)];
    }
}
