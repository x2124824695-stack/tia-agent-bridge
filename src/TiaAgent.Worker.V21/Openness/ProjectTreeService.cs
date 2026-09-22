using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal static class ProjectTreeService
{
    public static ProjectTreeDto Read(Project project)
    {
        var result = new ProjectTreeDto
        {
            ProjectName = project.Name,
            ProjectPath = project.Path?.FullName
        };

        foreach (var discovered in PlcSoftwareLocator.FindAll(project))
        {
            var plc = new PlcTreeDto { Name = discovered.DeviceName };
            WalkGroup(discovered.Software.BlockGroup, ObjectCatalog.Segment(discovered.DeviceName) + "/Blocks", plc.Blocks);
            result.Plcs.Add(plc);
        }

        return result;
    }

    private static void WalkGroup(PlcBlockGroup group, string path, List<BlockInfoDto> blocks)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            blocks.Add(new BlockInfoDto
            {
                Path = path + "/" + ObjectCatalog.Segment(block.Name),
                Name = block.Name,
                Kind = Kind(block),
                Language = block.ProgrammingLanguage.ToString(),
                Number = TryNumber(block),
                Author = NullIfBlank(block.HeaderAuthor),
                Version = NullIfBlank(block.HeaderVersion?.ToString()),
                Family = NullIfBlank(block.HeaderFamily)
            });
        }

        foreach (PlcBlockGroup child in group.Groups)
            WalkGroup(child, path + "/" + ObjectCatalog.Segment(child.Name), blocks);
    }

    private static string Kind(PlcBlock block)
        => block is FB ? "FB"
         : block is FC ? "FC"
         : block is OB ? "OB"
         : block is GlobalDB ? "GlobalDB"
         : block is InstanceDB ? "InstanceDB"
         : block.GetType().Name;

    private static int? TryNumber(PlcBlock block)
    {
        try { return block.Number; }
        catch (EngineeringException) { return null; }
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
