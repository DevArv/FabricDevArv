namespace DevArv.Contracts.Models;

/// <summary>Cabecera HTTP con la que cada worker (PC) se autentica ante el servidor.</summary>
public static class WorkerHeaders
{
    public const string API_KEY = "X-DevArv-Key";
}
