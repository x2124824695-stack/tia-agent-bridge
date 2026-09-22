namespace TiaAgent.Contracts;

public static class Protocol
{
    public const string Version = "0.2";
}

public static class WorkerMethods
{
    public const string Hello = "hello";
    public const string GetProjectStatus = "get_project_status";
    public const string BrowseProjectTree = "browse_project_tree";
    public const string GetBlockSource = "get_block_source";
    public const string UpdateBlockSource = "update_block_source";
    public const string Compile = "compile";
}
