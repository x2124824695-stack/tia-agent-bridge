using System.Collections.Generic;

namespace TiaAgent.Contracts;

// Siemens names and paths deliberately replace CODESYS POU/DUT/GVL semantics.
public sealed class EngineeringChange
{
    public string Action { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Name { get; set; }
    public string? Content { get; set; }
    public string? Format { get; set; }
    public string? Destination { get; set; }
    public string? DataType { get; set; }
    public string? Address { get; set; }
    public string? Attribute { get; set; }
    public string? Value { get; set; }
    public List<EngineeringSourceChange>? Sources { get; set; }
}

// Ordered, explicit source targets; one declaration per item, one PLC per batch.
public sealed class EngineeringSourceChange
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class ObjectInfoDto
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string?> Attributes { get; set; } = new Dictionary<string, string?>();
    public List<string> Errors { get; set; } = new List<string>();
}

public sealed class ObjectListDto
{
    public List<ObjectInfoDto> Objects { get; set; } = new List<ObjectInfoDto>();
    public bool Complete { get; set; } = true;
    public List<string> Warnings { get; set; } = new List<string>();
}

public sealed class SourceBatchDto
{
    public List<BlockSourceDto> Sources { get; set; } = new List<BlockSourceDto>();
    public List<string> ReadErrors { get; set; } = new List<string>();
    public bool Complete { get; set; } = true;
    public int NextOffset { get; set; }
}

public sealed class SearchHitDto
{
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public string Text { get; set; } = "";
}

public sealed class SearchResultDto
{
    public List<SearchHitDto> Hits { get; set; } = new List<SearchHitDto>();
    public bool Complete { get; set; }
    public List<string> ReadErrors { get; set; } = new List<string>();
    public string Semantics { get; set; } = "Literal source-text matches; not semantic cross-references.";
}
