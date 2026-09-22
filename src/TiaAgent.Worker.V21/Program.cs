using System.Text.Json;
using TiaAgent.Contracts;
using TiaAgent.Worker.V21.Openness;

namespace TiaAgent.Worker.V21;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static TiaSession? _session;
    private static TiaSession Session => _session ?? throw new InvalidOperationException("Worker session is not initialized.");
    private static WorkerAccess _access;
    private static readonly ExtendedOperations Extended = new ExtendedOperations();
    private static CompileReportDto? LastCompile;

    private static void Main(string[] args)
    {
        AssemblyResolver.Register();
        _session = new TiaSession();
        _access = WorkerAccessParser.Parse(args);
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Error.WriteLine("TiaAgent V21 worker access: " + _access.ToString().ToUpperInvariant());

        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            var response = Handle(line);
            Console.WriteLine(JsonSerializer.Serialize(response, Json));
            Console.Out.Flush();
        }
        _session.Dispose();
    }

    private static WorkerResponse Handle(string line)
    {
        WorkerRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<WorkerRequest>(line, Json)
                ?? throw new InvalidOperationException("Request was empty.");

            if (!string.Equals(request.ProtocolVersion, Protocol.Version, StringComparison.Ordinal))
                return Fail(request, "protocol_error", $"Protocol mismatch. Worker={Protocol.Version}, host={request.ProtocolVersion}.");

            if (request.Method == WorkerMethods.Hello)
                return Ok(request, new { worker = "TiaAgent.Worker.V21", version = Protocol.Version });
            if (request.Method == "get_compile_messages")
                return LastCompile == null ? Fail(request, "no_compile_result", "No explicit compile has completed in this worker session.") : Ok(request, new { report = LastCompile, historical = true });
            if (request.Method == "get_online_state" || request.Method == "connect_to_device" || request.Method == "disconnect_from_device")
            {
                if (_access < WorkerAccess.Online) return Fail(request, "access_denied", "Requires --access online or control.");
                var entry = ObjectCatalog.Resolve(Session.RequireProject(request.ProjectPath), request.Path ?? "");
                var item = entry.Value as Siemens.Engineering.HW.DeviceItem ?? throw new ArgumentException("An exact CPU device-item path is required.");
                var online = item.GetService<Siemens.Engineering.Online.OnlineProvider>() ?? throw new InvalidOperationException("No OnlineProvider on target item.");
                if (request.Method == "connect_to_device") online.GoOnline();
                else if (request.Method == "disconnect_from_device") online.GoOffline();
                return Ok(request, new { path = entry.Path, connectionState = online.State.ToString(), downloaded = false, operatingMode = "not queried" });
            }

            if (ExtendedOperations.ReadMethods.Contains(request.Method)) return Ok(request, Extended.Execute(Session, request));
            if (ExtendedOperations.WriteMethods.Contains(request.Method))
                return RequireEngineering(request, r => Ok(r, Extended.Execute(Session, r)));

            return request.Method switch
            {
                WorkerMethods.GetProjectStatus => GetStatus(request),
                WorkerMethods.BrowseProjectTree => BrowseTree(request),
                WorkerMethods.GetBlockSource => GetBlockSource(request),
                WorkerMethods.UpdateBlockSource => RequireEngineering(request, UpdateBlockSource),
                WorkerMethods.Compile => RequireEngineering(request, Compile),
                _ => Fail(request, "unknown_method", "Unsupported worker method: " + request.Method)
            };
        }
        catch (Exception ex)
        {
            return Fail(request, "worker_error", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static WorkerResponse GetStatus(WorkerRequest request)
    {
        var status = Session.GetStatus(request.ProjectPath);
        var project = status.Project;
        return Ok(request, new ProjectStatusDto
        {
            Connected = status.Connected,
            PortalProcessId = status.PortalProcessId,
            ProjectName = project?.Name,
            ProjectPath = project?.Path?.FullName,
            TiaVersion = "V21"
        });
    }

    private static WorkerResponse BrowseTree(WorkerRequest request)
        => Ok(request, ProjectTreeService.Read(Session.RequireProject(request.ProjectPath)));

    private static WorkerResponse GetBlockSource(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
            return Fail(request, "validation_error", "blockPath is required.");
        return Ok(request, BlockSourceService.Export(Session.RequireProject(request.ProjectPath), request.BlockPath!));
    }

    private static WorkerResponse UpdateBlockSource(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath) || request.Source is null)
            return Fail(request, "validation_error", "blockPath and source are required.");

        using var exclusive = Session.ExclusiveAccess();
        var project = Session.RequireProject(request.ProjectPath);
        var current = BlockSourceService.Export(project, request.BlockPath!);
        if (string.IsNullOrEmpty(request.ExpectedSha256) || current.Sha256 != request.ExpectedSha256)
            return Fail(request, "concurrency_conflict", "Worker source hash differs from preview; no write attempted.");
        return Ok(request, BlockSourceService.Update(
            project,
            request.BlockPath!,
            request.Source,
            request.SaveAfterSuccess, exclusive));
    }

    private static WorkerResponse Compile(WorkerRequest request)
    {
        LastCompile = CompileService.Compile(Session.RequireProject(request.ProjectPath), request.PlcName, request.BlockPath);
        return Ok(request, LastCompile);
    }

    private static WorkerResponse RequireEngineering(WorkerRequest request, Func<WorkerRequest, WorkerResponse> action)
        => _access < WorkerAccess.Engineering
            ? Fail(request, "access_denied", "This operation requires --access engineering or higher.")
            : action(request);

    private static WorkerResponse Ok<T>(WorkerRequest request, T payload)
        => new()
        {
            RequestId = request.RequestId,
            ProtocolVersion = Protocol.Version,
            Success = true,
            PayloadJson = JsonSerializer.Serialize(payload, Json)
        };

    private static WorkerResponse Fail(WorkerRequest? request, string code, string message)
        => new()
        {
            RequestId = request?.RequestId ?? string.Empty,
            ProtocolVersion = Protocol.Version,
            Success = false,
            ErrorCode = code,
            Error = message
        };
}
