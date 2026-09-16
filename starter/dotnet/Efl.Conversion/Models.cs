namespace Efl.Conversion;

/// <summary>
/// The language as plain objects. Both converters work on these, so neither
/// the JSON format nor CAEX leaks into the other side.
///
/// Keep this model free of anything that belongs to one of the two formats.
/// The moment a CAEX id or a JSON field name appears here, the two converters
/// stop being independent and a change on one side breaks the other.
/// </summary>
public sealed class EflModel
{
    /// <summary>Identifier of the diagram itself, used as the hierarchy name when none is given.</summary>
    public string Id { get; set; } = "diagram";

    public string? Name { get; set; }

    public List<EflNode> Nodes { get; } = new();

    public List<EflFlow> Flows { get; } = new();

    /// <summary>
    /// What reading left out, in the words a user can act on. Nothing in this
    /// library throws because a file is odd; it reports and carries on.
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Ids of elements the document holds but the reader could not show, such
    /// as a flow element whose links do not resolve. An update must not read
    /// their absence from the diagram as a deletion: the user never saw them.
    /// AMLPetriNet lost arcs this way before it kept such a set.
    /// </summary>
    public HashSet<string> Unresolved { get; } = new(StringComparer.Ordinal);

    public EflNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);
}

public enum EflNodeKind
{
    Step,
    Store,
}

public sealed class EflNode
{
    public string Id { get; set; } = "";

    public string? Name { get; set; }

    public EflNodeKind Kind { get; set; }

    /// <summary>Seconds for a Step, items for a Store. Null when the model does not say.</summary>
    public double? Value { get; set; }

    public EflBounds? Bounds { get; set; }
}

public sealed class EflFlow
{
    public string Id { get; set; } = "";

    public string? Name { get; set; }

    public string SourceId { get; set; } = "";

    public string TargetId { get; set; } = "";

    /// <summary>Bend points between the two nodes, without the docking points.</summary>
    public List<EflPoint> Waypoints { get; } = new();
}

public sealed record EflPoint(double X, double Y);

public sealed record EflBounds(double X, double Y, double Width, double Height)
{
    public EflPoint Centre => new(X + Width / 2, Y + Height / 2);
}
