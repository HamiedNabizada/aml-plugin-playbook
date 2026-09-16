using Efl.Conversion;
using Xunit;

namespace Efl.Tests;

/// <summary>
/// One broken model per rule, and a check that the sample breaks none.
///
/// The last test keeps the list honest: a rule that exists but has no test
/// here fails the build, so a rule cannot be added and forgotten.
/// </summary>
public class ValidatorTests
{
    private static List<string> Rules(EflModel model) =>
        EflValidator.Validate(model).Select(f => f.Rule).Distinct().OrderBy(r => r).ToList();

    private static readonly Dictionary<string, Func<EflModel>> Broken = new()
    {
        ["EFL01"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Flows[0].Id = "fill";
            return model;
        },
        ["EFL02"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Flows[0].Id = "";
            return model;
        },
        ["EFL03"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Flows[0].TargetId = "nowhere";
            return model;
        },
        ["EFL04"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Flows[0].TargetId = model.Flows[0].SourceId;
            return model;
        },
        ["EFL05"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Flows.Add(new EflFlow { Id = "f1b", SourceId = "fill", TargetId = "buffer" });
            return model;
        },
        ["EFL06"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Nodes[1].Value = -1;
            return model;
        },
        ["EFL07"] = () =>
        {
            var model = RoundTripTests.Sample();
            model.Nodes.Add(new EflNode { Id = "spare", Kind = EflNodeKind.Store, Bounds = new EflBounds(0, 300, 100, 60) });
            return model;
        },
    };

    [Fact]
    public void TheSampleBreaksNoRule() =>
        Assert.Empty(EflValidator.Validate(RoundTripTests.Sample()));

    public static IEnumerable<object[]> RuleIds => EflValidator.RuleIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void EachRuleFiresOnItsOwnBrokenModelAndOnlyThere(string rule)
    {
        Assert.True(Broken.ContainsKey(rule), $"No broken model for {rule}; add one to {nameof(Broken)}.");
        var fired = Rules(Broken[rule]());

        Assert.Contains(rule, fired);
        // EFL02 leaves a flow without an id, which some other rules may also see;
        // every other broken model has to trip its own rule alone.
        if (rule != "EFL02") Assert.Equal(new[] { rule }, fired);
    }

    [Fact]
    public void EveryFindingNamesTheElementSoTheCanvasCanSelectIt()
    {
        foreach (var (rule, build) in Broken)
        {
            if (rule == "EFL02") continue; // the element has no id to name
            Assert.All(EflValidator.Validate(build()), f => Assert.False(string.IsNullOrEmpty(f.ElementId), f.ToString()));
        }
    }
}
