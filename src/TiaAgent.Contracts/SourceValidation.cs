using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Linq;

namespace TiaAgent.Contracts;

public static class SourceValidation
{
    public static void SingleDeclaration(string source, string expectedName, string format, string? expectedKind = null)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 2000000) throw new ArgumentException("Source must contain 1..2000000 characters.");
        var clean = MaskCommentsAndStrings(source);
        var matches = Regex.Matches(clean, @"(?i)(?<![\p{L}\p{N}_])(FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)\s+(?:""([^""\r\n]+)""|([\p{L}_][\p{L}\p{N}_]*))");
        if (matches.Count != 1) throw new ArgumentException("Exactly one top-level declaration is required; multi-object sources are rejected.");
        var m = matches[0];
        var name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        if (!string.Equals(name, expectedName, StringComparison.Ordinal)) throw new ArgumentException("The declaration name must match the target object exactly.");
        var kind = m.Groups[1].Value.ToUpperInvariant();
        if (expectedKind != null && kind != expectedKind) throw new ArgumentException("Changing the existing block kind is not permitted.");
        if (!Regex.IsMatch(clean, @"(?i)\bEND_" + kind + @"\s*;?\s*$")) throw new ArgumentException("Source must end at its single declaration's END marker.");
        if ((format == "udt" && kind != "TYPE") || (format == "db" && kind != "DATA_BLOCK") ||
            ((format == "scl" || format == "awl") && (kind == "TYPE" || kind == "DATA_BLOCK")))
            throw new ArgumentException("Declaration kind does not match the source extension.");
    }

    // Preserve quoted Siemens identifiers; mask comments and IEC string literals so
    // a target name in a comment or assignment cannot authorize another declaration.
    private static string MaskCommentsAndStrings(string source)
    {
        var b = new StringBuilder(source.Length);
        int state = 0, depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i], n = i + 1 < source.Length ? source[i + 1] : '\0';
            if (state == 0)
            {
                if (c == '"') state = 4;
                else if (c == '\'') { state = 3; b.Append(' '); continue; }
                else if (c == '/' && n == '/') { state = 1; b.Append("  "); i++; continue; }
                else if (c == '(' && n == '*') { state = 2; depth = 1; b.Append("  "); i++; continue; }
                b.Append(c);
            }
            else if (state == 4) { b.Append(c); if (c == '"') state = 0; }
            else
            {
                b.Append(c == '\r' || c == '\n' ? c : ' ');
                if (state == 1 && (c == '\r' || c == '\n')) state = 0;
                else if (state == 2 && c == '(' && n == '*') { depth++; b.Append(' '); i++; }
                else if (state == 2 && c == '*' && n == ')') { if (--depth == 0) state = 0; b.Append(' '); i++; }
                else if (state == 3 && c == '$' && n != '\0') { b.Append(' '); i++; }
                else if (state == 3 && c == '\'') { if (n == '\'') { b.Append(' '); i++; } else state = 0; }
            }
        }
        if (state > 1) throw new ArgumentException("Unterminated source comment, string, or identifier.");
        return b.ToString();
    }

    public static XDocument ParseXml(string content)
    {
        if (content.Length > 4000000) throw new ArgumentException("XML exceeds the 4 MB character limit.");
        using var input = new System.IO.StringReader(content);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4000000 });
        return XDocument.Load(reader);
    }

    public static void SingleXmlObject(string content, string name, string category)
    {
        var doc = ParseXml(content);
        if (doc.Root?.Name.LocalName != "Document") throw new ArgumentException("Expected a Siemens XML Document.");
        var objects = doc.Root.Elements().Where(e => e.Name.LocalName.StartsWith("SW.", StringComparison.Ordinal)).ToList();
        if (objects.Count != 1) throw new ArgumentException("XML must contain exactly one top-level engineering object.");
        var obj = objects[0];
        string prefix = category == "Blocks" ? "SW.Blocks." : category == "Types" ? "SW.Types." : "SW.Tags.PlcTagTable";
        if (!obj.Name.LocalName.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("XML object kind does not match target group.");
        var declaredName = obj.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
        if (declaredName != name) throw new ArgumentException("XML object name must match target exactly.");
    }

    public static string CanonicalXml(string content)
    {
        var doc = ParseXml(content);
        doc.Root?.Elements().Where(e => e.Name.LocalName == "DocumentInfo").Remove();
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
