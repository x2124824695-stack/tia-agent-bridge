using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaAgent.Contracts;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host.Tools;

[McpServerToolType]
public sealed class TiaReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<ProjectStatusDto>))]
    [Description("Attach to the uniquely identifiable running TIA Portal V21 instance and return the active project status.")]
    public static async Task<CallToolResult> GetProjectStatus(
        TiaWorkerFacade worker,
        [Description("Optional absolute .ap21 path used only to select/verify an already-open project; v0.1 never opens a project from a read call.")] string? projectPath = null)
    {
        var call = await worker.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
        return Render(call);
    }

    [McpServerTool(Name = "browse_project_tree", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<ProjectTreeDto>))]
    [Description("Return PLCs and deterministic PLC block paths, including block language and available header author/version/family metadata.")]
    public static async Task<CallToolResult> BrowseProjectTree(
        TiaWorkerFacade worker,
        [Description("Optional absolute .ap21 path used only to verify the already-open project.")] string? projectPath = null)
    {
        var call = await worker.BrowseProjectTreeAsync(projectPath).ConfigureAwait(false);
        return Render(call);
    }

    [McpServerTool(Name = "get_block_source", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<BlockSourceDto>))]
    [Description("Export one supported PLC block as Siemens external-source text and return its SHA-256 hash.")]
    public static async Task<CallToolResult> GetBlockSource(
        TiaWorkerFacade worker,
        [Description("Deterministic path such as PLC_1/Blocks/Motion/FB_Axis.")] string blockPath,
        [Description("Optional absolute .ap21 path used only to verify the already-open project.")] string? projectPath = null)
    {
        var call = await worker.GetBlockSourceAsync(blockPath, projectPath).ConfigureAwait(false);
        return Render(call);
    }

    internal static CallToolResult Render<T>(WorkerCall<T> call)
    {
        var envelope = new ToolEnvelope<T>
        {
            Success = call.Success,
            Data = call.Value,
            Warnings = call.Warnings.ToList(),
            Error = call.Success ? null : new ToolError { Code = call.ErrorCode ?? "worker_error", Message = call.Error ?? "Worker operation failed." }
        };
        return StructuredToolResult.Create(envelope, !call.Success);
    }
}
