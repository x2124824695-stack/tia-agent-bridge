using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaAgent.Contracts;
using TiaAgent.Host.Safety;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host.Tools;

[McpServerToolType]
public sealed class TiaEngineeringTools
{

    [McpServerTool(Name = "preview_block_update", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<BlockUpdatePreviewDto>))]
    [Description("Preview an SCL/global-DB source update. Returns current/proposed hashes, a bounded diff, and a single-use 10-minute token.")]
    public static async Task<CallToolResult> PreviewBlockUpdate(
        TiaWorkerFacade worker,
        PreviewTokenService tokens,
        [Description("Deterministic block path such as PLC_1/Blocks/Motion/FB_Axis.")] string blockPath,
        [Description("Complete replacement Siemens external-source text for the existing block.")] string newSource,
        [Description("Optional absolute .ap21 path used only to verify the already-open project.")] string? projectPath = null)
    {
        var current = await worker.GetBlockSourceAsync(blockPath, projectPath).ConfigureAwait(false);
        if (!current.Success)
            return TiaReadTools.Render(current);

        var currentSource = current.Value!;
        var proposedHash = Hashing.Sha256(newSource);
        var token = tokens.Create(currentSource.ProjectIdentity + "|" + blockPath, currentSource.Sha256, proposedHash);
        var preview = new BlockUpdatePreviewDto
        {
            BlockPath = blockPath,
            CurrentSha256 = currentSource.Sha256,
            ProposedSha256 = proposedHash,
            PreviewToken = token.Value,
            ExpiresAtUtc = token.ExpiresAt.UtcDateTime.ToString("O"),
            Diff = DiffService.CreateBoundedDiff(currentSource.Content, newSource)
        };

        return StructuredToolResult.Create(new ToolEnvelope<BlockUpdatePreviewDto>
        {
            Success = true,
            Data = preview,
            Warnings = current.Warnings.ToList()
        });
    }

    [McpServerTool(Name = "apply_block_update", ReadOnly = false, Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<BlockUpdateResultDto>))]
    [Description("Apply an update only after preview. Revalidates the current source hash to prevent overwriting concurrent manual changes, then imports, compiles, re-exports, and optionally saves.")]
    public static async Task<CallToolResult> ApplyBlockUpdate(
        TiaWorkerFacade worker,
        PreviewTokenService tokens,
        [Description("Deterministic block path used during preview.")] string blockPath,
        [Description("Complete replacement source, byte-for-byte equivalent to the source previewed.")] string newSource,
        [Description("Current SHA-256 returned by preview_block_update.")] string expectedCurrentSha256,
        [Description("Single-use token returned by preview_block_update.")] string previewToken,
        [Description("Save the TIA project after a successful compile and re-export. Defaults false.")] bool saveAfterSuccess = false,
        [Description("Optional absolute .ap21 path used only to verify the already-open project.")] string? projectPath = null)
    {
        var current = await worker.GetBlockSourceAsync(blockPath, projectPath).ConfigureAwait(false);
        if (!current.Success)
            return TiaReadTools.Render(current);

        if (!string.Equals(current.Value!.Sha256, expectedCurrentSha256, StringComparison.Ordinal))
        {
            return StructuredToolResult.Create(new ToolEnvelope<BlockUpdateResultDto>
            {
                Success = false,
                Error = new ToolError
                {
                    Code = "concurrency_conflict",
                    Message = "The block changed after preview. Re-read and preview again; no write was attempted."
                }
            }, true);
        }

        var proposedHash = Hashing.Sha256(newSource);
        try
        {
            tokens.Consume(previewToken, current.Value!.ProjectIdentity + "|" + blockPath, expectedCurrentSha256, proposedHash);
        }
        catch (InvalidOperationException ex)
        {
            return StructuredToolResult.Create(new ToolEnvelope<BlockUpdateResultDto>
            {
                Success = false,
                Error = new ToolError { Code = "invalid_preview_token", Message = ex.Message }
            }, true);
        }

        var write = await worker.UpdateBlockSourceAsync(blockPath, newSource, saveAfterSuccess, current.Value!.ProjectIdentity, expectedSha256: expectedCurrentSha256).ConfigureAwait(false);
        if (!write.Success)
            return TiaReadTools.Render(write);

        var applied = write.Value!;
        var ok = applied.Applied && CompileOutcome.Succeeded(applied.Compile) && applied.SaveError == null;
        return StructuredToolResult.Create(new ToolEnvelope<BlockUpdateResultDto>
        {
            Success = ok,
            Data = applied,
            Warnings = write.Warnings.ToList(),
            Error = ok ? null : new ToolError
            {
                Code = applied.SaveError != null ? "save_failed" : "update_failed",
                Message = applied.SaveError != null ? "Changes applied but save failed: " + applied.SaveError : applied.Failure ?? (applied.RollbackSucceeded
                    ? "The proposed source failed compilation and the original source was restored."
                    : "The proposed source failed compilation. Inspect the returned rollback status before retrying.")
            }
        }, !ok);
    }

    [McpServerTool(Name = "compile", ReadOnly = false, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolEnvelope<CompileReportDto>))]
    [Description("Compile one deterministic block path or an entire PLC software and return structured compiler messages.")]
    public static async Task<CallToolResult> Compile(
        TiaWorkerFacade worker,
        [Description("Optional PLC name. Required when blockPath is omitted if the project contains multiple PLCs.")] string? plcName = null,
        [Description("Optional deterministic block path. When supplied, compiles that block.")] string? blockPath = null,
        [Description("Optional absolute .ap21 path used only to verify the already-open project.")] string? projectPath = null)
    {
        var call = await worker.CompileAsync(plcName, blockPath, projectPath).ConfigureAwait(false);
        if (!call.Success)
            return TiaReadTools.Render(call);

        var report = call.Value!;
        return StructuredToolResult.Create(new ToolEnvelope<CompileReportDto>
        {
            Success = CompileOutcome.Succeeded(report),
            Data = report,
            Warnings = call.Warnings.ToList(),
            Error = CompileOutcome.Succeeded(report) ? null : new ToolError { Code = "compile_failed", Message = $"Compilation returned state {report.State}, {report.ErrorCount} error(s)." }
        }, !CompileOutcome.Succeeded(report));
    }
}
