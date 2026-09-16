using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// Loading and saving AML text, with the two surprises of Aml.Engine taken care of.
/// </summary>
public static class EflDocuments
{
    /// <summary>
    /// Loads AML from text, refusing anything that is not a CAEX document.
    ///
    /// Aml.Engine does not always reject such text: for some inputs it returns a
    /// document whose CAEXFile is null, and the NullReferenceException then comes
    /// from wherever the document is used next. A web API reported that as a
    /// server fault (500) for what was the caller's broken file. Checking once,
    /// here, turns it into a FormatException with a message.
    /// </summary>
    public static CAEXDocument Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        CAEXDocument? document;
        try
        {
            document = CAEXDocument.LoadFromString(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("This is not an AML document: " + ex.Message, ex);
        }

        return document?.CAEXFile == null
            ? throw new FormatException("This is not an AML document.")
            : document;
    }

    /// <summary>
    /// Loads an AML file. Read as text on purpose: CAEXDocument.LoadFromFile
    /// writes a copy of the CAEX schema (CAEX_ClassModel_V.3.0.xsd) into the
    /// folder of the file, which ends up committed in example folders.
    /// </summary>
    public static CAEXDocument LoadFile(string path) => Load(File.ReadAllText(path));

    /// <summary>The document as indented XML text.</summary>
    public static string ToXml(CAEXDocument document)
    {
        using var stream = document.SaveToStream(prettyPrint: true);
        stream.Position = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Saves through a stream. SaveToFile, like LoadFromFile, drops the CAEX
    /// schema next to the file.
    /// </summary>
    public static void SaveFile(CAEXDocument document, string path) => File.WriteAllText(path, ToXml(document));
}
