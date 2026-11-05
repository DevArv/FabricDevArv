namespace DevArv.Web.ViewModels;

/// <summary>Resultado de probar la conexión de un proyecto con su repositorio de GitHub.</summary>
public sealed record ProjectConnectionTestViewModel(string Message, bool IsError);
