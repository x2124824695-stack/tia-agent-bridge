using Siemens.Engineering;

namespace TiaAgent.Worker.V21.Openness;

internal sealed class TiaSession : IDisposable
{
    private TiaPortal? _portal;
    private Project? _project;
    private int? _portalPid;

    public ExclusiveAccess ExclusiveAccess() => (_portal ?? throw new InvalidOperationException("Attach or launch TIA first.")).ExclusiveAccess("TiaAgent engineering operation");
    public Siemens.Engineering.HW.HardwareCatalog.HardwareCatalog HardwareCatalog()
        => (_portal ?? throw new InvalidOperationException("Attach first.")).HardwareCatalog;

    public object Launch()
    {
        if (_portal != null) return new { portalProcessId = _portalPid, alreadyAttached = true };
        _portal = new TiaPortal(TiaPortalMode.WithUserInterface);
        _portalPid = _portal.GetCurrentProcess().Id;
        return new { portalProcessId = _portalPid, alreadyAttached = false };
    }

    public object Open(string path)
    {
        if (!Path.IsPathRooted(path) || !path.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("An existing absolute .ap21 path is required; no implicit upgrade.");
        if (_portal == null) EnsureAttached(null);
        if (_portal!.Projects.Any()) throw new InvalidOperationException("An open project already exists. No implicit close or switch is permitted.");
        _project = _portal.Projects.Open(new FileInfo(path));
        return new { name = _project.Name, path = _project.Path.FullName };
    }

    public object Create(string directory, string name)
    {
        if (!Path.IsPathRooted(directory) || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
            throw new ArgumentException("An absolute directory and valid project name are required.");
        if (Directory.Exists(Path.Combine(directory, name))) throw new ArgumentException("Project destination already exists.");
        if (_portal == null) EnsureAttached(null);
        if (_portal!.Projects.Any()) throw new InvalidOperationException("An open project already exists; no implicit close.");
        _project = _portal.Projects.Create(new DirectoryInfo(directory), name);
        return new { name = _project.Name, path = _project.Path.FullName };
    }

    public object Detach()
    {
        // Dispose an attachment only: never terminate the user's engineering UI.
        Dispose();
        return new { detached = true, saved = false };
    }

    public Project RequireProject(string? requestedProjectPath)
    {
        EnsureAttached(requestedProjectPath);
        if (_project is null)
            throw new InvalidOperationException("No uniquely identifiable project is open in the attached TIA Portal instance.");
        return _project;
    }

    public ProjectStatusSnapshot GetStatus(string? requestedProjectPath)
    {
        EnsureAttached(requestedProjectPath);
        return new ProjectStatusSnapshot
        {
            Connected = _portal is not null,
            PortalProcessId = _portalPid,
            Project = _project
        };
    }

    private void EnsureAttached(string? requestedProjectPath)
    {
        if (_portal is null)
        {
            var processes = TiaPortal.GetProcesses().ToList();
            if (processes.Count == 0)
                throw new InvalidOperationException("No running TIA Portal process was found.");

            TiaPortalProcess selected;
            if (!string.IsNullOrWhiteSpace(requestedProjectPath))
            {
                var expected = Path.GetFullPath(requestedProjectPath);
                var matches = processes.Where(p => PathEquals(TryProcessProjectPath(p), expected)).ToList();
                if (matches.Count != 1)
                    throw new InvalidOperationException($"Could not identify exactly one running TIA Portal process for project '{expected}'.");
                selected = matches[0];
            }
            else
            {
                if (processes.Count != 1)
                    throw new InvalidOperationException("Multiple TIA Portal processes are running. Supply projectPath so the target can be selected explicitly.");
                selected = processes[0];
            }

            _portal = selected.Attach();
            _portalPid = _portal.GetCurrentProcess().Id;
        }

        RefreshProject(requestedProjectPath);
    }

    private void RefreshProject(string? requestedProjectPath)
    {
        if (_portal is null) return;
        var projects = _portal.Projects.ToList();
        if (!string.IsNullOrWhiteSpace(requestedProjectPath))
        {
            var expected = Path.GetFullPath(requestedProjectPath);
            var matches = projects.Where(p => PathEquals(TryProjectPath(p), expected)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"TIA Portal does not have exactly one matching open project '{expected}'. Read operations never open or switch projects.");
            _project = matches[0];
            return;
        }

        if (projects.Count == 1)
            _project = projects[0];
        else if (projects.Count == 0)
            _project = null;
        else
            throw new InvalidOperationException("Multiple projects are open in TIA Portal. Supply projectPath explicitly.");
    }

    private static string? TryProcessProjectPath(TiaPortalProcess process)
    {
        try { return process.ProjectPath?.FullName; }
        catch (EngineeringException) { return null; }
    }

    private static string? TryProjectPath(Project project)
    {
        try { return project.Path?.FullName; }
        catch (EngineeringException) { return null; }
    }

    private static bool PathEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _project = null;
        _portal?.Dispose();
        _portal = null;
        _portalPid = null;
    }
}

internal sealed class ProjectStatusSnapshot
{
    public bool Connected { get; set; }
    public int? PortalProcessId { get; set; }
    public Project? Project { get; set; }
}
