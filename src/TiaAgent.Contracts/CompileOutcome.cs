using System;

namespace TiaAgent.Contracts;

public static class CompileOutcome
{
    public static bool Succeeded(CompileReportDto report) => report.ErrorCount == 0 &&
        (string.Equals(report.State, "Success", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(report.State, "Information", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(report.State, "Warning", StringComparison.OrdinalIgnoreCase));
}
