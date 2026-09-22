using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaAgent.Contracts;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host.Tools;

public sealed record ToolConfiguration(AccessMode Access);

[McpServerToolType]
public sealed class TiaCapabilityTools
{
    [McpServerTool(Name = "get_command_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Inspect a timed-out/disconnected worker request without replaying it. Unknown results remain locked across host restarts.")]
    public static CallToolResult Status(WorkerProcessClient worker)
        => StructuredToolResult.Create(new ToolEnvelope<object> { Success = true, Data = worker.GetCommandStatus() });

    [McpServerTool(Name = "recover_session", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Acknowledge a completed uncertain request after inspecting get_command_status. Does not replay requests or unlock unknown results.")]
    public static async Task<CallToolResult> Recover(WorkerProcessClient worker, bool acknowledge)
    {
        try { return StructuredToolResult.Create(new ToolEnvelope<object> { Success = true, Data = await worker.RecoverAsync(acknowledge) }); }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return StructuredToolResult.Create(new ToolEnvelope<object> { Success = false, Error = new ToolError { Code = "recovery_refused", Message = ex.Message } }, true);
        }
    }
    public static IEnumerable<Type> EnabledTypes(AccessMode access)
    {
        yield return typeof(TiaReadTools);
        yield return typeof(TiaExtendedReadTools);
        yield return typeof(TiaCapabilityTools);
        if (access >= AccessMode.Engineering)
        {
            yield return typeof(TiaEngineeringTools);
            yield return typeof(TiaExtendedEngineeringTools);
        }
        if (access >= AccessMode.Online) yield return typeof(TiaOnlineTools);
    }

    [McpServerTool(Name = "get_capabilities", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Return tools actually registered for this access mode, engineering actions and explicit platform gaps. Registration/build success is not live TIA/PLC validation.")]
    public static CallToolResult Capabilities(ToolConfiguration configuration)
    {
        var names = EnabledTypes(configuration.Access).SelectMany(t => t.GetMethods()).Select(m => m.GetCustomAttribute<McpServerToolAttribute>()).Where(a => a != null).Select(a => a!.Name).OrderBy(n => n).ToArray();
        return StructuredToolResult.Create(new ToolEnvelope<object> { Success = true, Data = new {
            version = "0.2", target = "TIA Portal V21", access = configuration.Access.ToString(), tools = names,
            engineeringActions = configuration.Access >= AccessMode.Engineering ? new[] { "create_source", "import_xml", "create_folder", "create_tag_table", "create_tag", "delete_object", "rename_object", "move_object", "set_attribute", "add_device" } : Array.Empty<string>(),
            unavailable = new[] {
                "CODESYS properties/methods/templates: not equivalent to Siemens FB/FC/OB.",
                "Online variable I/O, monitoring and independent RUN/STOP: require a CPU-specific communication backend.",
                "Download: requires interface/address and explicit callback policy; not implemented by generic engineering actions.",
                "PLCSIM selection: requires the target simulation API/version; not a CODESYS simulation boolean.",
                "Semantic rename_symbol and arbitrary Python execution: not exposed.",
                "Software Units, V20 worker and global-library installation: not implemented." },
            validation = "Compiled against installed V21 SDK; live project/PLC acceptance is required." } });
    }
}
