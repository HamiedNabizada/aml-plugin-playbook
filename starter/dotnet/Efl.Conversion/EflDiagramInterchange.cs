using System.Reflection;
using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// The shared layout types: DD_Bounds, DD_Point and DD_Waypoint from
/// OMG_DD_AttributeTypeLib, the diagram interchange library every language
/// uses the same way (method pattern P7).
///
/// The library file ships inside this assembly (starter/libraries), because a
/// document has to carry it: the AutomationML Editor does not follow file
/// references, so a document that only references the library shows its
/// positions and bend points as unresolved attributes. Do not redefine these
/// types under your own library name; two languages in one document would then
/// describe layout in two incompatible ways.
///
/// A document names the library in one of two forms, and every write follows
/// the form the document already uses:
/// <list type="bullet">
/// <item>carried inline: <c>OMG_DD_AttributeTypeLib/DD_Point</c>;</item>
/// <item>referenced through an ExternalReference: <c>OMG_DD@OMG_DD_AttributeTypeLib/DD_Point</c>,
/// with the alias that document chose.</item>
/// </list>
/// Writing the referenced form into a document that carries the library inline
/// leaves an alias that resolves to nothing on every attribute an update touches.
/// </summary>
public static class EflDiagramInterchange
{
    public const string LibName = "OMG_DD_AttributeTypeLib";
    public const string Version = "0.1.0";
    public const string FileName = "OMG_DD_AttributeTypeLib_v0.1.aml";
    public const string Alias = "OMG_DD";

    public const string Bounds = "DD_Bounds";
    public const string Point = "DD_Point";
    public const string Waypoint = "DD_Waypoint";

    private static readonly XNamespace Caex = "http://www.dke.de/CAEX";

    /// <summary>The library file as published, read from the assembly.</summary>
    public static string Xml { get; } = ReadEmbedded();

    /// <summary>
    /// Puts the library into a document that has it in neither form. A document
    /// that already carries or references it is left as it is.
    /// </summary>
    public static void EnsureIn(CAEXFileType caex)
    {
        ArgumentNullException.ThrowIfNull(caex);
        if (caex.AttributeTypeLib[LibName] != null || ReferenceTo(caex) != null) return;

        var library = XDocument.Parse(Xml).Root?
            .Elements(Caex + "AttributeTypeLib")
            .FirstOrDefault(e => (string?)e.Attribute("Name") == LibName)
            ?? throw new InvalidOperationException($"The embedded {FileName} holds no AttributeTypeLib '{LibName}'.");

        caex.Node.Add(new XElement(library));
    }

    /// <summary>
    /// References the published file instead of carrying it. For the domain
    /// library artefact, which is published next to the OMG_DD file.
    /// </summary>
    public static void ReferenceFrom(CAEXFileType caex)
    {
        ArgumentNullException.ThrowIfNull(caex);
        if (caex.AttributeTypeLib[LibName] != null || ReferenceTo(caex) != null) return;

        var reference = caex.ExternalReference.Append();
        reference.Alias = Alias;
        reference.Path = FileName;
    }

    /// <summary>The path of a layout type, in the form the document of <paramref name="owner"/> uses.</summary>
    public static string PathOf(object owner, string type) =>
        PathIn((owner as CAEXBasicObject)?.CAEXDocument?.CAEXFile, type);

    /// <summary>The path of a layout type, in the form <paramref name="caex"/> uses.</summary>
    public static string PathIn(CAEXFileType? caex, string type)
    {
        // Inline, or no document at hand: the unqualified path.
        if (caex == null || caex.AttributeTypeLib[LibName] != null) return LibName + "/" + type;

        var reference = ReferenceTo(caex);
        return reference != null
            ? reference.Alias + "@" + LibName + "/" + type
            : LibName + "/" + type;
    }

    /// <summary>
    /// The reference to the OMG_DD file, under whatever alias the document chose.
    /// Recognised by the file name, so a document that called it "DD" keeps "DD".
    /// </summary>
    private static ExternalReferenceType? ReferenceTo(CAEXFileType caex) =>
        caex.ExternalReference.FirstOrDefault(r =>
            string.Equals(Path.GetFileName(r.Path ?? ""), FileName, StringComparison.OrdinalIgnoreCase)
            || r.Alias == Alias);

    private static string ReadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith(FileName, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException(
                       $"'{FileName}' is not embedded in {assembly.GetName().Name}. Check the EmbeddedResource item in the project file.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
