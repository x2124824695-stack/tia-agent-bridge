using System.Security.Cryptography;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.ExternalSources;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal static class ObjectSourceService
{
    public static string Hash(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    public static BlockSourceDto Export(CatalogEntry e, string format)
    {
        if (format != "xml" && format != "source") throw new ArgumentException("format must be xml or source.");
        using var temp = new TemporaryDirectory();
        var extension = "xml";
        if (format == "source")
        {
            extension = e.Value is PlcType ? "udt" : e.Value is GlobalDB ? "db" :
                e.Value is PlcBlock b && b.ProgrammingLanguage.ToString() == "SCL" ? "scl" :
                e.Value is PlcBlock a && a.ProgrammingLanguage.ToString() == "STL" ? "awl" :
                throw new InvalidOperationException("No textual source for this object. Use XML for graphical blocks/tag tables.");
        }
        var file = new FileInfo(Path.Combine(temp.Path, "object." + extension));
        if (format == "source")
            e.Plc!.Software.ExternalSourceGroup.GenerateSource(new[] { (IGenerateSource)e.Value }, file, GenerateOptions.None);
        else if (e.Value is PlcBlock block) block.Export(file, ExportOptions.WithDefaults);
        else if (e.Value is PlcType type) type.Export(file, ExportOptions.WithDefaults);
        else if (e.Value is PlcTagTable table) table.Export(file, ExportOptions.WithDefaults);
        else throw new InvalidOperationException("Only blocks, PLC types and tag tables support XML export.");
        if (!File.Exists(file.FullName)) throw new IOException("Openness returned without producing a source file.");
        var content = File.ReadAllText(file.FullName).TrimStart('\uFEFF');
        return new BlockSourceDto { BlockPath = e.Path, Format = extension, Content = content, Sha256 = Hash(content) };
    }

    public static SourceBatchDto Batch(Project project, int limit, int offset)
    {
        if (limit < 1 || limit > 200 || offset < 0) throw new ArgumentException("limit must be 1..200 and offset >= 0.");
        var entries = ObjectCatalog.Enumerate(project).Where(e => e.Value is PlcBlock || e.Value is PlcType)
            .OrderBy(e => e.Path, StringComparer.Ordinal).Skip(offset).Take(limit + 1).ToList();
        var result = new SourceBatchDto { Complete = offset == 0 && entries.Count <= limit && !ObjectCatalog.HasSoftwareUnits(project), NextOffset = offset };
        if (ObjectCatalog.HasSoftwareUnits(project)) result.ReadErrors.Add("Software Units are not included.");
        var characters = 0;
        foreach (var e in entries.Take(limit))
        {
            try
            {
                var source = Export(e, "source");
                if (characters + source.Content.Length > 2000000)
                {
                    result.Complete = false;
                    if (result.NextOffset == offset) { result.ReadErrors.Add(e.Path + ": source exceeds page size; use export_object."); result.NextOffset++; }
                    break;
                }
                characters += source.Content.Length;
                result.Sources.Add(source);
            }
            catch (Exception ex) { result.ReadErrors.Add(e.Path + ": " + ex.Message); result.Complete = false; }
            result.NextOffset++;
        }
        return result;
    }

    public static SearchResultDto Search(Project project, string query, int limit, bool matchCase)
    {
        if (string.IsNullOrEmpty(query) || query.Length > 1024 || limit < 1 || limit > 2000)
            throw new ArgumentException("query must have 1..1024 characters; limit must be 1..2000.");
        var result = new SearchResultDto { Complete = !ObjectCatalog.HasSoftwareUnits(project) };
        if (!result.Complete) result.ReadErrors.Add("Software Units are not included.");
        var count = 0;
        foreach (var e in ObjectCatalog.Enumerate(project).Where(e => e.Value is PlcBlock || e.Value is PlcType))
        {
            if (++count > 2000) { result.Complete = false; break; }
            try
            {
                var lines = Export(e, "source").Content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].IndexOf(query, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (result.Hits.Count == limit) { result.Complete = false; return result; }
                        result.Hits.Add(new SearchHitDto { Path = e.Path, Line = i + 1, Text = lines[i].Length <= 2000 ? lines[i] : lines[i].Substring(0, 2000) });
                    }
            }
            catch (Exception ex) { result.ReadErrors.Add(e.Path + ": " + ex.Message); result.Complete = false; }
        }
        return result;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tia-agent-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch (Exception ex) { Console.Error.WriteLine("Temporary directory cleanup failed: " + ex.Message); }
    }
}
