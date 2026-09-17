using Efl.Conversion;
using Xunit;

namespace Efl.Tests;

/// <summary>
/// Ties the .NET side to the files and the drawing the web side uses.
/// </summary>
public class ExampleAndGeometryTests
{
    /// <summary>
    /// Finds the starter's examples folder from the test output directory.
    /// Walking up until a marker folder appears works in a fresh clone, in an
    /// IDE and in CI alike; a path relative to the working directory does not.
    /// </summary>
    internal static string ExampleFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "examples", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"examples/{name} was not found above {AppContext.BaseDirectory}.");
    }

    [Fact]
    public void TheExampleFileTheBrowserTestsUseIsTheSampleModel()
    {
        // web/tools/verify-*.mjs load this file; the round trip tests build the
        // same model in code. If the two drift apart, the two halves of the
        // stack are no longer tested on the same diagram.
        var fromFile = EflJson.Read(File.ReadAllText(ExampleFile("bottling-line.json")));

        Assert.Empty(fromFile.Warnings);
        Assert.Equal(EflJson.Write(RoundTripTests.Sample()), EflJson.Write(fromFile));
    }

    [Fact]
    public void AFixedWritingTimeIsWrittenTheSameInEveryTimeZone()
    {
        // The CI check that examples are current compares bytes. Written through
        // Aml.Engine's property, the time came out in the machine's zone.
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        foreach (var document in new[] { EflToCaex.Convert(RoundTripTests.Sample(), writtenAt: at), EflLibraries.CreateArtefact(at) })
        {
            var written = document.CAEXFile.SourceDocumentInformation.Single().Node.Attribute("LastWritingDateTime")?.Value;
            Assert.Equal("2026-01-01T00:00:00Z", written);
        }
    }

    [Fact]
    public void AFileFromANewerFormatVersionIsReadWithAWarning()
    {
        var json = EflJson.Write(RoundTripTests.Sample()).Replace("\"formatVersion\": 1", "\"formatVersion\": 2");
        var model = EflJson.Read(json);

        Assert.Equal(3, model.Nodes.Count);
        Assert.Contains(model.Warnings, w => w.Contains("format version 2"));
    }

    [Fact]
    public void AFileWithoutAFormatVersionIsReadAsVersionOne()
    {
        var json = EflJson.Write(RoundTripTests.Sample()).Replace("\"formatVersion\": 1,", "");
        Assert.Empty(EflJson.Read(json).Warnings);
    }

    [Fact]
    public void AFlowEndsOnTheCircleOfAStoreNotOnItsBox()
    {
        var store = new EflNode { Id = "s", Kind = EflNodeKind.Store, Bounds = new EflBounds(0, 0, 100, 60) };

        // Diagonal: the box corner would be (100, 60); the ellipse is met earlier.
        var point = EflGeometry.DockingPoint(store, new EflPoint(150, 90));
        var onEllipse = Math.Pow((point.X - 50) / 50, 2) + Math.Pow((point.Y - 30) / 30, 2);

        Assert.Equal(1.0, onEllipse, 2);
    }

    [Fact]
    public void ANodeWithoutSizeDocksAtItsCentreInsteadOfNaN()
    {
        var store = new EflNode { Id = "s", Kind = EflNodeKind.Store, Bounds = new EflBounds(10, 20, 0, 0) };
        Assert.Equal(new EflPoint(10, 20), EflGeometry.DockingPoint(store, new EflPoint(300, 30)));
    }

    [Fact]
    public void AFlowEndsOnTheEdgeOfAStepsBox()
    {
        var step = new EflNode { Id = "s", Kind = EflNodeKind.Step, Bounds = new EflBounds(0, 0, 100, 60) };

        Assert.Equal(new EflPoint(100, 30), EflGeometry.DockingPoint(step, new EflPoint(300, 30)));
        Assert.Equal(new EflPoint(50, 60), EflGeometry.DockingPoint(step, new EflPoint(50, 200)));
    }
}
