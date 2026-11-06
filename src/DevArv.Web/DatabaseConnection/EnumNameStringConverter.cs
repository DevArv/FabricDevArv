using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DevArv.Web.DatabaseConnection;

/// <summary>
/// Guarda un enum como texto en PascalCase: el miembro <c>WAITING_APPROVAL</c> se guarda como <c>WaitingApproval</c>.
/// Los miembros del enum siguen el estándar UPPER_SNAKE_CASE del código y la base de datos conserva los valores que
/// ya tenía, así que las filas existentes se leen sin migrar datos.
/// </summary>
public sealed class EnumNameStringConverter<TEnum>() : ValueConverter<TEnum, string>(
    Value => ToDatabase(Value),
    Text => FromDatabase(Text))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> _ValuesByText =
        Enum.GetValues<TEnum>().ToDictionary(value => ToText(value.ToString()));

    /// <summary>Texto que se guarda para <paramref name="Value"/> («WAITING_APPROVAL» → «WaitingApproval»).</summary>
    public static string ToDatabase(TEnum Value) => ToText(Value.ToString());

    /// <summary>Miembro del enum que corresponde al texto guardado. Lanza si el texto no corresponde a ningún miembro.</summary>
    public static TEnum FromDatabase(string Text) =>
        _ValuesByText.TryGetValue(Text, out var value)
            ? value
            : throw new InvalidOperationException($"El valor «{Text}» guardado en la base de datos no es válido para {typeof(TEnum).Name}.");

    private static string ToText(string Name) =>
        string.Concat(Name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}
