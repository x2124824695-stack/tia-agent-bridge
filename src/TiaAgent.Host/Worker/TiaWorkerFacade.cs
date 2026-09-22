using System.Text.Json;
using TiaAgent.Contracts;

namespace TiaAgent.Host.Worker;

public sealed class TiaWorkerFacade
{
    private readonly WorkerProcessClient _client;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public TiaWorkerFacade(WorkerProcessClient client) => _client = client;

    public Task<WorkerCall<ProjectStatusDto>> GetProjectStatusAsync(string? projectPath = null, CancellationToken ct = default)
        => SendAsync<ProjectStatusDto>(new WorkerRequest { Method = WorkerMethods.GetProjectStatus, ProjectPath = projectPath }, ct);

    public Task<WorkerCall<ProjectTreeDto>> BrowseProjectTreeAsync(string? projectPath = null, CancellationToken ct = default)
        => SendAsync<ProjectTreeDto>(new WorkerRequest { Method = WorkerMethods.BrowseProjectTree, ProjectPath = projectPath }, ct);

    public Task<WorkerCall<BlockSourceDto>> GetBlockSourceAsync(string blockPath, string? projectPath = null, CancellationToken ct = default)
        => SendAsync<BlockSourceDto>(new WorkerRequest { Method = WorkerMethods.GetBlockSource, BlockPath = blockPath, ProjectPath = projectPath }, ct);

    public Task<WorkerCall<BlockUpdateResultDto>> UpdateBlockSourceAsync(string blockPath, string source, bool saveAfterSuccess, string? projectPath = null, CancellationToken ct = default, string? expectedSha256 = null)
        => SendAsync<BlockUpdateResultDto>(new WorkerRequest
        {
            Method = WorkerMethods.UpdateBlockSource,
            BlockPath = blockPath,
            Source = source,
            ExpectedSha256 = expectedSha256,
            SaveAfterSuccess = saveAfterSuccess,
            ProjectPath = projectPath
        }, ct);

    public Task<WorkerCall<CompileReportDto>> CompileAsync(string? plcName, string? blockPath, string? projectPath = null, CancellationToken ct = default)
        => SendAsync<CompileReportDto>(new WorkerRequest
        {
            Method = WorkerMethods.Compile,
            PlcName = plcName,
            BlockPath = blockPath,
            ProjectPath = projectPath
        }, ct);

    public async Task<WorkerCall<T>> SendAsync<T>(WorkerRequest request, CancellationToken ct = default)
    {
        WorkerResponse response;
        try { response = await _client.SendAsync(request, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is TimeoutException || ex is JsonException)
        {
            return WorkerCall<T>.Failure("transport_error", ex.Message + " Inspect get_command_status before retrying a dispatched operation.", Array.Empty<string>());
        }
        if (!response.Success)
            return WorkerCall<T>.Failure(response.ErrorCode ?? "worker_error", response.Error ?? "Worker operation failed.", response.Warnings);

        if (string.IsNullOrWhiteSpace(response.PayloadJson))
            return WorkerCall<T>.Failure("protocol_error", "Worker returned no payload.", response.Warnings);

        try
        {
            var payload = JsonSerializer.Deserialize<T>(response.PayloadJson, _json);
            return payload is null
                ? WorkerCall<T>.Failure("protocol_error", "Worker payload was empty.", response.Warnings)
                : WorkerCall<T>.Ok(payload, response.Warnings);
        }
        catch (JsonException ex)
        {
            return WorkerCall<T>.Failure("protocol_error", "Worker payload could not be decoded: " + ex.Message, response.Warnings);
        }
    }
}

public sealed class WorkerCall<T>
{
    public bool Success { get; private init; }
    public T? Value { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? Error { get; private init; }
    public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();

    public static WorkerCall<T> Ok(T value, IReadOnlyList<string> warnings) => new() { Success = true, Value = value, Warnings = warnings };
    public static WorkerCall<T> Failure(string code, string error, IReadOnlyList<string> warnings) => new() { Success = false, ErrorCode = code, Error = error, Warnings = warnings };
}
