using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Aml.Engine.CAEX;

namespace Efl.Conversion;

/// <summary>
/// CAEX ids derived from the language's own ids, deterministically.
///
/// The same model has to produce the same CAEX ids on every run. Otherwise an
/// update replaces every element instead of touching the ones that changed, and
/// every reference into the hierarchy that somebody else wrote dangles
/// afterwards. Deriving the id from the model id plus a role keeps that promise
/// without a mapping table in the file.
/// </summary>
public static class EflIds
{
    /// <summary>Seed, so ids from this mapper cannot collide with another tool's.</summary>
    private const string Namespace = "efl-aml-mapper";

    /// <summary>
    /// Separates the parts of a seed: a character no id can contain, so
    /// For("a", "b") and For("ab") cannot produce the same id. Written as an
    /// escape rather than as a literal NUL, which would make the file binary
    /// to git and invisible to grep.
    /// </summary>
    private const string Separator = "\0";

    public static string For(string kind, params string[] parts)
    {
        var seed = Namespace + Separator + kind + Separator + string.Join(Separator, parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);

        // Stamp version 5 and the RFC 4122 variant so the result is a well
        // formed UUID. Note the byte indices: Guid(byte[]) reads the first
        // three fields little endian, so the version nibble that shows up in
        // the rendered form comes from byte 7, not byte 6.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes).ToString("B");
    }

    public static string Element(string modelId) => For("element", modelId);

    public static string Diagram(string modelId) => For("diagram", modelId);

    /// <summary>
    /// Id for an interface, derived from the CAEX id of the element that owns
    /// it rather than from the model id. Two copies of the same model in one
    /// document would otherwise share interface ids, and removing a flow in one
    /// copy would take the other copy's links with it.
    /// </summary>
    public static string Interface(string ownerCaexId, string interfaceName) =>
        For("interface", ownerCaexId, interfaceName);

    public static string Link(string ownerCaexId, string side) => For("link", ownerCaexId, side);

    /// <summary>An id for an element, made distinct if that id is already taken in the document.</summary>
    public static string DistinctElement(string modelId, IdSpace taken)
    {
        var candidate = Element(modelId);
        for (var ordinal = 2; !taken.Claim(candidate); ordinal++)
            candidate = For("element", modelId, ordinal.ToString());
        return candidate;
    }
}

/// <summary>
/// The ids a document already uses. Read once rather than asked per element:
/// a per element lookup through the engine turns a large model into a wait, and
/// one pass over the XML also sees ids on classes and in other hierarchies.
/// </summary>
public sealed class IdSpace
{
    private readonly HashSet<string> _taken;

    public IdSpace(CAEXDocument document)
    {
        _taken = new HashSet<string>(StringComparer.Ordinal);

        var root = document.CAEXFile?.Node;
        if (root == null) return;

        foreach (var attribute in root.DescendantsAndSelf().Attributes("ID"))
            if (!string.IsNullOrEmpty(attribute.Value)) _taken.Add(attribute.Value);
    }

    /// <summary>Takes the id when it is free, false when it was already in use.</summary>
    public bool Claim(string id) => _taken.Add(id);
}
