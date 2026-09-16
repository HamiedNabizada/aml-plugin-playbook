// eflmap: the mapper on the command line. Useful for scripts, for CI, and for
// writing example files that the tests and the documentation refer to.
//
//   eflmap to-aml    <model.json> <out.aml> [--style link|element] [--timestamp 2026-01-01T00:00:00Z]
//   eflmap to-json   <in.aml> <out.json> [--hierarchy NAME]
//   eflmap update    <in.aml> <model.json> <out.aml> [--hierarchy NAME]
//   eflmap validate  <model.json|in.aml>
//   eflmap library   <out.aml>
//   eflmap arrange   <model.json> <out.json>     places nodes and flows that have no layout
//
// --timestamp fixes the time written into the document. Use it for files kept in
// the repository, so writing them again changes nothing unless the mapper did.
//
// Exit codes: 0 success, 1 findings with severity error, 2 usage or input error.

using Aml.Engine.CAEX;
using Efl.Conversion;

try
{
    return Run(args);
}
catch (Exception e) when (e is IOException or FormatException or System.Xml.XmlException or InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine("eflmap: " + e.Message);
    return 2;
}

static int Run(string[] args)
{
    var positional = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !args[i - 1].StartsWith("--"))).ToList();
    string? Option(string name)
    {
        var index = Array.IndexOf(args, "--" + name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    switch (positional.FirstOrDefault())
    {
        case "to-aml" when positional.Count == 3:
        {
            var model = EflJson.Read(File.ReadAllText(positional[1]));
            Report(model.Warnings);
            EflLayout.ArrangeMissing(model);
            var style = Option("style") == "element" ? EflConnectionStyle.Element : EflConnectionStyle.Link;
            var document = EflToCaex.Convert(model, null, style, Timestamp(Option("timestamp")));
            document.CAEXFile.FileName = Path.GetFileName(positional[2]);
            Save(document, positional[2]);
            // Say what was written: a silent success cannot be told from a run
            // that wrote nothing.
            Console.WriteLine($"wrote {positional[2]}: {model.Nodes.Count} node(s), {model.Flows.Count} flow(s), {style} encoding");
            return 0;
        }
        case "to-json" when positional.Count == 3:
        {
            var document = Load(positional[1]);
            var model = CaexToEfl.Read(Hierarchy(document, Option("hierarchy"))).First();
            Report(model.Warnings);
            EflLayout.ArrangeMissing(model);
            File.WriteAllText(positional[2], EflJson.Write(model));
            Console.WriteLine($"wrote {positional[2]}: {model.Nodes.Count} node(s), {model.Flows.Count} flow(s), {model.Warnings.Count} warning(s)");
            return 0;
        }
        case "update" when positional.Count == 4:
        {
            var document = Load(positional[1]);
            var model = EflJson.Read(File.ReadAllText(positional[2]));
            Report(model.Warnings);
            var summary = EflUpdater.UpdateInPlace(document, Hierarchy(document, Option("hierarchy")), model);
            Save(document, positional[3]);
            Console.WriteLine($"wrote {positional[3]}: {summary}");
            Report(summary.Notes);
            return 0;
        }
        case "validate" when positional.Count == 2:
        {
            var text = File.ReadAllText(positional[1]);
            var model = text.Contains("CAEXFile", StringComparison.Ordinal)
                ? CaexToEfl.ReadAll(EflDocuments.Load(text)).FirstOrDefault()
                  ?? throw new InvalidOperationException("The document holds no EFL diagram.")
                : EflJson.Read(text);
            var findings = EflValidator.Validate(model);
            foreach (var finding in findings) Console.WriteLine(finding);
            Console.WriteLine($"{findings.Count} finding(s), rules checked: {string.Join(", ", EflValidator.RuleIds)}");
            return findings.Any(f => f.Severity == EflSeverity.Error) ? 1 : 0;
        }
        case "arrange" when positional.Count == 3:
        {
            // For looking at what the layout does: the browser alone never runs it.
            var model = EflJson.Read(File.ReadAllText(positional[1]));
            Report(model.Warnings);
            var placed = EflLayout.ArrangeMissing(model);
            File.WriteAllText(positional[2], EflJson.Write(model));
            Console.WriteLine($"wrote {positional[2]}: {placed} node(s) placed");
            return 0;
        }
        case "library" when positional.Count == 2:
        {
            var document = EflLibraries.CreateArtefact();
            Save(document, positional[1]);
            Console.WriteLine($"wrote {positional[1]}: library version {EflNames.LibraryVersion}");

            // The artefact references the shared layout library by file name, so
            // it is published beside it.
            var layout = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(positional[1]))!, EflDiagramInterchange.FileName);
            File.WriteAllText(layout, EflDiagramInterchange.Xml);
            Console.WriteLine($"wrote {layout}: {EflDiagramInterchange.LibName} {EflDiagramInterchange.Version}");
            return 0;
        }
        default:
            Console.Error.WriteLine("usage: eflmap to-aml|to-json|update|validate|arrange|library ... (see Program.cs)");
            return 2;
    }
}

static DateTime? Timestamp(string? text) =>
    text == null
        ? null
        : DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

static InstanceHierarchyType Hierarchy(CAEXDocument document, string? name) =>
    (string.IsNullOrEmpty(name)
        ? document.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToEfl.ContainsDiagram)
        : document.CAEXFile.InstanceHierarchy[name])
    ?? throw new InvalidOperationException(name == null ? "The document holds no EFL diagram." : $"No hierarchy '{name}'.");

static CAEXDocument Load(string path) => EflDocuments.LoadFile(path);

static void Save(CAEXDocument document, string path) => EflDocuments.SaveFile(document, path);

static void Report(IEnumerable<string> lines)
{
    foreach (var line in lines) Console.Error.WriteLine("warning: " + line);
}
