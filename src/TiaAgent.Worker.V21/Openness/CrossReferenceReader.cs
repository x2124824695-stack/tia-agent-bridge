using Siemens.Engineering;
using Siemens.Engineering.CrossReference;

namespace TiaAgent.Worker.V21.Openness;

internal static class CrossReferenceReader
{
    public static object Read(Project project, string path, int limit)
    {
        if (limit < 1 || limit > 5000) throw new ArgumentException("limit must be 1..5000.");
        var target = ObjectCatalog.Resolve(project, path);
        var provider = target.Value as IEngineeringServiceProvider ?? throw new InvalidOperationException("Target does not provide engineering services.");
        var service = provider.GetService<CrossReferenceService>() ?? throw new InvalidOperationException("Cross-reference service is unavailable for this target.");
        var result = service.GetCrossReferences(CrossReferenceFilter.AllObjects);
        var rows = new List<object>();
        var complete = true;
        Walk(result.Sources, rows, limit, ref complete, 0);
        return new { path, complete, references = rows, semantics = "Native Siemens cross-reference data; compile first if the engineering index is stale." };
    }

    private static void Walk(SourceObjectComposition sources, List<object> rows, int limit, ref bool complete, int depth)
    {
        if (depth > 64) { complete = false; return; }
        foreach (SourceObject source in sources)
        {
            foreach (ReferenceObject reference in source.References)
            {
                if (rows.Count >= limit) { complete = false; return; }
                var locations = reference.Locations.Take(101).ToList();
                if (locations.Count > 100) complete = false;
                rows.Add(new {
                    source = new { source.Name, source.Path, source.Address, source.TypeName, source.Device },
                    reference = new { reference.Name, reference.Path, reference.Address, reference.TypeName, reference.Device },
                    locations = locations.Take(100).Select(l => new { l.Name, l.ReferenceLocation, access = l.Access.ToString(), referenceType = l.ReferenceType.ToString() }).ToList()
                });
            }
            Walk(source.Children, rows, limit, ref complete, depth + 1);
        }
    }
}
