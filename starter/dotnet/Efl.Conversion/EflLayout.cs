namespace Efl.Conversion;

/// <summary>
/// Gives positions to nodes that have none.
///
/// A file written by another tool often carries no layout at all. Without this
/// every node sits at the origin, in one pile, and the diagram is unusable.
/// Nodes that already have bounds are never moved: somebody's layout is theirs.
///
/// The method is the layered one, cut down to what a small language needs:
/// break the cycles, put every node in a column by its longest path from a
/// start node, order the nodes inside a column so that few flows cross, and
/// place them on a grid. Flows that would otherwise be drawn on top of another
/// flow or through a node get bend points: see RouteFlows. AMLPetriNet has the
/// fuller version of the placement.
/// </summary>
public static class EflLayout
{
    public const double NodeWidth = 100;
    public const double NodeHeight = 60;

    private const double ColumnSpacing = 180;
    private const double RowSpacing = 120;
    private const double OriginX = 80;
    private const double OriginY = 80;

    /// <summary>Distance between two lanes above or below the drawing, and of the first lane from it.</summary>
    private const double LaneGap = 30;

    /// <summary>How far a self loop reaches above its node.</summary>
    private const double LoopReach = 20;

    /// <summary>Positions every node without bounds. Returns how many were placed.</summary>
    public static int ArrangeMissing(EflModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var unplaced = model.Nodes.Where(n => n.Bounds == null).ToList();
        if (unplaced.Count == 0) return 0;

        var back = BackEdges(model);
        var column = Columns(model, back);
        var row = Rows(model, column, back);

        foreach (var node in unplaced)
        {
            node.Bounds = new EflBounds(
                OriginX + column[node.Id] * ColumnSpacing,
                OriginY + row[node.Id] * RowSpacing,
                NodeWidth,
                NodeHeight);
        }

        RouteFlows(model, unplaced, column, row, back);

        return unplaced.Count;
    }

    /// <summary>
    /// The flows that close a cycle, found by a depth first search. They are
    /// left out of the layering, because a column number relaxed over a cycle
    /// runs away.
    /// </summary>
    private static HashSet<(string From, string To)> BackEdges(EflModel model)
    {
        var successors = model.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var flow in model.Flows)
            if (successors.ContainsKey(flow.SourceId) && successors.ContainsKey(flow.TargetId))
                successors[flow.SourceId].Add(flow.TargetId);

        var back = new HashSet<(string, string)>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 unseen, 1 on the stack, 2 done

