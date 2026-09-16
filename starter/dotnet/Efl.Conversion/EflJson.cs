using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Efl.Conversion;

/// <summary>
/// The exchange format between the modeler and this mapper.
///
/// EFL uses a format of its own because it has no standard one. A language that
/// has a standard format should use it instead, as AMLPetriNet does with PNML:
/// the same bytes then travel to any other tool. The rules below hold either
/// way.
///
/// Shape of the file:
/// <code>
/// {
///   "formatVersion": 1,
///   "id": "diagram-1",
///   "name": "Bottling",
///   "nodes": [ { "id": "s1", "type": "Step", "name": "Fill",
///                "value": 12, "bounds": { "x": 0, "y": 0, "width": 100, "height": 60 } } ],
///   "flows": [ { "id": "f1", "source": "s1", "target": "b1",
///                "waypoints": [ { "x": 120, "y": 30 } ] } ]
/// }
/// </code>
///
/// Rules that are worth keeping in any format:
/// <list type="bullet">
/// <item>Every element carries its own id. Without it nothing can be matched on
/// a second visit and an update degenerates into a rebuild.</item>
/// <item>Layout is part of the format. A diagram that loses its layout on the
/// way is not a round trip.</item>
/// <item>A reader is tolerant and reports, a writer is strict. Anything the
/// reader drops lands in <see cref="EflModel.Warnings"/>.</item>
/// <item>The file says which version of the format it is. A reader meeting a
/// newer version reads what it knows and says so, instead of silently dropping
/// what a later version added.</item>
/// </list>
/// </summary>
public static class EflJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// The version this code writes. Raise it when a change means an older reader
    /// would lose something; keep the reader able to read every older version.
    /// </summary>
    public const int FormatVersion = 1;

    public static EflModel Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("The model is not well formed JSON: " + e.Message, e);
        }

        if (root is not JsonObject document)
            throw new FormatException("The model has to be a JSON object.");

        var model = new EflModel
        {
            Id = Text(document["id"]) ?? "diagram",
            Name = Text(document["name"]),
        };

        // A file without the field predates it and is version 1.
        var version = Number(document["formatVersion"]) ?? 1;
        if (version > FormatVersion)
            model.Warnings.Add($"The file is format version {version}; this reader knows version {FormatVersion}. " +
                               "Anything a newer version added is not read and would be lost on writing.");

        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in Array(document["nodes"]))
        {
            var id = Text(node?["id"]);
            if (id == null)
            {
                model.Warnings.Add("A node without an id was left out.");
                continue;
            }
            if (!known.Add(id))
            {
                model.Warnings.Add($"The id '{id}' is used more than once; the later node was left out.");
                continue;
            }

            var kind = Text(node?["type"]);
            if (!Enum.TryParse<EflNodeKind>(kind, ignoreCase: true, out var parsedKind))
            {
                model.Warnings.Add($"Node '{id}' has the unknown type '{kind}' and was left out.");
                continue;
            }

            model.Nodes.Add(new EflNode
            {
                Id = id,
                Name = Text(node?["name"]),
                Kind = parsedKind,
                Value = Number(node?["value"]),
                Bounds = ReadBounds(node?["bounds"]),
            });
        }

        foreach (var flow in Array(document["flows"]))
        {
            var id = Text(flow?["id"]);
            var source = Text(flow?["source"]);
            var target = Text(flow?["target"]);

            if (id == null || source == null || target == null)
            {
                model.Warnings.Add("A flow without an id, a source or a target was left out.");
                continue;
            }
            if (model.FindNode(source) == null || model.FindNode(target) == null)
            {
                // Said out loud rather than dropped silently: a flow that ends
                // nowhere is usually a sign that something else went missing.
                model.Warnings.Add($"Flow '{id}' runs between nodes the model does not have and was left out.");
                continue;
            }

            var read = new EflFlow
            {
                Id = id,
                Name = Text(flow?["name"]),
                SourceId = source,
                TargetId = target,
            };
            foreach (var point in Array(flow?["waypoints"]))
            {
                var x = Number(point?["x"]);
                var y = Number(point?["y"]);
                if (x != null && y != null) read.Waypoints.Add(new EflPoint(x.Value, y.Value));
            }
            model.Flows.Add(read);
        }

        return model;
    }

    public static string Write(EflModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var nodes = new JsonArray();
        foreach (var node in model.Nodes)
        {
            var written = new JsonObject
            {
                ["id"] = node.Id,
                ["type"] = node.Kind.ToString(),
            };
            if (node.Name != null) written["name"] = node.Name;
            if (node.Value != null) written["value"] = node.Value.Value;
            if (node.Bounds != null)
            {
                written["bounds"] = new JsonObject
                {
                    ["x"] = node.Bounds.X,
                    ["y"] = node.Bounds.Y,
                    ["width"] = node.Bounds.Width,
                    ["height"] = node.Bounds.Height,
                };
            }
            nodes.Add(written);
        }

        var flows = new JsonArray();
        foreach (var flow in model.Flows)
        {
            var written = new JsonObject
            {
                ["id"] = flow.Id,
                ["source"] = flow.SourceId,
                ["target"] = flow.TargetId,
            };
            if (flow.Name != null) written["name"] = flow.Name;
            if (flow.Waypoints.Count > 0)
            {
                var points = new JsonArray();
                foreach (var point in flow.Waypoints)
                    points.Add(new JsonObject { ["x"] = point.X, ["y"] = point.Y });
                written["waypoints"] = points;
            }
            flows.Add(written);
        }

        // Same key order as the modeler's export (web/src/io/json.js), so a file
        // written on either side diffs cleanly against the other.
        var document = new JsonObject { ["formatVersion"] = FormatVersion, ["id"] = model.Id };
        if (model.Name != null) document["name"] = model.Name;
        document["nodes"] = nodes;
        document["flows"] = flows;

        return document.ToJsonString(Options);
    }

    private static IEnumerable<JsonNode?> Array(JsonNode? node) =>
        node is JsonArray array ? array : Enumerable.Empty<JsonNode?>();

    private static string? Text(JsonNode? node)
    {
        var value = node?.GetValue<object?>()?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double? Number(JsonNode? node)
    {
        if (node == null) return null;
        var text = node.ToString();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;
    }

    private static EflBounds? ReadBounds(JsonNode? node)
    {
        if (node == null) return null;

        var x = Number(node["x"]);
        var y = Number(node["y"]);
        if (x == null || y == null) return null;

        // A size of zero draws an invisible element, so a missing or absurd
        // size falls back to the default the modeler uses.
        var width = Number(node["width"]) is { } w and > 0 ? w : 100;
        var height = Number(node["height"]) is { } h and > 0 ? h : 60;
        return new EflBounds(x.Value, y.Value, width, height);
    }
}
