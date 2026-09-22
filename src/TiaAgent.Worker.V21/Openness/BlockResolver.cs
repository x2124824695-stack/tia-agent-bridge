using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;

namespace TiaAgent.Worker.V21.Openness;

internal static class BlockResolver
{
    public static ResolvedBlock Resolve(Project project, string blockPath)
    {
        var parts = blockPath.Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Empty path segments are invalid.");
        if (parts.Length < 3 || !string.Equals(parts[1], "Blocks", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Block path must be PLC/Blocks/[Folder/...]/BlockName.");

        var plc = PlcSoftwareLocator.FindOne(project, parts[0]);
        PlcBlockGroup group = plc.Software.BlockGroup;
        for (var i = 2; i < parts.Length - 1; i++)
        {
            PlcBlockGroup? next = null;
            foreach (PlcBlockGroup candidate in group.Groups)
            {
                if (string.Equals(candidate.Name, parts[i], StringComparison.OrdinalIgnoreCase))
                {
                    next = candidate;
                    break;
                }
            }

            group = next ?? throw new InvalidOperationException($"Block folder '{parts[i]}' was not found.");
        }

        var blockName = parts[parts.Length - 1];
        var block = group.Blocks.Find(blockName)
            ?? throw new InvalidOperationException($"Block '{blockName}' was not found at '{blockPath}'.");

        return new ResolvedBlock(plc, group, block, blockPath);
    }
}

internal sealed class ResolvedBlock
{
    public ResolvedBlock(DiscoveredPlc plc, PlcBlockGroup group, PlcBlock block, string path)
    {
        Plc = plc;
        Group = group;
        Block = block;
        Path = path;
    }

    public DiscoveredPlc Plc { get; }
    public PlcBlockGroup Group { get; }
    public PlcBlock Block { get; }
    public string Path { get; }
    public PlcExternalSourceSystemGroup ExternalSourceGroup => Plc.Software.ExternalSourceGroup;
    public PlcBlockUserGroup? UserGroup => Group as PlcBlockUserGroup;
}
