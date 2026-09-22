using System.Text;

namespace TiaAgent.Host.Safety;

public static class DiffService
{
    private const int ContextLines = 4;
    private const int MaxChars = 16000;

    public static string CreateBoundedDiff(string oldText, string newText)
    {
        var oldLines = Normalize(oldText);
        var newLines = Normalize(newText);

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix])
            suffix++;

        if (prefix == oldLines.Length && prefix == newLines.Length)
            return "(no changes)";

        var sb = new StringBuilder();
        var contextStart = Math.Max(0, prefix - ContextLines);
        for (var i = contextStart; i < prefix; i++)
            Append(sb, "  ", oldLines[i]);

        var oldEnd = oldLines.Length - suffix;
        for (var i = prefix; i < oldEnd; i++)
            Append(sb, "- ", oldLines[i]);

        var newEnd = newLines.Length - suffix;
        for (var i = prefix; i < newEnd; i++)
            Append(sb, "+ ", newLines[i]);

        var suffixEnd = Math.Min(oldLines.Length, oldEnd + ContextLines);
        for (var i = oldEnd; i < suffixEnd; i++)
            Append(sb, "  ", oldLines[i]);

        if (sb.Length > MaxChars)
            return sb.ToString(0, MaxChars) + Environment.NewLine + "... diff truncated ...";

        return sb.ToString();
    }

    private static string[] Normalize(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static void Append(StringBuilder sb, string prefix, string line)
        => sb.Append(prefix).AppendLine(line);
}
