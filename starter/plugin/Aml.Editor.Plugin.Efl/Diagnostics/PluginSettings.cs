// What the user decided, kept between sessions in
//   %APPDATA%\AutomationMLEditor\EflPlugin\settings.json
// A file that cannot be read gives the defaults: a preference is not worth an
// error dialog.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Efl.Conversion;

namespace Aml.Editor.Plugin.Efl.Diagnostics;

public sealed class PluginSettings
{
    /// <summary>
    /// Run the editor's save command after an Update. On by default: the
    /// contract cannot tell the editor that the document changed, so an unsaved
    /// update is lost when the editor closes.
    /// </summary>
    [JsonPropertyName("save_after_update")] public bool SaveAfterUpdate { get; set; } = true;

    /// <summary>
    /// Ask before an Update that adds or removes more elements than the
    /// threshold. It guards against writing a diagram that is not the one the
    /// document holds, which would delete the difference.
    /// </summary>
    [JsonPropertyName("ask_before_large_updates")] public bool AskBeforeLargeUpdates { get; set; } = true;

    [JsonPropertyName("large_update_threshold")] public int LargeUpdateThreshold { get; set; } = 5;

    /// <summary>How New Diagram and Import write flows. Existing hierarchies keep theirs.</summary>
    [JsonPropertyName("connection_style")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EflConnectionStyle ConnectionStyle { get; set; } = EflConnectionStyle.Link;

    [JsonPropertyName("debug_logging")] public bool DebugLogging { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutomationMLEditor", "EflPlugin", "settings.json");

    public static PluginSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(FilePath), Json) ?? new PluginSettings()
                : new PluginSettings();
        }
        catch
        {
            return new PluginSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Could not save the settings: " + ex.Message);
        }
    }
}
