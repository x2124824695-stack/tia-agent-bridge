using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaAgent.Contracts;

namespace TiaAgent.Worker.V21.Openness;

internal sealed class CatalogEntry
{
    public string Path = "";
    public IEngineeringObject Value = null!;
    public DiscoveredPlc? Plc;
    public IEngineeringObject? Group;
}

internal static class ObjectCatalog
{
    public static bool HasSoftwareUnits(Project project) => PlcSoftwareLocator.FindAll(project).Any(plc => {
        var provider = plc.Software.GetService<Siemens.Engineering.SW.Units.PlcUnitProvider>();
        return provider != null && (provider.UnitGroup.Units.Any() || provider.UnitGroup.SafetyUnits.Any());
    });
    public static string Segment(string name) => Uri.EscapeDataString(name);

    public static IEnumerable<CatalogEntry> Enumerate(Project project)
    {
        foreach (var plc in PlcSoftwareLocator.FindAll(project))
        {
            var root = Segment(plc.DeviceName);
            foreach (var e in Blocks(plc.Software.BlockGroup, root + "/Blocks", plc)) yield return e;
            foreach (var e in Types(plc.Software.TypeGroup, root + "/Types", plc)) yield return e;
            foreach (var e in Tags(plc.Software.TagTableGroup, root + "/Tags", plc)) yield return e;
        }
        foreach (var e in Devices(project.Devices, "Devices")) yield return e;
        foreach (DeviceUserGroup group in project.DeviceGroups)
            foreach (var e in DeviceGroups(group, "DeviceGroups/" + Segment(group.Name))) yield return e;
        foreach (var e in Devices(project.UngroupedDevicesGroup.Devices, "UngroupedDevices")) yield return e;
    }

    private static IEnumerable<CatalogEntry> DeviceGroups(DeviceUserGroup group, string path)
    {
        foreach (var e in Devices(group.Devices, path)) yield return e;
        foreach (DeviceUserGroup child in group.Groups)
            foreach (var e in DeviceGroups(child, path + "/" + Segment(child.Name))) yield return e;
    }

    private static IEnumerable<CatalogEntry> Devices(DeviceComposition devices, string path)
    {
        foreach (Device device in devices)
        {
            var p = path + "/" + Segment(device.Name);
            yield return Entry(p, device);
            foreach (var e in Items(device.DeviceItems, p)) yield return e;
        }
    }

    private static IEnumerable<CatalogEntry> Items(DeviceItemComposition items, string path)
    {
        foreach (DeviceItem item in items)
        {
            var p = path + "/" + Segment(item.Name);
            yield return Entry(p, item);
            var index = 0;
            foreach (Siemens.Engineering.HW.Address address in item.Addresses)
                yield return Entry(p + "/@Addresses/" + index++, address);
            index = 0;
            foreach (Channel channel in item.Channels)
                yield return Entry(p + "/@Channels/" + index++, channel);
            foreach (var e in Items(item.DeviceItems, p)) yield return e;
        }
    }

    private static IEnumerable<CatalogEntry> Blocks(PlcBlockGroup group, string path, DiscoveredPlc plc)
    {
        yield return Entry(path, group, plc);
        foreach (PlcBlock value in group.Blocks) yield return Entry(path + "/" + Segment(value.Name), value, plc, group);
        foreach (PlcBlockUserGroup child in group.Groups)
            foreach (var e in Blocks(child, path + "/" + Segment(child.Name), plc)) yield return e;
    }

    private static IEnumerable<CatalogEntry> Types(PlcTypeGroup group, string path, DiscoveredPlc plc)
    {
        yield return Entry(path, group, plc);
        foreach (PlcType value in group.Types) yield return Entry(path + "/" + Segment(value.Name), value, plc, group);
        foreach (PlcTypeUserGroup child in group.Groups)
            foreach (var e in Types(child, path + "/" + Segment(child.Name), plc)) yield return e;
    }

    private static IEnumerable<CatalogEntry> Tags(PlcTagTableGroup group, string path, DiscoveredPlc plc)
    {
        yield return Entry(path, group, plc);
        foreach (PlcTagTable table in group.TagTables)
        {
            var p = path + "/" + Segment(table.Name);
            yield return Entry(p, table, plc, group);
            foreach (PlcTag tag in table.Tags) yield return Entry(p + "/" + Segment(tag.Name), tag, plc, table);
        }
        foreach (PlcTagTableUserGroup child in group.Groups)
            foreach (var e in Tags(child, path + "/" + Segment(child.Name), plc)) yield return e;
    }

    private static CatalogEntry Entry(string path, IEngineeringObject value, DiscoveredPlc? plc = null, IEngineeringObject? group = null)
        => new CatalogEntry { Path = path, Value = value, Plc = plc, Group = group };

    public static CatalogEntry Resolve(Project project, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An exact catalog path is required.");
        var matches = Enumerate(project).Where(e => string.Equals(e.Path, path, StringComparison.Ordinal)).Take(2).ToList();
        if (matches.Count != 1) throw new InvalidOperationException("Expected one object at '" + path + "', found " + matches.Count + ". Use get_project_structure paths exactly.");
        return matches[0];
    }

    public static ObjectInfoDto Describe(CatalogEntry entry, bool attributes = false)
    {
        var dto = new ObjectInfoDto { Path = entry.Path, Kind = entry.Value.GetType().Name, Name = Uri.UnescapeDataString(entry.Path.Split('/').Last()) };
        if (attributes)
            foreach (var info in entry.Value.GetAttributeInfos())
            {
                if ((info.AccessMode & EngineeringAttributeAccessMode.Read) == 0) continue;
                try { dto.Attributes[info.Name] = Convert.ToString(entry.Value.GetAttribute(info.Name), CultureInfo.InvariantCulture); }
                catch (Exception ex) { dto.Errors.Add(info.Name + ": " + ex.Message); }
            }
        return dto;
    }

    public static ObjectListDto List(Project project, string? prefix, int limit, int offset)
    {
        if (limit < 1 || limit > 5000 || offset < 0) throw new ArgumentException("limit must be 1..5000 and offset >= 0.");
        var found = Enumerate(project).Where(e => string.IsNullOrEmpty(prefix) || e.Path == prefix || e.Path.StartsWith(prefix + "/", StringComparison.Ordinal))
            .OrderBy(e => e.Path, StringComparer.Ordinal).Skip(offset).Take(limit + 1).ToList();
        var result = new ObjectListDto { Complete = offset == 0 && found.Count <= limit && !HasSoftwareUnits(project) };
        result.Objects = found.Take(limit).Select(e => Describe(e)).ToList();
        result.Warnings.Add("Software Units are not included; use a non-unit PLC project. Pages are not an atomic project snapshot.");
        return result;
    }
}
