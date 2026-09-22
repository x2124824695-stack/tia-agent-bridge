using Siemens.Engineering;
using Siemens.Engineering.HW.HardwareCatalog;
using Siemens.Engineering.SW.Blocks;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal sealed class ExtendedOperations
{
    public static readonly string[] ReadMethods = { "get_project_structure", "get_object_info", "export_object", "get_all_block_sources", "search_code", "get_task_config", "list_project_libraries", "list_device_repository", "find_references" };
    public static readonly string[] WriteMethods = { "launch_tia", "open_project", "create_project", "save_project", "create_project_archive", "detach_tia", "preview_engineering_change", "apply_engineering_change" };
    private readonly EngineeringChangeService _changes = new EngineeringChangeService();

    public object Execute(TiaSession session, WorkerRequest r)
    {
        if (r.Method == "launch_tia") return session.Launch();
        if (r.Method == "open_project") return session.Open(r.Path ?? "");
        if (r.Method == "create_project") return session.Create(r.Path ?? "", r.Name ?? "");
        if (r.Method == "detach_tia") return session.Detach();
        var project = session.RequireProject(r.ProjectPath);
        switch (r.Method)
        {
            case "find_references": return CrossReferenceReader.Read(project, r.Path ?? "", r.Limit);
            case "get_project_structure": return ObjectCatalog.List(project, r.Path, r.Limit, r.Offset);
            case "get_object_info": return ObjectCatalog.Describe(ObjectCatalog.Resolve(project, r.Path ?? ""), true);
            case "export_object": return ObjectSourceService.Export(ObjectCatalog.Resolve(project, r.Path ?? ""), r.Format ?? "xml");
            case "get_all_block_sources": return ObjectSourceService.Batch(project, r.Limit, r.Offset);
            case "search_code": return ObjectSourceService.Search(project, r.Query ?? "", r.Limit, r.MatchCase);
            case "get_task_config":
                return new { semantics = "Siemens organization blocks, not CODESYS tasks. Native OB attributes are returned without assuming timing units. Inspect XML for CPU-specific configuration.",
                    objects = ObjectCatalog.Enumerate(project).Where(e => e.Value is OB).Select(e => ObjectCatalog.Describe(e, true)).ToList() };
            case "list_project_libraries": return Libraries(project, r.Limit);
            case "list_device_repository":
                if (string.IsNullOrWhiteSpace(r.Query) || r.Limit < 1 || r.Limit > 1000) throw new ArgumentException("query and limit 1..1000 are required.");
                var catalog = session.HardwareCatalog() ?? throw new InvalidOperationException("Hardware catalog service unavailable.");
                var devices = catalog.Find(r.Query!).Take(r.Limit + 1).ToList();
                return new { complete = devices.Count <= r.Limit, entries = devices.Take(r.Limit).Select(e => new { e.TypeIdentifier, e.ArticleNumber, e.TypeName, e.Version, e.Description, e.CatalogPath }).ToList() };
            case "preview_engineering_change": return _changes.Preview(session, project, r.Change ?? throw new ArgumentException("change is required."));
            case "apply_engineering_change": return _changes.Apply(session, project, r.PreviewToken ?? "");
            case "save_project":
                using (session.ExclusiveAccess()) project.Save();
                return new { saved = true, projectPath = project.Path.FullName };
            case "create_project_archive":
                if (r.Path == null || !Path.IsPathRooted(r.Path) || string.IsNullOrWhiteSpace(r.Name) || r.Name!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || r.Name == "." || r.Name == "..")
                    throw new ArgumentException("Absolute archive directory and plain archive name required.");
                if (Directory.Exists(r.Path) && Directory.EnumerateFileSystemEntries(r.Path, r.Name + "*").Any()) throw new ArgumentException("Archive target already exists.");
                using (session.ExclusiveAccess()) project.Archive(new DirectoryInfo(r.Path), r.Name, ProjectArchivationMode.Compressed);
                return new { archived = true, directory = r.Path, name = r.Name };
            default: throw new ArgumentException("Unsupported extended method.");
        }
    }

    private static ObjectListDto Libraries(Project project, int limit)
    {
        if (limit < 1 || limit > 5000) throw new ArgumentException("limit must be 1..5000.");
        var result = new ObjectListDto();
        WalkLibrary(project.ProjectLibrary.TypeFolder, "Library/Types", result, limit, 0);
        WalkLibrary(project.ProjectLibrary.MasterCopyFolder, "Library/MasterCopies", result, limit, 0);
        return result;
    }

    private static void WalkLibrary(IEngineeringObject obj, string path, ObjectListDto result, int limit, int depth)
    {
        if (depth > 32 || result.Objects.Count >= limit) { result.Complete = false; return; }
        var entry = new CatalogEntry { Path = path, Value = obj };
        var info = ObjectCatalog.Describe(entry, true);
        if (info.Errors.Count > 0) result.Complete = false;
        result.Objects.Add(info);
        foreach (var composition in obj.GetCompositionInfos())
        {
            if (composition.Name != "Folders" && composition.Name != "Types" && composition.Name != "MasterCopies" && composition.Name != "Versions") continue;
            foreach (IEngineeringObject child in (System.Collections.IEnumerable)obj.GetComposition(composition.Name))
            {
                if (result.Objects.Count >= limit) { result.Complete = false; return; }
                var name = child.GetAttributeInfos().Any(a => a.Name == "Name") ? child.GetAttribute("Name")?.ToString() : child.GetAttribute("VersionNumber")?.ToString();
                WalkLibrary(child, path + "/" + composition.Name + "/" + ObjectCatalog.Segment(name ?? "unnamed"), result, limit, depth + 1);
            }
        }
    }
}
