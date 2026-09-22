namespace TiaAgent.Worker.V21;

internal enum WorkerAccess
{
    Inspect = 0,
    Engineering = 1,
    Online = 2,
    Control = 3
}

internal static class WorkerAccessParser
{
    public static WorkerAccess Parse(string[] args)
    {
        string? raw = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--access", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                raw = args[i + 1];
            else if (args[i].StartsWith("--access=", StringComparison.OrdinalIgnoreCase))
                raw = args[i].Substring("--access=".Length);
        }

        raw ??= "inspect";
        return raw.ToLowerInvariant() switch
        {
            "inspect" => WorkerAccess.Inspect,
            "engineering" => WorkerAccess.Engineering,
            "online" => WorkerAccess.Online,
            "control" => WorkerAccess.Control,
            _ => WorkerAccess.Inspect
        };
    }
}
