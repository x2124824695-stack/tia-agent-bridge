namespace TiaAgent.Host;

public sealed class TiaInstallationDetector
{
    private const string V21RelativePath = @"Siemens\Automation\Portal V21\PublicAPI\V21\net48";

    /// <summary>Common TIA Portal V21 install roots, in probe order.</summary>
    public static readonly string[] DefaultV21Paths =
    {
        Path.Combine(@"C:\Program Files", V21RelativePath),
        Path.Combine(@"D:\Program Files", V21RelativePath)
    };

    public string ResolveV21PublicApiDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("TIA_PORTAL_PUBLIC_API");
        if (!string.IsNullOrWhiteSpace(configured) && HasCoreAssemblies(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var defaultPath in DefaultV21Paths)
        {
            if (HasCoreAssemblies(defaultPath))
            {
                return defaultPath;
            }
        }

        throw new DirectoryNotFoundException(
            "TIA Portal V21 Openness assemblies were not found. Set TIA_PORTAL_PUBLIC_API to the V21 net48 PublicAPI directory.");
    }

    public string ResolveWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable("TIA_AGENT_WORKER_V21");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "openness-worker", "TiaAgent.Worker.V21.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException("TiaAgent.Worker.V21.exe was not found.", candidate);
    }

    private static bool HasCoreAssemblies(string directory)
        => Directory.Exists(directory)
           && File.Exists(Path.Combine(directory, "Siemens.Engineering.Base.dll"))
           && File.Exists(Path.Combine(directory, "Siemens.Engineering.Step7.dll"));
}
