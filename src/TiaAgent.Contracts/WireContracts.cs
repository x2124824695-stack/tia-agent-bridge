using System.Collections.Generic;

namespace TiaAgent.Contracts;

public sealed class WorkerRequest
{
    public string ProtocolVersion { get; set; } = Protocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public string? PlcName { get; set; }
    public string? BlockPath { get; set; }
    public string? Source { get; set; }
    public bool SaveAfterSuccess { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? Query { get; set; }
    public string? Format { get; set; }
    public int Limit { get; set; } = 200;
    public int Offset { get; set; }
    public bool MatchCase { get; set; }
    public EngineeringChange? Change { get; set; }
    public string? PreviewToken { get; set; }
}

public sealed class WorkerResponse
{
    public string ProtocolVersion { get; set; } = Protocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string? PayloadJson { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
}
