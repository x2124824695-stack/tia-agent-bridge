using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaAgent.Contracts;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host.Tools;

[McpServerToolType]
public sealed class TiaOnlineTools
{
    [McpServerTool(Name = "get_online_state", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Read the selected CPU device item's Openness connection state. This is not PLC RUN/STOP state. Requires --access online/control.")]
    public static Task<CallToolResult> State(TiaWorkerFacade worker, string path, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "get_online_state", Path = path, ProjectPath = projectPath });

    [McpServerTool(Name = "connect_to_device", ReadOnly = false, Destructive = false, OpenWorld = true)]
    [Description("Go online to the exact CPU device-item path using its TIA-configured connection. No implicit download, RUN/STOP, password change or fallback to another device. Configure and verify the target in TIA first.")]
    public static Task<CallToolResult> Connect(TiaWorkerFacade worker, string path, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "connect_to_device", Path = path, ProjectPath = projectPath });

    [McpServerTool(Name = "disconnect_from_device", ReadOnly = false, Destructive = false, OpenWorld = true)]
    [Description("Go offline for the exact CPU device-item path. Does not stop the PLC or close TIA.")]
    public static Task<CallToolResult> Disconnect(TiaWorkerFacade worker, string path, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "disconnect_from_device", Path = path, ProjectPath = projectPath });
}
