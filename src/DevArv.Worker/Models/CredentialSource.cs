namespace DevArv.Worker.Models;

/// <summary>De dónde tomará el worker una credencial. <see cref="Configured"/> es false si no hay ninguna explícita.</summary>
public sealed record CredentialSource(bool Configured, string Description);