        // Deterministic order, so the same model always comes out the same way.
        // Nodes nothing points at come first: that is where a reader starts.
        var hasIncoming = model.Flows.Select(f => f.TargetId).ToHashSet(StringComparer.Ordinal);
        var roots = model.Nodes
            .OrderBy(n => hasIncoming.Contains(n.Id) ? 1 : 0)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => n.Id);

        void Visit(string id)
        {
            state[id] = 1;
            foreach (var next in successors[id].OrderBy(s => s, StringComparer.Ordinal))
            {
                var seen = state.GetValueOrDefault(next);
                if (seen == 1) back.Add((id, next));
                else if (seen == 0) Visit(next);
            }
            state[id] = 2;
        }

        foreach (var root in roots)
            if (state.GetValueOrDefault(root) == 0) Visit(root);

        return back;
    }

    /// <summary>The column of each node: its longest path from a node nothing feeds.</summary>
    private static Dictionary<string, int> Columns(EflModel model, HashSet<(string From, string To)> back)
    {
        var column = model.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var successors = model.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var waiting = model.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);

        foreach (var flow in model.Flows)
        {
            if (!column.ContainsKey(flow.SourceId) || !column.ContainsKey(flow.TargetId)) continue;
            if (flow.SourceId == flow.TargetId || back.Contains((flow.SourceId, flow.TargetId))) continue;

            successors[flow.SourceId].Add(flow.TargetId);
            waiting[flow.TargetId]++;
        }

        var ready = new Queue<string>(waiting.Where(e => e.Value == 0).Select(e => e.Key).OrderBy(id => id, StringComparer.Ordinal));
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            foreach (var next in successors[id])
            {
                if (column[next] < column[id] + 1) column[next] = column[id] + 1;
                if (--waiting[next] == 0) ready.Enqueue(next);
            }
        }

        return column;
    }

    /// <summary>
    /// The row of each node inside its column. Every node moves to the average
    /// row of the nodes it is connected to in the column on its left, and the
    /// result is made distinct again. Two sweeps are enough for a small model.
    /// </summary>
    private static Dictionary<string, int> Rows(
        EflModel model, Dictionary<string, int> column, HashSet<(string From, string To)> back)
    {
        var byColumn = model.Nodes
            .GroupBy(n => column[n.Id])
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).OrderBy(id => id, StringComparer.Ordinal).ToList());

        var left = model.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var flow in model.Flows)
        {
            if (!left.ContainsKey(flow.SourceId) || !left.ContainsKey(flow.TargetId)) continue;
            if (back.Contains((flow.SourceId, flow.TargetId))) continue;
            if (column[flow.SourceId] >= column[flow.TargetId]) continue;
            left[flow.TargetId].Add(flow.SourceId);
        }

        var row = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, ids) in byColumn)
            for (var i = 0; i < ids.Count; i++) row[ids[i]] = i;

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var index in byColumn.Keys.OrderBy(c => c))
            {
                var ordered = byColumn[index]
                    .Select(id => (Id: id, Key: left[id].Count == 0 ? row[id] : left[id].Average(n => (double)row[n])))
                    .OrderBy(entry => entry.Key)
                    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => entry.Id)
                    .ToList();

                byColumn[index] = ordered;
                for (var i = 0; i < ordered.Count; i++) row[ordered[i]] = i;
            }
        }

        return row;
    }

    /// <summary>
    /// Gives bend points to the flows a grid would draw badly.
    ///
    /// Placing nodes is not enough. A flow that closes a cycle, drawn straight,
    /// runs exactly on top of the forward flow between the same two nodes, and
    /// the reader sees one line where there are two in opposite directions. A
    /// flow that skips a column runs straight through the node in between. A
    /// flow from a node to itself has no length at all and disappears. Most
    /// graphical languages are cyclic, so all three turn up in the first real
    /// file.
    ///
    /// Back flows leave their source to the right, run along a lane below the
    /// drawing and come up into their target from the left. Forward flows whose
    /// straight line would cross a node do the same above the drawing. Every
    /// routed flow gets its own lane and its own vertical channels in the gaps
    /// between columns, where no node is placed, so two routed flows never share
    /// a segment. Routes meet a node a quarter of its height off the centre
    /// line, because a straight flow to the neighbouring column runs along that
    /// centre line and a route meeting the node there would overprint it.
    ///
    /// Only flows between two nodes this call placed, and only flows without
    /// bend points of their own: a route somebody drew is theirs, and around a
    /// node that already had a position the gaps are not known to be free.
    /// </summary>
    private static void RouteFlows(
        EflModel model,
        List<EflNode> unplaced,
        Dictionary<string, int> column,
        Dictionary<string, int> row,
        HashSet<(string From, string To)> back)
    {
        var placed = unplaced.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);

        // Sorted by where the flow sits in the drawing, not by list position, so
        // the lanes do not change when the same flows arrive in another order.
        var candidates = model.Flows
            .Where(f => f.Waypoints.Count == 0 && placed.ContainsKey(f.SourceId) && placed.ContainsKey(f.TargetId))
            .OrderBy(f => column[f.SourceId])
            .ThenBy(f => row[f.SourceId])
            .ThenBy(f => column[f.TargetId])
            .ThenBy(f => row[f.TargetId])
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        var routes = new List<Route>();
        foreach (var flow in candidates)
        {
            var from = column[flow.SourceId];
            var to = column[flow.TargetId];

            if (flow.SourceId == flow.TargetId)
                routes.Add(new Route(flow, RouteKind.SelfLoop, from + 1, -1));
            else if (to <= from || back.Contains((flow.SourceId, flow.TargetId)))
                routes.Add(new Route(flow, RouteKind.Below, from + 1, to));
            else if (CrossesANode(model, flow))
                routes.Add(new Route(flow, RouteKind.Above, from + 1, to));
        }
        if (routes.Count == 0) return;

        var channelX = AssignChannels(routes);

        // The lanes lie outside every node, including nodes that already had a
        // position, so a lane never cuts through somebody's layout either.
        var bounded = model.Nodes.Where(n => n.Bounds != null).Select(n => n.Bounds!).ToList();
        var top = bounded.Min(b => b.Y);
        var bottom = bounded.Max(b => b.Y + b.Height);
        var lanesAbove = 0;
        var lanesBelow = 0;
        var loopsPerNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var offset = NodeHeight / 4;

        for (var i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            var source = placed[route.Flow.SourceId].Bounds!;
            var target = placed[route.Flow.TargetId].Bounds!;
            var points = route.Flow.Waypoints;
            var (sourceX, targetX) = channelX[i];

            switch (route.Kind)
            {
                case RouteKind.SelfLoop:
                {
                    // Out of the right side, up past the top edge, down into the
                    // top. A second loop on the same node reaches higher, so the
                    // two do not share their top segment.
                    var count = loopsPerNode.GetValueOrDefault(route.Flow.SourceId) + 1;
                    loopsPerNode[route.Flow.SourceId] = count;
                    var y = source.Y - LoopReach * count;
                    points.Add(new EflPoint(sourceX, source.Centre.Y - offset));
                    points.Add(new EflPoint(sourceX, y));
                    points.Add(new EflPoint(source.Centre.X, y));
                    break;
                }
                case RouteKind.Below:
                {
                    var y = bottom + LaneGap * ++lanesBelow;
                    points.Add(new EflPoint(sourceX, source.Centre.Y + offset));
                    points.Add(new EflPoint(sourceX, y));
                    points.Add(new EflPoint(targetX, y));
                    points.Add(new EflPoint(targetX, target.Centre.Y + offset));
                    break;
                }
                case RouteKind.Above:
                {
                    var y = top - LaneGap * ++lanesAbove;
                    points.Add(new EflPoint(sourceX, source.Centre.Y - offset));
                    points.Add(new EflPoint(sourceX, y));
                    points.Add(new EflPoint(targetX, y));
                    points.Add(new EflPoint(targetX, target.Centre.Y - offset));
                    break;
                }
            }
        }
    }

    private enum RouteKind
    {
        SelfLoop,
        Below,
        Above,
    }

    /// <summary>
    /// A flow to route and the column gaps its vertical legs run in. Gap g lies
    /// left of column g; a self loop has no target leg and carries -1.
    /// </summary>
    private sealed record Route(EflFlow Flow, RouteKind Kind, int SourceGap, int TargetGap);

    /// <summary>
    /// The x of every vertical leg. Each leg gets its own slot in its gap, and
    /// the slots are spread evenly over the gap, so a busy gap packs its legs
    /// closer instead of pushing the last one into the next column's nodes.
    /// </summary>
    private static List<(double Source, double Target)> AssignChannels(List<Route> routes)
    {
        var legs = new List<(int Route, bool IsTarget, int Gap)>();
        for (var i = 0; i < routes.Count; i++)
        {
            legs.Add((i, false, routes[i].SourceGap));
            if (routes[i].TargetGap >= 0) legs.Add((i, true, routes[i].TargetGap));
        }

        var result = routes.Select(_ => (Source: 0.0, Target: 0.0)).ToList();
        var gapWidth = ColumnSpacing - NodeWidth;
        foreach (var gap in legs.GroupBy(l => l.Gap))
        {
            var inGap = gap.ToList();
            var step = gapWidth / (inGap.Count + 1);
            var left = OriginX + (gap.Key - 1) * ColumnSpacing + NodeWidth;
            for (var slot = 0; slot < inGap.Count; slot++)
            {
                var (index, isTarget, _) = inGap[slot];
                var x = left + step * (slot + 1);
                result[index] = isTarget ? (result[index].Source, x) : (x, result[index].Target);
            }
        }
        return result;
    }

    /// <summary>
    /// Whether the straight line of a flow runs through a node it does not
    /// connect. Checked for every forward flow, not only for flows that skip a
    /// column: a steep line to a far row of the next column clips a neighbour
    /// of its own source as well.
    /// </summary>
    private static bool CrossesANode(EflModel model, EflFlow flow)
    {
        var line = EflGeometry.Polyline(model, flow);
        return model.Nodes.Any(n =>
            n.Bounds != null && n.Id != flow.SourceId && n.Id != flow.TargetId
            && SegmentEntersBox(line[0], line[^1], n.Bounds));
    }

    /// <summary>
    /// Liang and Barsky clipping against the open box. A line that only grazes
    /// an edge does not count: it is drawn beside the node, not through it.
    /// </summary>
    internal static bool SegmentEntersBox(EflPoint a, EflPoint b, EflBounds box)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        double enter = 0, leave = 1;

        bool Clip(double p, double q)
        {
            if (p == 0) return q > 0;
            var t = q / p;
            if (p < 0) { if (t > enter) enter = t; }
            else if (t < leave) leave = t;
            return enter < leave;
        }

        return Clip(-dx, a.X - box.X)
            && Clip(dx, box.X + box.Width - a.X)
            && Clip(-dy, a.Y - box.Y)
            && Clip(dy, box.Y + box.Height - a.Y);
    }
}
