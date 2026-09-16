// The two things the plugin contract does not offer, reached by reflection on
// the editor's main view model:
//
//   1. The document that is open right now. DocumentLoaded fires once, when a
//      file opens. A view created later (undock and redock, a plugin activated
//      after the file was opened, a restored session) never hears of it.
//   2. The editor's own save command. The contract cannot tell the editor that
//      the document changed, so an Update is only in the file after a save.
//
// Both are brittle by nature: a new editor version may rename what is looked up
// here. Every failure is logged and the plugin falls back to what it would do
// without this class (wait for DocumentLoaded, ask the user to press Ctrl+S).

using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Aml.Editor.Plugin.Efl.Diagnostics;
using Aml.Engine.CAEX;

namespace Aml.Editor.Plugin.Efl.Bridge;

public static class EditorAccess
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string[] DocumentHolders =
        { "CurrentDocument", "ActiveDocument", "Document", "CAEXDocument", "AMLDocument", "OpenedDocument", "SelectedDocument" };

    private static readonly string[] SaveCommands =
        { "SaveAMLCommand", "SaveCommand", "SaveActiveDocumentCommand", "SaveCurrentAMLFileCommand" };

    public static CAEXDocument? TryFindOpenDocument()
    {
        try
        {
            var viewModel = Application.Current?.MainWindow?.DataContext;
            return viewModel == null ? null : FindDocument(viewModel, depth: 0);
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Looking up the open document failed: " + ex.Message);
            return null;
        }
    }

    private static CAEXDocument? FindDocument(object owner, int depth)
    {
        if (depth > 2) return null;
        var type = owner.GetType();

        foreach (var property in type.GetProperties(Members).Where(p => typeof(CAEXDocument).IsAssignableFrom(p.PropertyType)))
        {
            try
            {
                if (property.GetValue(owner) is CAEXDocument document) return document;
            }
            catch { /* a getter with preconditions */ }
        }

        foreach (var name in DocumentHolders)
        {
            object? value;
            try { value = type.GetProperty(name, Members)?.GetValue(owner); }
            catch { continue; }
            if (value == null) continue;
            if (value is CAEXDocument document) return document;
            if (FindDocument(value, depth + 1) is { } nested) return nested;
        }

        return null;
    }

    /// <summary>True when a save command was found and run. Whether the editor wrote the file is its business.</summary>
    public static bool TrySave()
    {
        try
        {
            var viewModel = Application.Current?.MainWindow?.DataContext;
            if (viewModel == null) return false;

            foreach (var name in SaveCommands)
            {
                if (viewModel.GetType().GetProperty(name, Members)?.GetValue(viewModel) is not ICommand command) continue;
                // A later candidate may be executable when this one is not.
                if (!command.CanExecute(null)) continue;

                command.Execute(null);
                PluginLog.Info($"Ran the editor's {name}.");
                return true;
            }

            PluginLog.Warn($"No save command found on {viewModel.GetType().FullName}; the editor may have renamed it.");
            return false;
        }
        catch (Exception ex)
        {
            PluginLog.Error("Running the editor's save command failed", ex);
            return false;
        }
    }
}
