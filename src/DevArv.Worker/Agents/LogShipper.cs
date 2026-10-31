using System.Text;
using DevArv.Worker.Infrastructure;

namespace DevArv.Worker.Agents;

/// <summary>
/// Acumula el log de una ejecución y lo envía al servidor en lotes (cada 2 s, o cada 15 s como señal de vida).
/// Si el servidor responde que la ejecución fue cancelada, cancela el trabajo local.
/// </summary>
public sealed class LogShipper : IAsyncDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);
    private const int MaxChunkChars = 64_000;

    private readonly ServerClient _server;
    private readonly long _runId;
    private readonly CancellationTokenSource _jobCts;
    private readonly ILogger _logger;
    private readonly StringBuilder _buffer = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private DateTimeOffset _lastSent = DateTimeOffset.UtcNow;

    public LogShipper(ServerClient server, long runId, CancellationTokenSource jobCts, ILogger logger)
    {
        _server = server;
        _runId = runId;
        _jobCts = jobCts;
        _logger = logger;
        _loop = Task.Run(LoopAsync);
    }

    public bool CancelledByServer { get; private set; }

    public void Write(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (_buffer) _buffer.AppendLine(stamped);
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await FlushAsync(force: false);
        }
    }

    private async Task FlushAsync(bool force)
    {
        string chunk;
        lock (_buffer)
        {
            if (_buffer.Length == 0 && !force && DateTimeOffset.UtcNow - _lastSent < KeepAliveInterval) return;
            var take = Math.Min(_buffer.Length, MaxChunkChars);
            chunk = _buffer.ToString(0, take);
            _buffer.Remove(0, take);
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _server.AppendLogAsync(_runId, chunk, timeout.Token);
            _lastSent = DateTimeOffset.UtcNow;
            if (response.CancelRequested && !CancelledByServer)
            {
                CancelledByServer = true;
                _logger.LogWarning("El servidor canceló la ejecución {RunId}", _runId);
                try { _jobCts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Se reintenta en el próximo ciclo con el texto devuelto al inicio del búfer.
            lock (_buffer) _buffer.Insert(0, chunk);
            _logger.LogDebug(ex, "No se pudo enviar el log de la ejecución {RunId}", _runId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop; } catch (OperationCanceledException) { }

        // Envío final de todo lo pendiente (varios lotes si hace falta).
        for (var i = 0; i < 20; i++)
        {
            int pending;
            lock (_buffer) pending = _buffer.Length;
            if (pending == 0) break;
            await FlushAsync(force: true);
        }
        _stop.Dispose();
    }
}
