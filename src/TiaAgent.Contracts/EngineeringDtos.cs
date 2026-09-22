using System.Collections.Generic;

namespace TiaAgent.Contracts;

public sealed class ProjectStatusDto
{
    public bool Connected { get; set; }
    public int? PortalProcessId { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectPath { get; set; }
    public string TiaVersion { get; set; } = "V21";
}

public sealed class ProjectTreeDto
{
    public string? ProjectName { get; set; }
    public string? ProjectPath { get; set; }
    public List<PlcTreeDto> Plcs { get; set; } = new List<PlcTreeDto>();
}

public sealed class PlcTreeDto
{
    public string Name { get; set; } = string.Empty;
    public List<BlockInfoDto> Blocks { get; set; } = new List<BlockInfoDto>();
}

public sealed class BlockInfoDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int? Number { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Family { get; set; }
}

public sealed class BlockSourceDto
{
    public string ProjectIdentity { get; set; } = string.Empty;
    public string BlockPath { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class CompileMessageDto
{
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class CompileReportDto
{
    public string Scope { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<CompileMessageDto> Messages { get; set; } = new List<CompileMessageDto>();
}

public sealed class BlockUpdateResultDto
{
    public string? Failure { get; set; }
    public string? SaveError { get; set; }
    public string BlockPath { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public string? NewSha256 { get; set; }
    public CompileReportDto Compile { get; set; } = new CompileReportDto();
    public bool RollbackAttempted { get; set; }
    public bool RollbackSucceeded { get; set; }
    public string? RollbackError { get; set; }
    public bool Saved { get; set; }
}

public sealed class ToolEnvelope<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
    public ToolError? Error { get; set; }
}

public sealed class ToolError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class BlockUpdatePreviewDto
{
    public string BlockPath { get; set; } = string.Empty;
    public string CurrentSha256 { get; set; } = string.Empty;
    public string ProposedSha256 { get; set; } = string.Empty;
    public string PreviewToken { get; set; } = string.Empty;
    public string ExpiresAtUtc { get; set; } = string.Empty;
    public string Diff { get; set; } = string.Empty;
}
