using System.Text;

namespace DevArv.Worker.Services;

/// <summary>
/// Acumula el log de una ejecución y lo envía al servidor en lotes (cada 2 s, o cada 15 s como señal de vida).
/// Si el servidor responde que la ejecución fue cancelada, cancela el trabajo local.
/// </summary>
public sealed class LogShipper : IAsyncDisposable
{
    private static readonly TimeSpan _FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _KeepAliveInterval = TimeSpan.FromSeconds(15);
    private const int MAX_CHUNK_CHARS = 64_000;

    private readonly ServerClient _Server;
    private readonly long _RunID;
    private readonly CancellationTokenSource _JobCts;
    private readonly ILogger _Logger;
    private readonly StringBuilder _Buffer = new();
    private readonly CancellationTokenSource _Stop = new();
    private readonly Task _Loop;
    private DateTimeOffset _LastSent = DateTimeOffset.UtcNow;

    public LogShipper(ServerClient Server, long RunID, CancellationTokenSource JobCts, ILogger Logger)
    {
        _Server = Server;
        _RunID = RunID;
        _JobCts = JobCts;
        _Logger = Logger;
        _Loop = Task.Run(LoopAsync);
    }

    public bool CancelledByServer { get; private set; }

    public void Write(string Line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {Line}";
        lock (_Buffer) _Buffer.AppendLine(stamped);
    }

    private async Task LoopAsync()
    {
        while (!_Stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_FlushInterval, _Stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await FlushAsync(Force: false);
        }
    }

    private async Task FlushAsync(bool Force)
    {
        string chunk;
        lock (_Buffer)
        {
            if (_Buffer.Length == 0 && !Force && DateTimeOffset.UtcNow - _LastSent < _KeepAliveInterval) return;
            var take = Math.Min(_Buffer.Length, MAX_CHUNK_CHARS);
            chunk = _Buffer.ToString(0, take);
            _Buffer.Remove(0, take);
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _Server.AppendLogAsync(
                RunID: _RunID,
                Text: chunk,
                CT: timeout.Token);
            _LastSent = DateTimeOffset.UtcNow;
            if (response.CancelRequested && !CancelledByServer)
            {
                CancelledByServer = true;
                _Logger.LogWarning("El servidor canceló la ejecución {RunId}", _RunID);
                try { _JobCts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Se reintenta en el próximo ciclo con el texto devuelto al inicio del búfer.
            lock (_Buffer) _Buffer.Insert(0, chunk);
            _Logger.LogDebug(ex, "No se pudo enviar el log de la ejecución {RunId}", _RunID);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _Stop.Cancel();
        try { await _Loop; } catch (OperationCanceledException) { }

        // Envío final de todo lo pendiente (varios lotes si hace falta).
        for (var i = 0; i < 20; i++)
        {
            int pending;
            lock (_Buffer) pending = _Buffer.Length;
            if (pending == 0) break;
            await FlushAsync(Force: true);
        }
        _Stop.Dispose();
    }
}
