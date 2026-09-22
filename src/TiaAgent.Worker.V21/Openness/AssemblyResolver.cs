using System.Reflection;

namespace TiaAgent.Worker.V21.Openness;

internal static class AssemblyResolver
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name).Name;
        if (requested is null || !requested.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            return null;

        var root = Environment.GetEnvironmentVariable("TIA_PORTAL_PUBLIC_API");
        foreach (var dir in CandidateRoots(root))
        {
            var candidate = Path.Combine(dir, requested + ".dll");
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured!;

        const string relative = @"Siemens\Automation\Portal V21\PublicAPI\V21\net48";
        yield return Path.Combine(@"C:\Program Files", relative);
        yield return Path.Combine(@"D:\Program Files", relative);
    }
}
