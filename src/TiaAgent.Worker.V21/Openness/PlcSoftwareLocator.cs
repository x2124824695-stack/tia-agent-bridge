using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace TiaAgent.Worker.V21.Openness;

internal static class PlcSoftwareLocator
{
    public static IEnumerable<DiscoveredPlc> FindAll(Project project, string? name = null)
    {
        foreach (Device device in Devices(project).Distinct())
        {
            foreach (var software in FindInItems(device.DeviceItems))
            {
                if (name is not null &&
                    !string.Equals(name, device.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, software.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return new DiscoveredPlc(device.Name, software);
            }
        }
    }

    private static IEnumerable<Device> Devices(Project project)
    {
        foreach (Device d in project.Devices) yield return d;
        foreach (Device d in project.UngroupedDevicesGroup.Devices) yield return d;
        foreach (DeviceUserGroup group in project.DeviceGroups)
            foreach (var d in Devices(group)) yield return d;
    }

    private static IEnumerable<Device> Devices(DeviceUserGroup group)
    {
        foreach (Device d in group.Devices) yield return d;
        foreach (DeviceUserGroup child in group.Groups)
            foreach (var d in Devices(child)) yield return d;
    }

    public static DiscoveredPlc FindOne(Project project, string name)
    {
        var matches = FindAll(project, name).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one PLC matching '{name}', found {matches.Count}.");
        return matches[0];
    }

    private static IEnumerable<PlcSoftware> FindInItems(DeviceItemComposition items)
    {
        foreach (DeviceItem item in items)
        {
            PlcSoftware? software = null;
            try
            {
                software = item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            }
            catch (EngineeringException ex) { throw new InvalidOperationException("Cannot inspect device item '" + item.Name + "'; PLC discovery would be incomplete.", ex); }

            if (software is not null)
                yield return software;

            foreach (var child in FindInItems(item.DeviceItems))
                yield return child;
        }
    }
}

internal sealed class DiscoveredPlc
{
    public DiscoveredPlc(string deviceName, PlcSoftware software)
    {
        DeviceName = deviceName;
        Software = software;
    }

    public string DeviceName { get; }
    public PlcSoftware Software { get; }
}
