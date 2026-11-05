namespace DevArv.Web.ViewModels;

/// <summary>API key en claro de una PC; sólo se conoce al crearla o regenerarla.</summary>
public sealed record WorkerKeyViewModel(string WorkerName, string Key);
