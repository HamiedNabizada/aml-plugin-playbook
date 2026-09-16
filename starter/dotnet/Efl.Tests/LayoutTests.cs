using Efl.Conversion;
using Xunit;

namespace Efl.Tests;

/// <summary>
/// The arranged picture, checked on the flows and not only on the nodes.
///
/// A test that only asks whether nodes overlap passed while a back flow lay
/// exactly on top of the forward flow it returns along, and while a flow that
/// skipped a column ran through the node in between. These tests look at the
/// lines a modeler would draw.
/// </summary>
public class LayoutTests
{
    private static EflModel Unplaced(string[] nodes, params (string Id, string From, string To)[] flows)
    {
        var model = new EflModel();
        foreach (var id in nodes) model.Nodes.Add(new EflNode { Id = id, Name = id, Kind = EflNodeKind.Step });
        foreach (var (id, from, to) in flows) model.Flows.Add(new EflFlow { Id = id, SourceId = from, TargetId = to });
        return model;
    }

    /// <summary>
    /// Whether a segment runs through the inside of a box. Sampled rather than
    /// solved, so the test does not share its arithmetic with the code under test.
    /// </summary>
    private static bool Enters(EflPoint a, EflPoint b, EflBounds box)
    {
        const int samples = 400;
        const double margin = 0.5;
        for (var i = 0; i <= samples; i++)
        {
            var x = a.X + (b.X - a.X) * i / samples;
            var y = a.Y + (b.Y - a.Y) * i / samples;
            if (x > box.X + margin && x < box.X + box.Width - margin
                && y > box.Y + margin && y < box.Y + box.Height - margin) return true;
        }
        return false;
    }

    private static IEnumerable<(EflPoint A, EflPoint B)> Segments(List<EflPoint> line) =>
        line.Zip(line.Skip(1), (a, b) => (a, b)).Where(s => s.a != s.b);

