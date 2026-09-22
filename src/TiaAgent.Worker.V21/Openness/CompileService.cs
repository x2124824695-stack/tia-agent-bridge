using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal static class CompileService
{
    public static CompileReportDto Compile(Project project, string? plcName, string? blockPath)
    {
        if (!string.IsNullOrWhiteSpace(blockPath))
        {
            var target = BlockResolver.Resolve(project, blockPath!);
            return Build("block", blockPath!, CompileObject(target.Block));
        }

        if (string.IsNullOrWhiteSpace(plcName))
        {
            var all = PlcSoftwareLocator.FindAll(project).ToList();
            if (all.Count != 1)
                throw new InvalidOperationException("Specify plcName when the project does not contain exactly one PLC.");
            plcName = all[0].DeviceName;
        }

        return CompilePlc(project, plcName!);
    }

    public static CompileReportDto CompilePlc(Project project, string plcName)
    {
        var plc = PlcSoftwareLocator.FindOne(project, plcName);
        return Build("plc", plc.DeviceName, CompileObject(plc.Software));
    }

    private static CompilerResult CompileObject(object value)
    {
        if (value is IEngineeringServiceProvider provider)
        {
            var service = provider.GetService<ICompilable>();
            if (service is not null)
                return service.Compile();
        }

        var method = value.GetType().GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
            throw new InvalidOperationException($"Object '{value.GetType().Name}' is not compilable through Openness.");

        try
        {
            return (CompilerResult)(method.Invoke(value, null)
                ?? throw new InvalidOperationException("Compile returned no result."));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static CompileReportDto Build(string scope, string target, CompilerResult result)
    {
        var dto = new CompileReportDto
        {
            Scope = scope,
            Target = target,
            State = result.State.ToString(),
            ErrorCount = result.ErrorCount,
            WarningCount = result.WarningCount
        };

        AddMessages(result.Messages, dto.Messages);
        return dto;
    }

    private static void AddMessages(CompilerResultMessageComposition messages, List<CompileMessageDto> output)
    {
        foreach (CompilerResultMessage message in messages)
        {
            output.Add(new CompileMessageDto
            {
                Severity = message.ErrorCount > 0 ? "Error" : message.WarningCount > 0 ? "Warning" : "Information",
                Description = message.Description,
                Path = ReadPath(message)
            });
            AddMessages(message.Messages, output);
        }
    }

    private static string ReadPath(CompilerResultMessage message)
        => message.GetType().GetProperty("Path")?.GetValue(message, null)?.ToString() ?? string.Empty;
}
