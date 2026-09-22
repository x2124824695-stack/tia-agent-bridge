using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaAgent.Contracts;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host.Tools;

[McpServerToolType]
public sealed class TiaExtendedReadTools
{
    [McpServerTool(Name = "get_compile_messages", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read the last explicit compile result in this worker session, with nested diagnostics. This is historical and is not proof of current project validity.")]
    public static Task<CallToolResult> CompileMessages(TiaWorkerFacade worker)
        => Call(worker, new WorkerRequest { Method = "get_compile_messages" });
    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read native Siemens cross references for a block/tag/type catalog path. Returns source, reference, location and access type. Requires a target exposing CrossReferenceService; no text-search fallback.")]
    public static Task<CallToolResult> References(TiaWorkerFacade worker, string path, int limit = 200, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "find_references", Path = path, Limit = limit, ProjectPath = projectPath });
    internal static async Task<CallToolResult> Call(TiaWorkerFacade worker, WorkerRequest request)
        => TiaReadTools.Render(await worker.SendAsync<object>(request).ConfigureAwait(false));

    [McpServerTool(Name = "get_project_structure", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List Siemens blocks, UDTs, tag tables/tags and device items. Paths use URL-escaped name segments. Software Units are excluded. Results are paginated; complete is false for partial pages.")]
    public static Task<CallToolResult> Structure(TiaWorkerFacade worker, string? prefix = null, int limit = 200, int offset = 0, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "get_project_structure", Path = prefix, Limit = limit, Offset = offset, ProjectPath = projectPath });

    [McpServerTool(Name = "get_object_info", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read native Openness attributes for an exact get_project_structure path, including device parameters, PLC tags and OB settings. Attribute read errors are explicit.")]
    public static Task<CallToolResult> Info(TiaWorkerFacade worker, string path, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "get_object_info", Path = path, ProjectPath = projectPath });

    [McpServerTool(Name = "export_object", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Export a block, UDT or tag table as Siemens XML (including supported LAD/FBD), or source for SCL/STL/DB/UDT. Returns content and SHA-256. Does not convert graphic logic to SCL.")]
    public static Task<CallToolResult> Export(TiaWorkerFacade worker, string path, string format = "xml", string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "export_object", Path = path, Format = format, ProjectPath = projectPath });

    [McpServerTool(Name = "get_all_block_sources", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read a bounded page of block/UDT external sources (max 200 objects/2M characters). Use nextOffset for the next page. Graphic/protected/failed exports are explicit readErrors; software units excluded.")]
    public static Task<CallToolResult> Sources(TiaWorkerFacade worker, int limit = 100, int offset = 0, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "get_all_block_sources", Limit = limit, Offset = offset, ProjectPath = projectPath });

    [McpServerTool(Name = "search_code", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Literal text search across up to 2000 block/UDT sources. Returns line numbers and completeness. Not a semantic cross-reference; graphic logic and software units are not covered.")]
    public static Task<CallToolResult> Search(TiaWorkerFacade worker, string query, int limit = 200, bool matchCase = false, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "search_code", Query = query, Limit = limit, MatchCase = matchCase, ProjectPath = projectPath });

    [McpServerTool(Name = "get_task_config", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read Siemens OB attributes. Siemens schedules organization blocks; there is no CODESYS task/POU-call list. Do not infer timing units from numeric values.")]
    public static Task<CallToolResult> Tasks(TiaWorkerFacade worker, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "get_task_config", ProjectPath = projectPath });

    [McpServerTool(Name = "list_project_libraries", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List project library types, versions and master copies with native metadata and output limits.")]
    public static Task<CallToolResult> Libraries(TiaWorkerFacade worker, int limit = 200, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "list_project_libraries", Limit = limit, ProjectPath = projectPath });

    [McpServerTool(Name = "list_device_repository", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Search the installed Siemens hardware catalog. query is the native catalog search pattern. Returns Siemens TypeIdentifiers, not CODESYS vendor/device IDs.")]
    public static Task<CallToolResult> Catalog(TiaWorkerFacade worker, string query, int limit = 100, string? projectPath = null)
        => Call(worker, new WorkerRequest { Method = "list_device_repository", Query = query, Limit = limit, ProjectPath = projectPath });
}

[McpServerToolType]
public sealed class TiaExtendedEngineeringTools
{
    [McpServerTool(Name = "preview_engineering_change", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Preview a native Siemens change. Actions: write_source_batch, create_source, import_xml, create_folder, create_tag_table, create_tag, delete_object, rename_object, move_object, set_attribute, add_device. write_source_batch uses ordered sources items {path: exact Blocks/Types group, name, content, format: scl/udt/db}, creating or replacing explicit targets in one PLC transaction without intermediate compilation; call compile for the entire PLC after applying the complete batch. For other actions, path is an exact catalog object/group; name is the new name; content/format for source; destination for move; dataType/address for tag; attribute/value for scalar setting. add_device uses value=TypeIdentifier, name=device name, destination=item name. Returns a single-use 10-minute token bound to the project and source snapshots. No writes in preview.")]
    public static Task<CallToolResult> Preview(TiaWorkerFacade worker, EngineeringChange change, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "preview_engineering_change", Change = change, ProjectPath = projectPath });

    [McpServerTool(Name = "apply_engineering_change", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Apply the exact worker-stored preview under exclusive access and an Openness rollback transaction. Does not save or compile automatically. Then call compile and explicitly save_project. Cannot be used for online operations.")]
    public static Task<CallToolResult> Apply(TiaWorkerFacade worker, string previewToken, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "apply_engineering_change", PreviewToken = previewToken, ProjectPath = projectPath });

    [McpServerTool(Name = "launch_tia", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Explicitly launch a TIA V21 user-interface process. Reuses this worker's attachment if already attached. Does not open a project.")]
    public static Task<CallToolResult> Launch(TiaWorkerFacade worker)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "launch_tia" });

    [McpServerTool(Name = "open_project", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Explicitly open an existing absolute .ap21 file in an empty attached/launched TIA instance. Refuses implicit upgrade, project close or switch.")]
    public static Task<CallToolResult> Open(TiaWorkerFacade worker, string path)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "open_project", Path = path });

    [McpServerTool(Name = "create_project", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Create a Siemens project in an absolute directory with a new name. Requires an empty attached/launched instance. Does not select CODESYS templates.")]
    public static Task<CallToolResult> Create(TiaWorkerFacade worker, string directory, string name)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "create_project", Path = directory, Name = name });

    [McpServerTool(Name = "save_project", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Explicitly save the current TIA project, including unsaved user edits. Compile and inspect diagnostics first.")]
    public static Task<CallToolResult> Save(TiaWorkerFacade worker, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "save_project", ProjectPath = projectPath });

    [McpServerTool(Name = "create_project_archive", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Create a compressed native Siemens project archive in an absolute directory; refuses an existing matching target.")]
    public static Task<CallToolResult> Archive(TiaWorkerFacade worker, string directory, string name, string? projectPath = null)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "create_project_archive", Path = directory, Name = name, ProjectPath = projectPath });

    [McpServerTool(Name = "detach_tia", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Detach this MCP worker without saving or explicitly terminating TIA. Outstanding previews become invalid for a new session.")]
    public static Task<CallToolResult> Detach(TiaWorkerFacade worker)
        => TiaExtendedReadTools.Call(worker, new WorkerRequest { Method = "detach_tia" });
}
