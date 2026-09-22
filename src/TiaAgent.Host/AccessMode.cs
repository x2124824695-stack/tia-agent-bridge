namespace TiaAgent.Host;

public enum AccessMode
{
    Inspect = 0,
    Engineering = 1,
    Online = 2,
    Control = 3
}

public static class AccessModeParser
{
    public static AccessMode Parse(string[] args)
    {
        string? raw = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--access", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                raw = args[i + 1];
                break;
            }

            if (args[i].StartsWith("--access=", StringComparison.OrdinalIgnoreCase))
            {
                raw = args[i].Substring("--access=".Length);
                break;
            }
        }

        raw ??= Environment.GetEnvironmentVariable("TIA_AGENT_ACCESS") ?? "inspect";
        return raw.Trim().ToLowerInvariant() switch
        {
            "inspect" or "read-only" => AccessMode.Inspect,
            "engineering" or "read-write" => AccessMode.Engineering,
            "online" => AccessMode.Online,
            "control" => AccessMode.Control,
            _ => throw new ArgumentException("--access must be inspect, engineering, online, or control.")
        };
    }
}
