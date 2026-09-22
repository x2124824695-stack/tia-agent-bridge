using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.ExternalSources;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal sealed class EngineeringChangeService
{
    private sealed class Pending
    {
        public EngineeringChange Change = null!;
        public string ProjectPath = "";
        public string Snapshot = "";
        public Project Project = null!;
        public DateTime Expires;
    }
    private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
    public static readonly string[] Actions = { "write_source_batch", "create_source", "import_xml", "create_folder", "create_tag_table", "create_tag", "delete_object", "rename_object", "move_object", "set_attribute", "add_device" };

    public object Preview(TiaSession session, Project project, EngineeringChange change)
    {
        if (!Actions.Contains(change.Action)) throw new ArgumentException("Unsupported action. See get_capabilities.");
        using var access = session.ExclusiveAccess();
        Validate(project, change);
        var snapshot = Snapshot(project, change);
        foreach (var expired in _pending.Where(p => p.Value.Expires <= DateTime.UtcNow).Select(p => p.Key).ToList()) _pending.Remove(expired);
        if (_pending.Count >= 100) throw new InvalidOperationException("Too many outstanding previews; apply one or wait for expiry.");
        var bytes = new byte[24];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        var token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        var expires = DateTime.UtcNow.AddMinutes(10);
        _pending[token] = new Pending { Change = change, ProjectPath = project.Path.FullName, Project = project, Snapshot = snapshot, Expires = expires };
        return new { previewToken = token, expiresAtUtc = expires, projectPath = project.Path.FullName, change, currentSha256 = snapshot,
            warnings = new[] { "Writes use an Openness transaction; compilation must be requested separately. No implicit project save, PLC download, or RUN/STOP.", "Rename changes an object name; it is not a semantic symbol refactor." } };
    }

    public object Apply(TiaSession session, Project project, string token)
    {
        if (!_pending.TryGetValue(token, out var pending)) throw new InvalidOperationException("Unknown, consumed or expired preview token.");
        _pending.Remove(token);
        if (pending.Expires <= DateTime.UtcNow || pending.ProjectPath != project.Path.FullName || !pending.Project.Equals(project))
            throw new InvalidOperationException("Preview expired or the project session changed.");
        using var access = session.ExclusiveAccess();
        Validate(project, pending.Change);
        if (Snapshot(project, pending.Change) != pending.Snapshot) throw new InvalidOperationException("concurrency_conflict: target changed since preview; no write attempted.");
        object result;
        using (var transaction = access.Transaction(project, "TiaAgent " + pending.Change.Action))
        {
            result = Execute(project, pending.Change);
            transaction.CommitOnDispose();
        }
        return new { applied = true, saved = false, compiled = false, result };
    }

    private static void Name(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name!.Any(c => char.IsControl(c) || c == '/' || c == '\\' || c == '"'))
            throw new ArgumentException("A nonempty object name without slash, quote or control characters is required.");
    }

    private static void Validate(Project project, EngineeringChange c)
    {
        if (c.Action == "write_source_batch")
        {
            ValidateSourceBatch(project, c);
            return;
        }
        if (c.Action == "add_device")
        {
            Name(c.Name); Name(c.Destination);
            if (string.IsNullOrWhiteSpace(c.Value)) throw new ArgumentException("value must be the exact Siemens TypeIdentifier, not a CODESYS device ID.");
            if (project.Devices.Find(c.Name!) != null) throw new ArgumentException("Device name already exists.");
            return;
        }
        var entry = ObjectCatalog.Resolve(project, c.Path);
        if (c.Action == "create_source" || c.Action == "import_xml")
        {
            var targetPath = c.Path + "/" + ObjectCatalog.Segment(c.Name ?? "");
            if (ObjectCatalog.Enumerate(project).Any(other => other.Plc?.Software.Equals(entry.Plc?.Software) == true &&
                other.Path != targetPath && Uri.UnescapeDataString(other.Path.Split('/').Last()).Equals(c.Name, StringComparison.OrdinalIgnoreCase) &&
                ((entry.Value is PlcBlockGroup && other.Value is PlcBlock) || (entry.Value is PlcTypeGroup && other.Value is PlcType))))
                throw new ArgumentException("An object with this name exists elsewhere in the PLC. Refusing an implicit overwrite or move.");
        }
        if (c.Action.StartsWith("create_", StringComparison.Ordinal) || c.Action == "rename_object" || c.Action == "import_xml") Name(c.Name);
        if (c.Action == "create_source")
        {
            if (c.Format != "scl" && c.Format != "db" && c.Format != "udt" && c.Format != "awl") throw new ArgumentException("Source format must be scl, db, udt or awl.");
            if (!(entry.Value is PlcBlockGroup) && !(entry.Value is PlcTypeGroup)) throw new ArgumentException("Source target must be a Blocks or Types group.");
            if ((entry.Value is PlcTypeGroup) != (c.Format == "udt")) throw new ArgumentException("UDT requires a Types group; other formats require Blocks.");
            SourceValidation.SingleDeclaration(c.Content ?? "", c.Name!, c.Format!);
            EnsureAbsent(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!));
        }
        else if (c.Action == "import_xml")
        {
            var category = entry.Value is PlcBlockGroup ? "Blocks" : entry.Value is PlcTypeGroup ? "Types" : entry.Value is PlcTagTableGroup ? "Tags" : throw new ArgumentException("Import target must be a Blocks/Types/Tags group.");
            SourceValidation.SingleXmlObject(c.Content ?? "", c.Name!, category);
            var numberText = SourceValidation.ParseXml(c.Content!).Root!.Elements().First(x => x.Name.LocalName.StartsWith("SW.")).Elements().FirstOrDefault(x => x.Name.LocalName == "AttributeList")?.Elements().FirstOrDefault(x => x.Name.LocalName == "Number")?.Value;
            if (category == "Blocks" && int.TryParse(numberText, out var number))
            {
                var rootKind = SourceValidation.ParseXml(c.Content!).Root!.Elements().First(x => x.Name.LocalName.StartsWith("SW.")).Name.LocalName.Split('.').Last();
                foreach (var other in ObjectCatalog.Enumerate(project).Where(x => x.Plc?.Software.Equals(entry.Plc?.Software) == true && x.Value is PlcBlock))
                {
                    var block = (PlcBlock)other.Value;
                    bool sameKind = block.GetType().Name == rootKind || ((rootKind == "GlobalDB" || rootKind == "InstanceDB") && (block is GlobalDB || block is InstanceDB));
                    if (sameKind && block.Number == number && other.Path != c.Path + "/" + ObjectCatalog.Segment(c.Name!))
                        throw new ArgumentException("XML block number collides with another block: " + other.Path);
                }
            }
        }
        else if (c.Action == "create_folder")
        {
            if (!(entry.Value is PlcBlockGroup) && !(entry.Value is PlcTypeGroup) && !(entry.Value is PlcTagTableGroup)) throw new ArgumentException("Expected a Blocks/Types/Tags group.");
            EnsureAbsent(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!));
        }
        else if (c.Action == "create_tag_table")
        {
            if (!(entry.Value is PlcTagTableGroup)) throw new ArgumentException("Expected a Tags group.");
            EnsureAbsent(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!));
        }
        else if (c.Action == "create_tag")
        {
            if (!(entry.Value is PlcTagTable) || string.IsNullOrWhiteSpace(c.DataType) || c.Address == null) throw new ArgumentException("Expected a tag table, dataType and address (empty for unset).");
            EnsureAbsent(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!));
        }
        else if (c.Action == "delete_object")
        {
            if (!Mutable(entry.Value)) throw new ArgumentException("System objects and hardware cannot be deleted through this tool.");
            if (entry.Value is PlcBlockGroup || entry.Value is PlcTypeGroup || entry.Value is PlcTagTableGroup)
                if (ObjectCatalog.Enumerate(project).Any(e => e.Path.StartsWith(c.Path + "/", StringComparison.Ordinal))) throw new ArgumentException("Only empty folders can be deleted.");
        }
        else if (c.Action == "rename_object")
        {
            if (!Mutable(entry.Value)) throw new ArgumentException("System objects cannot be renamed.");
            EnsureAbsent(project, c.Path.Substring(0, c.Path.LastIndexOf('/') + 1) + ObjectCatalog.Segment(c.Name!));
        }
        else if (c.Action == "move_object")
        {
            var dest = ObjectCatalog.Resolve(project, c.Destination ?? "");
            if (entry.Plc == null || dest.Plc == null || !entry.Plc.Software.Equals(dest.Plc.Software)) throw new ArgumentException("Only moves within the same PLC are allowed.");
            if (!((entry.Value is PlcBlock && dest.Value is PlcBlockGroup) || (entry.Value is PlcType && dest.Value is PlcTypeGroup) || (entry.Value is PlcTagTable && dest.Value is PlcTagTableGroup))) throw new ArgumentException("Move requires a block, UDT or tag table and a matching destination group.");
            EnsureAbsent(project, dest.Path + "/" + c.Path.Split('/').Last());
        }
        else if (c.Action == "set_attribute")
        {
            if (!(entry.Value is DeviceItem) && !(entry.Value is OB) && !(entry.Value is PlcTag) && !(entry.Value is Siemens.Engineering.HW.Address) && !(entry.Value is Channel)) throw new ArgumentException("Attribute writes are limited to device items, I/O addresses/channels, OBs and PLC tags.");
            if (c.Attribute == "Name" || c.Attribute == null || c.Value == null) throw new ArgumentException("Supply an attribute and value; use rename_object to rename.");
            var info = entry.Value.GetAttributeInfos().SingleOrDefault(a => a.Name == c.Attribute);
            if (info == null || (info.AccessMode & EngineeringAttributeAccessMode.Write) == 0) throw new ArgumentException("Attribute is not writable through this Openness object.");
            ConvertValue(entry.Value.GetAttribute(c.Attribute), c.Value);
        }
    }

    private static bool Mutable(IEngineeringObject value) => value is PlcBlock || value is PlcType || value is PlcTag || value is PlcTagTable || value is PlcBlockUserGroup || value is PlcTypeUserGroup || value is PlcTagTableUserGroup;
    private static void ValidateSourceBatch(Project project, EngineeringChange c)
    {
        if (c.Sources == null || c.Sources.Count == 0 || c.Sources.Count > 200)
            throw new ArgumentException("A source batch requires 1..200 ordered source items.");
        if (c.Sources.Sum(s => (long)s.Content.Length) > 4000000)
            throw new ArgumentException("Source batch exceeds 4 million characters.");
        var entries = ObjectCatalog.Enumerate(project).ToList();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? software = null;
        foreach (var item in c.Sources)
        {
            Name(item.Name);
            if (!names.Add(item.Name)) throw new ArgumentException("Duplicate batch name: " + item.Name);
            if (item.Format != "scl" && item.Format != "udt" && item.Format != "db")
                throw new ArgumentException("Source batch supports scl, udt and db only.");
            var parent = ObjectCatalog.Resolve(project, item.Path);
            if (!(parent.Value is PlcBlockGroup) && !(parent.Value is PlcTypeGroup))
                throw new ArgumentException("Each source target must be a Blocks or Types group.");
            if ((parent.Value is PlcTypeGroup) != (item.Format == "udt"))
                throw new ArgumentException("UDT requires a Types group; SCL/DB require Blocks.");
            if (parent.Plc == null) throw new ArgumentException("Source target has no PLC software.");
            if (software == null) software = parent.Plc.Software;
            else if (!software.Equals(parent.Plc.Software))
                throw new ArgumentException("A source batch must belong to exactly one PLC.");
            var path = item.Path + "/" + ObjectCatalog.Segment(item.Name);
            var existing = entries.SingleOrDefault(e => e.Path == path);
            foreach (var other in entries.Where(e => e.Plc?.Software.Equals(parent.Plc.Software) == true))
            {
                if (other.Path != path && Uri.UnescapeDataString(other.Path.Split('/').Last()).Equals(item.Name, StringComparison.OrdinalIgnoreCase)
                    && (other.Value is PlcBlock || other.Value is PlcType))
                    throw new ArgumentException("Name exists at a different target: " + other.Path);
            }
            string? expected = null;
            if (existing != null)
            {
                if (existing.Value is FB block && block.ProgrammingLanguage.ToString() == "SCL") expected = "FUNCTION_BLOCK";
                else if (existing.Value is FC function && function.ProgrammingLanguage.ToString() == "SCL") expected = "FUNCTION";
                else if (existing.Value is GlobalDB || existing.Value is InstanceDB) expected = "DATA_BLOCK";
                else if (existing.Value is PlcType) expected = "TYPE";
                else throw new ArgumentException("Batch cannot replace graphical/system/unsupported object: " + path);
                if ((expected == "TYPE" && item.Format != "udt") || (expected == "DATA_BLOCK" && item.Format != "db")
                    || ((expected == "FUNCTION_BLOCK" || expected == "FUNCTION") && item.Format != "scl"))
                    throw new ArgumentException("Batch format differs from existing object: " + path);
            }
            SourceValidation.SingleDeclaration(item.Content, item.Name, item.Format, expected);
        }
    }

    private static void EnsureAbsent(Project project, string path)
    {
        if (ObjectCatalog.Enumerate(project).Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Target already exists: " + path);
    }

    private static string Snapshot(Project project, EngineeringChange c)
    {
        var entries = ObjectCatalog.Enumerate(project).ToList();
        var b = new StringBuilder(string.Join("\n", entries.Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal)));
        if (c.Action == "write_source_batch")
        {
            foreach (var item in c.Sources!)
            {
                var path = item.Path + "/" + ObjectCatalog.Segment(item.Name);
                var existing = entries.SingleOrDefault(e => e.Path == path);
                b.Append('\n').Append(path).Append('\n');
                // SCL source export also works for a not-yet-compiled block.
                // An instance DB has no standalone source, so retain XML there.
                b.Append(existing == null ? "<absent>" : existing.Value is InstanceDB
                    ? SourceValidation.CanonicalXml(ObjectSourceService.Export(existing, "xml").Content)
                    : ObjectSourceService.Export(existing, "source").Content);
            }
            return ObjectSourceService.Hash(b.ToString());
        }
        foreach (var e in entries.Where(e => e.Path == c.Path || e.Path.StartsWith(c.Path + "/", StringComparison.Ordinal)))
        {
            // Import targets are groups: include existing child bodies, not only their names.
            b.Append(e.Path);
            if (e.Value is PlcBlock || e.Value is PlcType || e.Value is PlcTagTable)
                b.Append(SourceValidation.CanonicalXml(ObjectSourceService.Export(e, "xml").Content));
            else
            {
                var d = ObjectCatalog.Describe(e, true);
                if (d.Errors.Count > 0) throw new InvalidOperationException("Cannot snapshot attributes: " + string.Join("; ", d.Errors));
                b.Append(JsonSerializer.Serialize(d.Attributes.OrderBy(p => p.Key)));
            }
        }
        return ObjectSourceService.Hash(b.ToString());
    }

    private static object Execute(Project project, EngineeringChange c)
    {
        if (c.Action == "write_source_batch")
        {
            var written = new List<object>();
            foreach (var item in c.Sources!)
            {
                var parent = ObjectCatalog.Resolve(project, item.Path);
                using var temp = new TemporaryDirectory();
                var file = Path.Combine(temp.Path, "source." + item.Format);
                File.WriteAllText(file, item.Content, new UTF8Encoding(true));
                var external = parent.Plc!.Software.ExternalSourceGroup.ExternalSources.CreateFromFile(
                    "tiaagent_batch_" + Guid.NewGuid().ToString("N"), file);
                try
                {
                    if (parent.Value is PlcBlockUserGroup bg) external.GenerateBlocksFromSource(bg, GenerateBlockOption.None);
                    else if (parent.Value is PlcTypeUserGroup tg) external.GenerateBlocksFromSource(tg, GenerateBlockOption.None);
                    else external.GenerateBlocksFromSource(GenerateBlockOption.None);
                }
                finally { external.Delete(); }
                var target = ObjectCatalog.Resolve(project, item.Path + "/" + ObjectCatalog.Segment(item.Name));
                written.Add(new { path = target.Path, sourceSha256 = ObjectSourceService.Hash(item.Content) });
            }
            // No compilation inside or between writes. The caller compiles the
            // entire PLC once after this complete transaction is committed.
            return new { sources = written, compilationRequired = true };
        }
        if (c.Action == "add_device")
        {
            var device = project.Devices.CreateWithItem(c.Value!, c.Destination!, c.Name!);
            return new { name = device.Name };
        }
        var e = ObjectCatalog.Resolve(project, c.Path);
        if (c.Action == "create_source")
        {
            using var temp = new TemporaryDirectory();
            var file = Path.Combine(temp.Path, "source." + c.Format);
            File.WriteAllText(file, c.Content, new UTF8Encoding(true));
            var external = e.Plc!.Software.ExternalSourceGroup.ExternalSources.CreateFromFile("tiaagent_" + Guid.NewGuid().ToString("N"), file);
            try
            {
                if (e.Value is PlcBlockUserGroup bg) external.GenerateBlocksFromSource(bg, GenerateBlockOption.None);
                else if (e.Value is PlcTypeUserGroup tg) external.GenerateBlocksFromSource(tg, GenerateBlockOption.None);
                else external.GenerateBlocksFromSource(GenerateBlockOption.None);
            }
            finally { external.Delete(); }
            var created = ObjectCatalog.Resolve(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!));
            // Instance DBs are valid external-source declarations, but Siemens
            // cannot generate standalone textual source for an instance DB.
            // Confirm identity inside the transaction; compile/export XML later.
            if (created.Value is InstanceDB) return ObjectCatalog.Describe(created);
            return ObjectSourceService.Export(created, "source");
        }
        if (c.Action == "import_xml")
        {
            Import(e.Value, c.Content!);
            return ObjectSourceService.Export(ObjectCatalog.Resolve(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!)), "xml");
        }
        if (c.Action == "move_object")
        {
            var xml = ObjectSourceService.Export(e, "xml").Content;
            var dest = ObjectCatalog.Resolve(project, c.Destination!);
            Delete(e.Value);
            Import(dest.Value, xml);
            return ObjectSourceService.Export(ObjectCatalog.Resolve(project, c.Destination + "/" + c.Path.Split('/').Last()), "xml");
        }
        if (c.Action == "delete_object") { Delete(e.Value); return new { deleted = c.Path }; }
        if (c.Action == "rename_object")
        {
            e.Value.SetAttribute("Name", c.Name!);
            var path = c.Path.Substring(0, c.Path.LastIndexOf('/') + 1) + ObjectCatalog.Segment(c.Name!);
            return ObjectCatalog.Describe(ObjectCatalog.Resolve(project, path));
        }
        if (c.Action == "set_attribute")
        {
            var value = ConvertValue(e.Value.GetAttribute(c.Attribute!), c.Value!);
            e.Value.SetAttribute(c.Attribute!, value);
            var actual = e.Value.GetAttribute(c.Attribute!);
            if (!Equals(actual, value)) throw new InvalidOperationException("Attribute readback differs; transaction will roll back.");
            return new { path = c.Path, attribute = c.Attribute, value = Convert.ToString(actual, CultureInfo.InvariantCulture) };
        }
        if (c.Action == "create_folder")
        {
            if (e.Value is PlcBlockGroup bg) bg.Groups.Create(c.Name!);
            else if (e.Value is PlcTypeGroup tg) tg.Groups.Create(c.Name!);
            else ((PlcTagTableGroup)e.Value).Groups.Create(c.Name!);
        }
        else if (c.Action == "create_tag_table") ((PlcTagTableGroup)e.Value).TagTables.Create(c.Name!);
        else if (c.Action == "create_tag") ((PlcTagTable)e.Value).Tags.Create(c.Name!, c.DataType!, c.Address!);
        return ObjectCatalog.Describe(ObjectCatalog.Resolve(project, c.Path + "/" + ObjectCatalog.Segment(c.Name!)), true);
    }

    private static object ConvertValue(object current, string value)
    {
        if (current == null) throw new ArgumentException("Cannot infer the type of a null attribute.");
        var type = current.GetType();
        if (type.IsEnum) return Enum.Parse(type, value, false);
        if (type != typeof(string) && type != typeof(bool) && type != typeof(byte) && type != typeof(short) && type != typeof(ushort) && type != typeof(int) && type != typeof(uint) && type != typeof(long) && type != typeof(ulong) && type != typeof(float) && type != typeof(double)) throw new ArgumentException("Only scalar attributes are supported.");
        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    private static void Import(IEngineeringObject group, string content)
    {
        using var temp = new TemporaryDirectory();
        var file = new FileInfo(Path.Combine(temp.Path, "object.xml"));
        File.WriteAllText(file.FullName, content, new UTF8Encoding(true));
        if (group is PlcBlockGroup bg) bg.Blocks.Import(file, ImportOptions.Override);
        else if (group is PlcTypeGroup tg) tg.Types.Import(file, ImportOptions.Override);
        else if (group is PlcTagTableGroup tt) tt.TagTables.Import(file, ImportOptions.Override);
        else throw new ArgumentException("Unsupported import group.");
    }

    private static void Delete(IEngineeringObject value)
    {
        if (value is PlcBlock b) b.Delete();
        else if (value is PlcType t) t.Delete();
        else if (value is PlcTagTable table) table.Delete();
        else if (value is PlcTag tag) tag.Delete();
        else if (value is PlcBlockUserGroup bg) bg.Delete();
        else if (value is PlcTypeUserGroup tg) tg.Delete();
        else if (value is PlcTagTableUserGroup tt) tt.Delete();
        else throw new ArgumentException("Object cannot be deleted.");
    }
}
