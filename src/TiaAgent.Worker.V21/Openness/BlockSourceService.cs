using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal static class BlockSourceService
{
    public static BlockSourceDto Export(Project project, string blockPath)
    {
        var target = BlockResolver.Resolve(project, blockPath);
        var extension = ExtensionFor(target.Block);
        var content = ExportText(target, extension);
        return new BlockSourceDto
        {
            ProjectIdentity = project.Path.FullName,
            BlockPath = blockPath,
            Format = extension.TrimStart('.'),
            Content = content,
            Sha256 = Sha256(content)
        };
    }

    public static BlockUpdateResultDto Update(Project project, string blockPath, string newSource, bool saveAfterSuccess, ExclusiveAccess exclusive)
    {
        var target = BlockResolver.Resolve(project, blockPath);
        var extension = ExtensionFor(target.Block);
        SourceValidation.SingleDeclaration(newSource, target.Block.Name, extension.TrimStart('.'),
            target.Block is FB ? "FUNCTION_BLOCK" : target.Block is FC ? "FUNCTION" : target.Block is OB ? "ORGANIZATION_BLOCK" : "DATA_BLOCK");
        var original = ExportText(target, extension);
        var report = new CompileReportDto { State = "Error", ErrorCount = 1 };
        try
        {
            using (var transaction = exclusive.Transaction(project, "TiaAgent update block"))
            {
                ApplySource(target, extension, newSource);
                transaction.CommitOnDispose();
            }
            // Siemens explicitly forbids compilation inside an open transaction.
            report = CompileService.CompilePlc(project, target.Plc.DeviceName);
            if (!CompileOutcome.Succeeded(report)) throw new InvalidOperationException("Proposed source failed compilation.");
            var exported = ExportText(BlockResolver.Resolve(project, blockPath), extension);
            var result = new BlockUpdateResultDto { BlockPath = blockPath, Applied = true, NewSha256 = Sha256(exported), Compile = report };
            if (saveAfterSuccess)
            {
                try { project.Save(); result.Saved = true; }
                catch (Exception ex) { result.SaveError = ex.Message; }
            }
            return result;
        }
        catch (Exception ex)
        {
            var result = new BlockUpdateResultDto { BlockPath = blockPath, Applied = false, Compile = report, RollbackAttempted = true, Failure = ex.Message };
            try
            {
                using (var transaction = exclusive.Transaction(project, "TiaAgent restore block"))
                {
                    ApplySource(BlockResolver.Resolve(project, blockPath), extension, original);
                    transaction.CommitOnDispose();
                }
                var restored = ExportText(BlockResolver.Resolve(project, blockPath), extension);
                result.RollbackSucceeded = Sha256(restored) == Sha256(original);
                if (!result.RollbackSucceeded) result.RollbackError = "Restored source readback differs from original.";
            }
            catch (Exception rollback) { result.RollbackError = rollback.Message; }
            return result;
        }
    }

    private static string ExtensionFor(PlcBlock block)
    {
        if (block is GlobalDB)
            return ".db";

        if ((block is FB || block is FC || block is OB) &&
            string.Equals(block.ProgrammingLanguage.ToString(), "SCL", StringComparison.OrdinalIgnoreCase))
            return ".scl";

        throw new InvalidOperationException(
            $"v0.1 source editing supports SCL FB/FC/OB and GlobalDB only. '{block.Name}' is {block.GetType().Name}/{block.ProgrammingLanguage}.");
    }

    private static string ExportText(ResolvedBlock target, string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tia-agent-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = new FileInfo(Path.Combine(dir, target.Block.Name + extension));
            target.ExternalSourceGroup.GenerateSource(
                new List<IGenerateSource> { target.Block },
                file,
                GenerateOptions.None);

            if (!file.Exists)
                throw new InvalidOperationException("TIA Portal did not produce an external source file.");

            return File.ReadAllText(file.FullName).TrimStart('\uFEFF');
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void ApplySource(ResolvedBlock target, string extension, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tia-agent-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        PlcExternalSource? external = null;
        try
        {
            var filePath = Path.Combine(dir, target.Block.Name + extension);
            File.WriteAllText(filePath, NormalizeCrLf(source), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var sourceName = target.Block.Name + "_tiaagent_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            external = target.ExternalSourceGroup.ExternalSources.CreateFromFile(sourceName, filePath);

            if (target.UserGroup is not null)
                external.GenerateBlocksFromSource(target.UserGroup, GenerateBlockOption.None);
            else
                external.GenerateBlocksFromSource(GenerateBlockOption.None);
        }
        finally
        {
            if (external is not null)
            {
                try { external.Delete(); }
                catch (Exception ex) { Console.Error.WriteLine("Temporary external source cleanup failed: " + ex.Message); }
            }
            TryDeleteDirectory(dir);
        }
    }

    private static void ValidateDeclaredName(string blockName, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Source cannot be empty.");

        SourceValidation.SingleDeclaration(source, blockName, "auto");
    }

    private static string NormalizeCrLf(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Replace("\n", "\r\n");
    }

    private static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}
