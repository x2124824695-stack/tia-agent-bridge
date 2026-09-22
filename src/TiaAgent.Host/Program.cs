using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaAgent.Host.Safety;
using TiaAgent.Host.Tools;
using TiaAgent.Host.Worker;

namespace TiaAgent.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        AccessMode accessMode;
        try
        {
            accessMode = AccessModeParser.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (args.Length > 0 && string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase))
            return RunDoctor();

        Console.Error.WriteLine($"TiaAgentBridge access mode: {accessMode.ToString().ToUpperInvariant()}");
        if (accessMode == AccessMode.Inspect)
            Console.Error.WriteLine("Engineering writes, online operations and PLC control are disabled.");

        // Fully qualified: the enclosing 'TiaAgent.Host' namespace would otherwise
        // win name resolution over the 'Microsoft.Extensions.Hosting.Host' type.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<TiaInstallationDetector>();
        builder.Services.AddSingleton(sp => new WorkerProcessClient(
            sp.GetRequiredService<ILogger<WorkerProcessClient>>(),
            sp.GetRequiredService<TiaInstallationDetector>(),
            accessMode));
        builder.Services.AddSingleton<TiaWorkerFacade>();
        builder.Services.AddSingleton<PreviewTokenService>();
        builder.Services.AddSingleton(new ToolConfiguration(accessMode));

        var mcp = builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<TiaReadTools>()
            .WithTools<TiaExtendedReadTools>()
            .WithTools<TiaCapabilityTools>();

        if (accessMode >= AccessMode.Engineering)
            mcp.WithTools<TiaEngineeringTools>().WithTools<TiaExtendedEngineeringTools>();
        if (accessMode >= AccessMode.Online) mcp.WithTools<TiaOnlineTools>();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static int RunDoctor()
    {
        var detector = new TiaInstallationDetector();
        try
        {
            Console.WriteLine("TIA V21 PublicAPI: " + detector.ResolveV21PublicApiDirectory());
        }
        catch (Exception ex)
        {
            Console.WriteLine("TIA V21 PublicAPI: ERROR - " + ex.Message);
            return 1;
        }

        try
        {
            Console.WriteLine("V21 worker: " + detector.ResolveWorkerPath());
        }
        catch (Exception ex)
        {
            Console.WriteLine("V21 worker: ERROR - " + ex.Message);
            return 1;
        }

        Console.WriteLine("Environment check passed.");
        return 0;
    }
}