    /// <summary>Two segments on one line that overlap over some length: drawn, they are one stroke.</summary>
    private static bool Share((EflPoint A, EflPoint B) s, (EflPoint A, EflPoint B) t)
    {
        const double epsilon = 1e-6;
        static double Cross(EflPoint o, EflPoint p, EflPoint q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        if (Math.Abs(Cross(s.A, s.B, t.A)) > epsilon || Math.Abs(Cross(s.A, s.B, t.B)) > epsilon) return false;

        var dx = s.B.X - s.A.X;
        var dy = s.B.Y - s.A.Y;
        var length = dx * dx + dy * dy;
        double Along(EflPoint p) => ((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / length;

        var (t0, t1) = (Along(t.A), Along(t.B));
        var low = Math.Max(0, Math.Min(t0, t1));
        var high = Math.Min(1, Math.Max(t0, t1));
        return (high - low) * Math.Sqrt(length) > epsilon;
    }

    private static void AssertNoFlowCrossesANode(EflModel model)
    {
        foreach (var flow in model.Flows)
        {
            var line = EflGeometry.Polyline(model, flow);
            foreach (var node in model.Nodes.Where(n => n.Id != flow.SourceId && n.Id != flow.TargetId))
                Assert.False(
                    Segments(line).Any(s => Enters(s.A, s.B, node.Bounds!)),
                    $"flow {flow.Id} runs through node {node.Id}");
        }
    }

    private static void AssertNoTwoFlowsShareASegment(EflModel model)
    {
        var lines = model.Flows.Select(f => (f.Id, Segments: Segments(EflGeometry.Polyline(model, f)).ToList())).ToList();
        for (var i = 0; i < lines.Count; i++)
            for (var j = i + 1; j < lines.Count; j++)
                Assert.False(
                    lines[i].Segments.Any(s => lines[j].Segments.Any(t => Share(s, t))),
                    $"flows {lines[i].Id} and {lines[j].Id} share a segment");
    }

    [Fact]
    public void AnArrangedCycleHasNoFlowThroughANodeAndNoTwoFlowsOnOneLine()
    {
        var model = Unplaced(new[] { "A", "B", "C" }, ("ab", "A", "B"), ("bc", "B", "C"), ("ca", "C", "A"));

        EflLayout.ArrangeMissing(model);

        Assert.NotEmpty(model.Flows.Single(f => f.Id == "ca").Waypoints);
        AssertNoFlowCrossesANode(model);
        AssertNoTwoFlowsShareASegment(model);
    }

    [Fact]
    public void AFlowBackAlongTheSameTwoNodesIsNotDrawnOnTopOfTheForwardFlow()
    {
        // The case seen in the trial run: two flows in opposite directions
        // between the same nodes, overprinted into one line.
        var model = Unplaced(new[] { "A", "B" }, ("ab", "A", "B"), ("ba", "B", "A"));

        EflLayout.ArrangeMissing(model);

        AssertNoFlowCrossesANode(model);
        AssertNoTwoFlowsShareASegment(model);
    }

    [Fact]
    public void SeveralBackFlowsGetLanesOfTheirOwn()
    {
        var model = Unplaced(
            new[] { "A", "B", "C", "D" },
            ("ab", "A", "B"), ("bc", "B", "C"), ("cd", "C", "D"),
            ("da", "D", "A"), ("db", "D", "B"), ("ca", "C", "A"), ("ac", "A", "C"));

        EflLayout.ArrangeMissing(model);

        AssertNoFlowCrossesANode(model);
        AssertNoTwoFlowsShareASegment(model);
    }

    [Fact]
    public void AFlowThatSkipsAColumnDoesNotCrossTheNodeInBetween()
    {
        var model = Unplaced(new[] { "A", "B", "C" }, ("ab", "A", "B"), ("bc", "B", "C"), ("ac", "A", "C"));

        EflLayout.ArrangeMissing(model);

        // Without routing the three nodes sit in one row and ac runs through B.
        var a = model.FindNode("A")!.Bounds!;
        var c = model.FindNode("C")!.Bounds!;
        Assert.True(Enters(a.Centre, c.Centre, model.FindNode("B")!.Bounds!), "the test no longer sets up a crossing");

        Assert.NotEmpty(model.Flows.Single(f => f.Id == "ac").Waypoints);
        AssertNoFlowCrossesANode(model);
        AssertNoTwoFlowsShareASegment(model);
    }

    [Fact]
    public void ASelfLoopGetsBendPointsAndALineWithLength()
    {
        var model = Unplaced(new[] { "A", "B" }, ("ab", "A", "B"), ("aa", "A", "A"));

        EflLayout.ArrangeMissing(model);

        var loop = model.Flows.Single(f => f.Id == "aa");
        Assert.Equal(3, loop.Waypoints.Count);

        var line = EflGeometry.Polyline(model, loop);
        var length = Segments(line).Sum(s => Math.Sqrt(Math.Pow(s.B.X - s.A.X, 2) + Math.Pow(s.B.Y - s.A.Y, 2)));
        Assert.True(length > 0);

        AssertNoFlowCrossesANode(model);
        AssertNoTwoFlowsShareASegment(model);
    }

    [Fact]
    public void FlowsBetweenNodesThatAlreadyHadPositionsKeepTheirWaypoints()
    {
        var model = new EflModel();
        model.Nodes.Add(new EflNode { Id = "A", Kind = EflNodeKind.Step, Bounds = new EflBounds(0, 0, 100, 60) });
        model.Nodes.Add(new EflNode { Id = "B", Kind = EflNodeKind.Step, Bounds = new EflBounds(200, 0, 100, 60) });
        model.Nodes.Add(new EflNode { Id = "C", Kind = EflNodeKind.Step });
        model.Flows.Add(new EflFlow { Id = "ab", SourceId = "A", TargetId = "B" });
        model.Flows.Add(new EflFlow { Id = "ba", SourceId = "B", TargetId = "A" });
        model.Flows.Add(new EflFlow { Id = "bb", SourceId = "B", TargetId = "B" });
        model.Flows.Add(new EflFlow { Id = "bc", SourceId = "B", TargetId = "C" });
        model.Flows.Add(new EflFlow { Id = "cb", SourceId = "C", TargetId = "B" });

        var drawn = new EflFlow { Id = "cc", SourceId = "C", TargetId = "C" };
        drawn.Waypoints.Add(new EflPoint(1, 2));
        model.Flows.Add(drawn);

        Assert.Equal(1, EflLayout.ArrangeMissing(model));

        foreach (var id in new[] { "ab", "ba", "bb", "bc", "cb" })
            Assert.Empty(model.Flows.Single(f => f.Id == id).Waypoints);
        Assert.Equal(new[] { new EflPoint(1, 2) }, drawn.Waypoints);
    }

    [Fact]
    public void ArrangingTheSameModelTwiceGivesTheSameDocument()
    {
        static EflModel Build() => Unplaced(
            new[] { "A", "B", "C", "D" },
            ("ab", "A", "B"), ("bc", "B", "C"), ("cd", "C", "D"), ("ac", "A", "C"),
            ("da", "D", "A"), ("cb", "C", "B"), ("dd", "D", "D"));

        var first = Build();
        var second = Build();
        EflLayout.ArrangeMissing(first);
        EflLayout.ArrangeMissing(second);

        Assert.Equal(EflJson.Write(first), EflJson.Write(second));
    }
}
