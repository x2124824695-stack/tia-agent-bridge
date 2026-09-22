using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace TiaAgent.Host;

public static class StructuredToolResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static CallToolResult Create<T>(T value, bool isError = false)
    {
        var text = JsonSerializer.Serialize(value, JsonOptions);
        using var doc = JsonDocument.Parse(text);
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
            StructuredContent = doc.RootElement.Clone(),
            IsError = isError
        };
    }
}
