namespace Efl.Conversion;

public enum EflSeverity
{
    /// <summary>The model breaks a rule of the language.</summary>
    Error,

    /// <summary>Allowed, but almost certainly not what the author meant.</summary>
    Warning,
}

/// <summary>One finding. The element id is the language's own, so a finding can select the element on a canvas.</summary>
public sealed record EflFinding(string Rule, EflSeverity Severity, string Message, string? ElementId = null)
{
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()} {Rule}: {Message}";
}

/// <summary>
/// The structural rules of the language, checked on the model rather than on a
/// document. A model that came from the modeler and never reached AML is
/// checked the same way as one read back from a file.
///
/// Rules a drawing tool can break without noticing are what belongs here.
/// Modelling advice does not: a rule that fires on a perfectly good model
/// teaches people to ignore the list.
/// </summary>
public static class EflValidator
{
    public static IReadOnlyList<EflFinding> Validate(EflModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var findings = new List<EflFinding>();
        findings.AddRange(CheckIdentifiers(model));
        findings.AddRange(CheckFlows(model));
        findings.AddRange(CheckValues(model));
        findings.AddRange(CheckConnectedness(model));
        return findings;
    }

    /// <summary>The ids of every rule, so a report can say which ones passed.</summary>
    public static IReadOnlyList<string> RuleIds { get; } =
        new[] { "EFL01", "EFL02", "EFL03", "EFL04", "EFL05", "EFL06", "EFL07" };

    private static IEnumerable<EflFinding> CheckIdentifiers(EflModel model)
    {
        var all = model.Nodes.Select(n => n.Id).Concat(model.Flows.Select(f => f.Id)).ToList();

        foreach (var duplicate in all.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            yield return new EflFinding("EFL01", EflSeverity.Error,
                $"The id '{duplicate.Key}' is used by {duplicate.Count()} elements; ids have to be unique.",
                duplicate.Key);

        foreach (var empty in all.Where(string.IsNullOrWhiteSpace))
            yield return new EflFinding("EFL02", EflSeverity.Error, "An element has no id.", empty);
    }

    private static IEnumerable<EflFinding> CheckFlows(EflModel model)
    {
        foreach (var flow in model.Flows)
        {
            if (model.FindNode(flow.SourceId) == null)
                yield return new EflFinding("EFL03", EflSeverity.Error,
                    $"Flow '{flow.Id}' starts at '{flow.SourceId}', which is not a node of this diagram.", flow.Id);

            if (model.FindNode(flow.TargetId) == null)
                yield return new EflFinding("EFL03", EflSeverity.Error,
                    $"Flow '{flow.Id}' ends at '{flow.TargetId}', which is not a node of this diagram.", flow.Id);

            if (flow.SourceId == flow.TargetId)
                yield return new EflFinding("EFL04", EflSeverity.Error,
                    $"Flow '{flow.Id}' starts and ends at the same node.", flow.Id);
        }

        foreach (var parallel in model.Flows
                     .GroupBy(f => (f.SourceId, f.TargetId))
                     .Where(g => g.Count() > 1))
        {
            yield return new EflFinding("EFL05", EflSeverity.Warning,
                $"{parallel.Count()} flows run from '{parallel.Key.SourceId}' to '{parallel.Key.TargetId}'.",
                parallel.First().Id);
        }
    }

    private static IEnumerable<EflFinding> CheckValues(EflModel model)
    {
        foreach (var node in model.Nodes.Where(n => n.Value is < 0))
        {
            var what = node.Kind == EflNodeKind.Step ? "duration" : "capacity";
            yield return new EflFinding("EFL06", EflSeverity.Error,
                $"'{node.Id}' has a negative {what}.", node.Id);
        }
    }

    private static IEnumerable<EflFinding> CheckConnectedness(EflModel model)
    {
        if (model.Nodes.Count < 2) yield break;

        var touched = model.Flows
            .SelectMany(f => new[] { f.SourceId, f.TargetId })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in model.Nodes.Where(n => !touched.Contains(n.Id)))
            yield return new EflFinding("EFL07", EflSeverity.Warning,
                $"'{node.Id}' is not connected to anything.", node.Id);
    }
}
