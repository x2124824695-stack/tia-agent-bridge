using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaAgent.Contracts;

namespace TiaAgent.Host.Worker;

public sealed class WorkerProcessClient : IAsyncDisposable, IDisposable
{
    private readonly ILogger<WorkerProcessClient> _logger;
    private readonly TiaInstallationDetector _detector;
    private readonly AccessMode _accessMode;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private Process? _process;
    private Task? _stderrPump;
    private Task<WorkerResponse>? _pending;
    private string? _pendingId;
    private bool _needsRecovery;
    private readonly string _journalPath;
    private readonly FileStream _sessionLock;
    private readonly int _timeoutMs;

    public WorkerProcessClient(
        ILogger<WorkerProcessClient> logger,
        TiaInstallationDetector detector,
        AccessMode accessMode)
    {
        _logger = logger;
        _detector = detector;
        _accessMode = accessMode;
        var configuredTimeout = Environment.GetEnvironmentVariable("TIA_AGENT_TIMEOUT_MS");
        _timeoutMs = configuredTimeout == null ? 60000 : int.Parse(configuredTimeout, System.Globalization.CultureInfo.InvariantCulture);
        if (_timeoutMs < 1000 || _timeoutMs > 600000) throw new ArgumentException("TIA_AGENT_TIMEOUT_MS must be 1000..600000.");
        var stateDirectory = Environment.GetEnvironmentVariable("TIA_AGENT_STATE_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TiaAgentBridge", Safety.Hashing.Sha256(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant()).Substring(0, 16));
        Directory.CreateDirectory(stateDirectory);
        _journalPath = Path.Combine(stateDirectory, "pending.json");
        _sessionLock = new FileStream(Path.Combine(stateDirectory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _needsRecovery = File.Exists(_journalPath);
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_needsRecovery || _pending != null)
                return new WorkerResponse { Success = false, ErrorCode = "session_uncertain", Error = "A prior request requires inspection. Use get_command_status and recover_session; never replay it automatically." };
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            request.ProtocolVersion = Protocol.Version;
            request.RequestId = Guid.NewGuid().ToString("N");

            var line = JsonSerializer.Serialize(request, _json);
            _pendingId = request.RequestId;
            File.WriteAllText(_journalPath, JsonSerializer.Serialize(new { requestId = request.RequestId, method = request.Method, state = "unknown" }, _json));
            _needsRecovery = true;
            await _process!.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            // Never cancel or replace the stdout reader after dispatch: timeout is
            // uncertainty, not cancellation of the operation inside TIA.
            _pending = ReadResponseAsync(request.RequestId);
            var timeout = Task.Delay(_timeoutMs, cancellationToken);
            if (await Task.WhenAny(_pending, timeout).ConfigureAwait(false) != _pending)
                return new WorkerResponse { RequestId = request.RequestId, Success = false, ErrorCode = "result_pending", Error = "Result wait ended; operation may still be running. Inspect get_command_status, do not retry." };
            var response = await _pending.ConfigureAwait(false);
            File.Delete(_journalPath);
            _needsRecovery = false;
            _pending = null;
            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkerResponse> ReadResponseAsync(string requestId)
    {
        var line = await _process!.StandardOutput.ReadLineAsync().ConfigureAwait(false)
            ?? throw new IOException("Worker closed stdout; operation result is unknown.");
        var response = JsonSerializer.Deserialize<WorkerResponse>(line, _json) ?? throw new IOException("Invalid worker response.");
        if (response.RequestId != requestId || response.ProtocolVersion != Protocol.Version)
            throw new IOException("Worker response identity/protocol mismatch; operation result is unknown.");
        // Retain completed results even if the waiting MCP caller has disconnected.
        var temporary = _journalPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { requestId, state = "completed", response }, _json)).ConfigureAwait(false);
        File.Move(temporary, _journalPath, true);
        return response;
    }

    public object GetCommandStatus()
    {
        if (!File.Exists(_journalPath)) return new { state = "idle" };
        using var doc = JsonDocument.Parse(File.ReadAllText(_journalPath));
        return doc.RootElement.Clone();
    }

    public async Task<object> RecoverAsync(bool acknowledge)
    {
        if (!acknowledge) throw new ArgumentException("Inspect get_command_status, then acknowledge=true.");
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("Request is still running.");
            if (!File.Exists(_journalPath)) return new { recovered = true, alreadyIdle = true };
            using var doc = JsonDocument.Parse(File.ReadAllText(_journalPath));
            if (doc.RootElement.GetProperty("state").GetString() != "completed") throw new InvalidOperationException("Result is unknown. Inspect TIA manually; automatic recovery is not allowed.");
            File.Delete(_journalPath);
            _pending = null;
            _needsRecovery = false;
            return new { recovered = true, alreadyIdle = false };
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
            return;

        TerminateWorker();
        var workerPath = _detector.ResolveWorkerPath();
        var publicApi = _detector.ResolveV21PublicApiDirectory();

        var psi = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(workerPath)!
        };
        psi.ArgumentList.Add("--access");
        psi.ArgumentList.Add(_accessMode.ToString().ToLowerInvariant());
        psi.Environment["TIA_PORTAL_PUBLIC_API"] = publicApi;

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start TIA Openness worker.");
        _stderrPump = PumpStderrAsync(_process, CancellationToken.None);

        WorkerResponse hello;
        try { hello = await SendHandshakeDirectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(_timeoutMs), cancellationToken).ConfigureAwait(false); }
        catch { TerminateWorker(); throw; }
        if (!hello.Success)
        {
            TerminateWorker();
            throw new InvalidOperationException("TIA Openness worker handshake failed: " + hello.Error);
        }
    }

    private async Task<WorkerResponse> SendHandshakeDirectAsync(CancellationToken cancellationToken)
    {
        var request = new WorkerRequest
        {
            Method = WorkerMethods.Hello,
            RequestId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = Protocol.Version
        };

        await _process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, _json)).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Worker exited during handshake.");
        var response = JsonSerializer.Deserialize<WorkerResponse>(line, _json)
            ?? throw new IOException("Worker returned an invalid handshake response.");
        if (response.RequestId != request.RequestId || response.ProtocolVersion != Protocol.Version)
            throw new IOException("Worker handshake identity/protocol mismatch.");
        return response;
    }

    private async Task PumpStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                _logger.LogWarning("TIA worker: {Line}", line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TIA worker stderr pump stopped.");
        }
    }

    private void TerminateWorker()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: false);
        }
        catch { }
        _process.Dispose();
        _process = null;
        _stderrPump = null;
    }

    public void Dispose()
    {
        TerminateWorker();
        _gate.Dispose();
        _sessionLock.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
