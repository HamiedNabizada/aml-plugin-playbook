using System.Text.Json.Serialization;

namespace Aml.Editor.Plugin.Efl.Bridge;

/// <summary>A message from the page. Mirrors what web/src/bridge.js posts.</summary>
public sealed class PageMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("level")] public string? Level { get; set; }

    /// <summary>The diagram in the exchange format, for "changed" and "imported".</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>What the page's reader dropped, for "imported".</summary>
    [JsonPropertyName("warnings")] public List<string>? Warnings { get; set; }

    [JsonPropertyName("svg")] public string? Svg { get; set; }
}

/// <summary>The type tags on the wire, in one place on each side.</summary>
public static class MessageType
{
    // page -> host
    public const string Ready = "ready";
    public const string Imported = "imported";
    public const string Changed = "changed";
    public const string Svg = "svg";
    public const string Log = "log";
    public const string Error = "error";

    // host -> page
    public const string ImportModel = "importModel";
    public const string RequestExport = "requestExport";
    public const string RequestSvg = "requestSvg";
    public const string SelectElement = "selectElement";
    public const string SetTheme = "setTheme";
}
