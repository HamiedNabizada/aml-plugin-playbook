namespace Efl.Conversion;

/// <summary>
/// Where a flow meets its nodes.
///
/// The two end points are not stored in the exchange format: a modeler
/// recomputes them from the node geometry, and writing them would show up as
/// stray bends in another tool. They are computed here so that the document
/// carries the same picture the canvas shows.
/// </summary>
internal static class EflGeometry
{
    /// <summary>The full polyline of a flow: docking point, bend points, docking point.</summary>
    internal static List<EflPoint> Polyline(EflModel model, EflFlow flow)
    {
        var source = model.FindNode(flow.SourceId);
        var target = model.FindNode(flow.TargetId);
        if (source?.Bounds == null || target?.Bounds == null) return new List<EflPoint>(flow.Waypoints);

        var towardsSource = flow.Waypoints.Count > 0 ? flow.Waypoints[0] : target.Bounds.Centre;
        var towardsTarget = flow.Waypoints.Count > 0 ? flow.Waypoints[^1] : source.Bounds.Centre;

        var points = new List<EflPoint> { DockingPoint(source, towardsSource) };
        points.AddRange(flow.Waypoints);
        points.Add(DockingPoint(target, towardsTarget));
        return points;
    }

    /// <summary>
    /// Where a line from the centre towards a point leaves the node's outline.
    ///
    /// The outline has to be the one the modeler draws and crops against
    /// (web/src/draw/Renderer.js, getShapePath): a box for a step, the ellipse
    /// inside the box for a store. Cropping a store against its box puts the
    /// stored end point in the air next to the circle, and the document then
    /// disagrees with the canvas for every flow that does not meet the circle
    /// on an axis.
    /// </summary>
    internal static EflPoint DockingPoint(EflNode node, EflPoint towards)
    {
        var bounds = node.Bounds!;
        var centre = bounds.Centre;
        var dx = towards.X - centre.X;
        var dy = towards.Y - centre.Y;
        if (dx == 0 && dy == 0) return centre;

        var halfWidth = bounds.Width / 2;
        var halfHeight = bounds.Height / 2;

        // A document edited by hand can store a zero size. The ellipse formula
        // would divide by zero and write NaN into the file.
        if (halfWidth <= 0 || halfHeight <= 0) return centre;

        double scale;
        if (node.Kind == EflNodeKind.Store)
        {
            // Solve (dx*t/a)^2 + (dy*t/b)^2 = 1 for t.
            scale = 1 / Math.Sqrt(dx * dx / (halfWidth * halfWidth) + dy * dy / (halfHeight * halfHeight));
        }
        else
        {
            // Scale the direction until it touches the nearer pair of edges.
            var scaleX = dx == 0 ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
            var scaleY = dy == 0 ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
            scale = Math.Min(scaleX, scaleY);
        }

        return new EflPoint(Round(centre.X + dx * scale), Round(centre.Y + dy * scale));
    }

    /// <summary>Keeps stored coordinates short without moving anything visibly.</summary>
    private static double Round(double value) => Math.Round(value, 2);
}
