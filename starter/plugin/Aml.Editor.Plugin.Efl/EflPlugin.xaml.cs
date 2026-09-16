// The EFL plugin for the AutomationML Editor.
//
// Shows one diagram of the open document in a diagram-js modeler hosted in
// WebView2, and writes edits back with Update. The AML document stays the
// master: Update changes what the language owns and leaves everything else in
// the document alone.
//
// State the view keeps, and why:
//   _document, _hierarchyId/_hierarchyName, _diagramIndex  what the canvas shows
//   _pendingModel   the latest edit not yet written back ("unsaved changes")
//   _echoBaseline   the page's own export right after an import; a change with
//                   exactly this content is import fallout, not an edit
//   _restoring      the page is being handed its unsaved edits back after a
//                   reload, so the import acknowledgement must not clear them

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aml.Editor.Plugin.Contracts;
using Aml.Editor.Plugin.Efl.Bridge;
using Aml.Editor.Plugin.Efl.Diagnostics;
using Aml.Editor.Plugin.WPFBase;
using Aml.Engine.CAEX;
using Efl.Conversion;
using Microsoft.Win32;

namespace Aml.Editor.Plugin.Efl;

public partial class EflPlugin : PluginViewBase, INotifyAMLDocumentLoad, ISupportsThemes
{
    private const int MaxLogLines = 500;

    private readonly PluginSettings _settings = PluginSettings.Load();
    private ModelerView? _modeler;

    private CAEXDocument? _document;
    private string? _hierarchyId;
    private string? _hierarchyName;
    private int _diagramIndex;

    private string? _pendingModel;
    private string? _echoBaseline;
    private bool _restoring;
    private bool _baselineIsDocument = true;
    private string? _theme;

    private bool _fillingPicker;
    private bool _fillingSettings;

    private sealed record FindingRow(string Severity, string Rule, string Element, string Message, string? ElementId);

    private sealed record DiagramChoice(string? HierarchyId, string? HierarchyName, int Index, string Label)
    {
        public override string ToString() => Label;
    }

    public EflPlugin()
    {
        InitializeComponent();

        // No dots, slashes or spaces: the editor turns DisplayName into a WPF
        // x:Name and an XML element name in its configuration, and both refuse
        // them. The failure is an exception at startup with no hint to the cause.
        DisplayName = "EFLStarter";
        IsReactive = true;

        PluginLog.Init();
        PluginLog.DebugEnabled = _settings.DebugLogging;
        PluginLog.OnLine += AppendLogLine;
        PluginLog.Info($"Plugin {typeof(EflPlugin).Assembly.GetName().Version?.ToString(3)} starting. Log: {PluginLog.FilePath}");
        if (!File.Exists(Path.Combine(ModelerView.AssetsPath, "efl.esm.js")))
            PluginLog.Error("The modeler bundle is not next to the plugin: " + ModelerView.AssetsPath);

        NewDiagramItem.Command = new RelayCommand<object>(_ => NewDiagram(), _ => _document != null);
        ImportJsonItem.Command = new RelayCommand<object>(_ => ImportJson(), _ => _document != null);

        // Created in the constructor, not in Loaded: the editor may call
        // DocumentLoaded before the view is loaded, and without a bridge object
        // that diagram would be lost instead of buffered.
        CreateModeler();

        Loaded += async (_, _) =>
        {
            FillSettings();
            await _modeler!.InitAsync();

            // A view created after the file was opened never gets DocumentLoaded.
            if (_document == null && EditorAccess.TryFindOpenDocument() is { } open) Attach(open);
        };

        // Deliberately no Unloaded handler. The editor fires Loaded and Unloaded
        // on every re-parenting of the visual tree (tab switches, docking,
        // resizing panels). Tearing the WebView down there reloaded the page each
        // time and threw away unsaved edits. DocumentUnLoaded and
        // ApplicationClose end its life.
    }

    // ── plugin identity ─────────────────────────────────────────────────────

    public override string PackageName => "Aml.Editor.Plugin.Efl";

    public override DockPositionEnum InitialDockPosition => DockPositionEnum.DockContent;

    public override bool CanClose => true;

    // ── bridge ──────────────────────────────────────────────────────────────

    private void CreateModeler()
    {
        _modeler = new ModelerView(Modeler);
        _modeler.Info += PluginLog.Info;
        _modeler.Error += message => PluginLog.Error(message);
        _modeler.PageLog += (level, message) =>
        {
            if (level == "error") PluginLog.Error("[page] " + message);
            else if (level == "warn") PluginLog.Warn("[page] " + message);
            else PluginLog.Debug("[page] " + message);
        };
        _modeler.Ready += OnModelerReady;
        _modeler.Imported += OnImported;
        _modeler.Changed += OnChanged;
    }

    /// <summary>
    /// Every page boot: the first, a reload from the context menu, a recovery
    /// after a renderer crash. Unsaved edits win over the document.
    /// </summary>
    private void OnModelerReady()
    {
        if (_pendingModel != null)
        {
            _restoring = true;
            _modeler!.ImportModel(_pendingModel);
            PluginLog.Info("Page booted; restored the unsaved diagram.");
        }
        else if (CurrentHierarchy() is { } hierarchy)
        {
            // Read from the document now. Whatever was buffered before the boot
            // is older; the bridge drops it because this handler sent something.
            Show(hierarchy);
        }

        if (_theme != null) _modeler!.SetTheme(_theme);
    }

    private void OnImported(string? baseline, IReadOnlyList<string> warnings)
    {
        _echoBaseline = baseline;
        // A restored canvas shows unsaved edits, so its baseline is not what the
        // document holds.
        _baselineIsDocument = !_restoring;
        if (_restoring) _restoring = false;
        else _pendingModel = null;
        foreach (var warning in warnings) PluginLog.Warn("[page] " + warning);
        UpdatePendingLabel();
    }

    private void OnChanged(string json)
    {
        // Compared by content, not by a time window: a window long enough to
        // swallow import fallout also swallows a quick real edit.
        if (string.Equals(json, _echoBaseline, StringComparison.Ordinal))
        {
            // Back where the import left the canvas: import fallout, or edits
            // undone. If that state is the document's, nothing is pending any
            // more; keeping the older edit would write an undone change on the
            // next Update.
            _pendingModel = _baselineIsDocument ? null : json;
            UpdatePendingLabel();
            return;
        }

        _pendingModel = json;
        UpdatePendingLabel();

        try
        {
            ShowFindings(EflJson.Read(json), open: false);
        }
        catch (Exception ex)
        {
            PluginLog.Debug("Could not check the diagram: " + ex.Message);
        }
    }

    // ── document lifecycle ──────────────────────────────────────────────────

    /// <summary>
    /// Required by the contract, never raised. The editor answers this event by
    /// calling DocumentLoaded again, which raised it again: a loop that ran about
    /// forty times a second in AMLFPB.js before it was found.
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<CAEXDocument>? IsDocumentLoaded;
#pragma warning restore CS0067

    public void DocumentLoaded(CAEXDocument document)
    {
        if (document == null)
        {
            DocumentUnLoaded();
            return;
        }
        if (ReferenceEquals(document, _document)) return;

        // The editor sometimes hands out a new wrapper for the file that is
        // already open. Reattaching would reload the canvas and drop unsaved
        // edits, so identity is decided by the file, not by the object.
        if (SameFile(document, _document))
        {
            _document = document;
            return;
        }

        Attach(document);
    }

    /// <summary>
    /// File name plus OriginID, as AMLPetriNet does. The contract exposes no
    /// path, so two files with the same name and origin in different folders
    /// still count as one; switching between such files keeps the canvas.
    /// </summary>
    private static bool SameFile(CAEXDocument? a, CAEXDocument? b)
    {
        if (a == null || b == null) return false;

        static (string Origin, string File) Identity(CAEXDocument d) => (
            d.CAEXFile?.SourceDocumentInformation?.FirstOrDefault()?.OriginID ?? "",
            d.CAEXFile?.FileName ?? "");

        var (originA, fileA) = Identity(a);
        // Two new, unsaved documents share an empty identity; they are different.
        if (fileA.Length == 0) return false;
        return (originA, fileA) == Identity(b);
    }

    public void DocumentUnLoaded()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(DocumentUnLoaded);
            return;
        }

        _document = null;
        _hierarchyId = _hierarchyName = null;
        _pendingModel = _echoBaseline = null;
        _diagramIndex = 0;
        ClearFindings();
        UpdatePendingLabel();
        FillPicker();
        SetStatus("");
        Placeholder.Visibility = Visibility.Visible;
        CommandManager.InvalidateRequerySuggested();
    }

    public void ApplicationClose()
    {
        _modeler?.Dispose();
        _modeler = null;
        PluginLog.Shutdown();
    }

    /// <summary>
    /// The editor's callbacks are not documented as UI thread only, and WPF and
    /// WebView2 are thread affine, so the whole body is marshalled.
    /// </summary>
    private void Attach(CAEXDocument document)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Attach(document));
            return;
        }

        _document = document;
        _pendingModel = null;
        _diagramIndex = 0;
        CommandManager.InvalidateRequerySuggested();
        ClearFindings();
        UpdatePendingLabel();

        var hierarchy = document.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToEfl.ContainsDiagram);
        if (hierarchy == null)
        {
            Bind(null);
            FillPicker();
            Placeholder.Visibility = Visibility.Visible;
            SetStatus("This document holds no EFL diagram.");
            return;
        }

        Bind(hierarchy);
        Show(hierarchy);
    }

    /// <summary>
    /// Bound by id and name. A user can rename a hierarchy in the tree at any
    /// time; binding by name alone loses it, and the next Update adds a second one.
    /// </summary>
    private void Bind(InstanceHierarchyType? hierarchy)
    {
        _hierarchyId = hierarchy?.ID;
        _hierarchyName = hierarchy?.Name;
    }

    private InstanceHierarchyType? CurrentHierarchy()
    {
        if (_document == null) return null;
        var all = _document.CAEXFile.InstanceHierarchy;
        // Compared by ID: Aml.Engine wrappers are not reference stable.
        var byId = string.IsNullOrEmpty(_hierarchyId) ? null : all.FirstOrDefault(h => h.ID == _hierarchyId);
        if (byId != null)
        {
            _hierarchyName = byId.Name;
            return byId;
        }
        return _hierarchyName == null ? null : all.FirstOrDefault(h => h.Name == _hierarchyName);
    }

    // ── AML to canvas ───────────────────────────────────────────────────────

    private void Show(InstanceHierarchyType hierarchy)
    {
        try
        {
            var diagrams = CaexToEfl.Read(hierarchy);
            if (diagrams.Count == 0)
            {
                SetStatus($"'{hierarchy.Name}' holds no readable diagram.");
                return;
            }

            _diagramIndex = Math.Clamp(_diagramIndex, 0, diagrams.Count - 1);
            var model = diagrams[_diagramIndex];
            FillPicker();

            foreach (var warning in model.Warnings) PluginLog.Warn(warning);
            // A document from another tool often has no layout. Without this every
            // node sits at the origin in one pile.
            var placed = EflLayout.ArrangeMissing(model);
            ShowFindings(model, open: true);

            Placeholder.Visibility = Visibility.Collapsed;
            SetStatus($"{model.Name ?? model.Id}: {model.Nodes.Count} node(s), {model.Flows.Count} flow(s)"
                      + (placed > 0 ? $", {placed} arranged." : "."));

            _modeler?.ImportModel(EflJson.Write(model));
        }
        catch (Exception ex)
        {
            PluginLog.Error("Could not load the diagram from AML", ex);
            SetStatus("Could not load the diagram: " + ex.Message);
        }
    }

    private void FillPicker()
    {
        var choices = new List<DiagramChoice>();
        if (_document != null)
        {
            foreach (var hierarchy in _document.CAEXFile.InstanceHierarchy.Where(CaexToEfl.ContainsDiagram))
            {
                var diagrams = CaexToEfl.Read(hierarchy);
                for (var i = 0; i < diagrams.Count; i++)
                {
                    var name = diagrams[i].Name ?? diagrams[i].Id;
                    choices.Add(new DiagramChoice(hierarchy.ID, hierarchy.Name, i,
                        diagrams.Count > 1 ? $"{hierarchy.Name} / {name}" : hierarchy.Name ?? name));
                }
            }
        }

        _fillingPicker = true;
        try
        {
            DiagramPicker.Items.Clear();
            foreach (var choice in choices) DiagramPicker.Items.Add(choice);
            DiagramPicker.Visibility = choices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            DiagramPicker.SelectedIndex = choices.FindIndex(c => c.HierarchyId == _hierarchyId && c.Index == _diagramIndex);
        }
        finally
        {
            _fillingPicker = false;
        }
    }

    private void DiagramPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingPicker || _document == null || DiagramPicker.SelectedItem is not DiagramChoice choice) return;
        if (!DiscardPendingConfirmed("Switching diagrams"))
        {
            FillPicker();
            return;
        }

        var target = _document.CAEXFile.InstanceHierarchy.FirstOrDefault(h => h.ID == choice.HierarchyId);
        if (target == null) return;

        Bind(target);
        _diagramIndex = choice.Index;
        _pendingModel = null;
        UpdatePendingLabel();
        Show(target);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentHierarchy() is not { } hierarchy)
        {
            SetStatus("No diagram to reload.");
            return;
        }
        if (!DiscardPendingConfirmed("Reloading from AML")) return;

        _pendingModel = null;
        UpdatePendingLabel();
        Show(hierarchy);
    }

    private bool DiscardPendingConfirmed(string action) =>
        _pendingModel == null
        || MessageBox.Show($"The diagram has unsaved changes. {action} discards them. Continue?",
            "EFL", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    // ── canvas to AML ───────────────────────────────────────────────────────

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null)
        {
            SetStatus("No AML document is open.");
            return;
        }
        if (_pendingModel == null)
        {
            SetStatus("Nothing to write back: the diagram has no unsaved changes.");
            return;
        }

        try
        {
            var model = EflJson.Read(_pendingModel);
            var hierarchy = CurrentHierarchy();
            if (hierarchy != null && !LargeUpdateConfirmed(hierarchy, model))
            {
                SetStatus("Update cancelled; the diagram keeps its changes.");
                return;
            }

            EflUpdateSummary summary;
            if (hierarchy == null)
            {
                hierarchy = EflToCaex.AppendInto(_document, model, UniqueName("EFL_Diagrams"), _settings.ConnectionStyle);
                summary = new EflUpdateSummary(model.Nodes.Count + model.Flows.Count, 0, 0);
            }
            else
            {
                // In place, never regenerate: attributes a user added by hand and
                // links into other hierarchies have to survive.
                summary = EflUpdater.UpdateInPlace(_document, hierarchy, model, _diagramIndex);
            }

            Bind(hierarchy);
            _pendingModel = null;
            _echoBaseline = null;
            UpdatePendingLabel();
            FillPicker();
            ShowFindings(model, open: true);

            PluginLog.Info($"'{hierarchy.Name}': {summary}.");
            foreach (var note in summary.Notes) PluginLog.Warn(note);

            var saved = _settings.SaveAfterUpdate && EditorAccess.TrySave();
            SetStatus($"Updated '{hierarchy.Name}': {summary}."
                      + (summary.Notes.Count > 0 ? $" {summary.Notes.Count} note(s) in Diagnostics." : "")
                      + (saved ? " Saved." : " Press Ctrl+S to save."));
        }
        catch (Exception ex)
        {
            PluginLog.Error("Update failed", ex);
            SetStatus("Update failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Asks before an update that adds or removes many elements. The case it is
    /// for: a canvas showing a diagram that is not the one in the document.
    /// Counted by id, so moving and renaming never ask.
    /// </summary>
    private bool LargeUpdateConfirmed(InstanceHierarchyType hierarchy, EflModel incoming)
    {
        if (!_settings.AskBeforeLargeUpdates) return true;
        var stored = CaexToEfl.Read(hierarchy).ElementAtOrDefault(_diagramIndex);
        if (stored == null) return true;

        static HashSet<string> Ids(EflModel m) =>
            m.Nodes.Select(n => n.Id).Concat(m.Flows.Select(f => f.Id)).ToHashSet(StringComparer.Ordinal);

        var before = Ids(stored);
        var after = Ids(incoming);
        var added = after.Except(before).Count();
        var removed = before.Except(after).Count();
        if (added + removed <= _settings.LargeUpdateThreshold) return true;

        return MessageBox.Show(
            $"This update adds {added} element(s) and removes {removed}. Removed elements are gone from the document, "
            + "together with anything attached to them.\n\nApply it?",
            "Update", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    // ── diagram menu ────────────────────────────────────────────────────────

    private void DiagramMenuButton_Click(object sender, RoutedEventArgs e)
    {
        DiagramMenu.PlacementTarget = DiagramMenuButton;
        DiagramMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        DiagramMenu.IsOpen = true;
    }

    private void NewDiagram()
    {
        if (_document == null || !DiscardPendingConfirmed("Creating a diagram")) return;
        AddToDocument(new EflModel { Id = "diagram", Name = "Diagram" }, "EFL_Diagrams");
    }

    private void ImportJson()
    {
        if (_document == null || !DiscardPendingConfirmed("Importing")) return;

        var dialog = new OpenFileDialog { Filter = "EFL JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var model = EflJson.Read(File.ReadAllText(dialog.FileName));
            foreach (var warning in model.Warnings) PluginLog.Warn(warning);
            // Arranged before writing, so the document never holds a diagram
            // without layout.
            EflLayout.ArrangeMissing(model);
            AddToDocument(model, Path.GetFileNameWithoutExtension(dialog.FileName));
        }
        catch (Exception ex)
        {
            PluginLog.Error("Import failed", ex);
            MessageBox.Show("Could not import that file:\n\n" + ex.Message, "Import JSON",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddToDocument(EflModel model, string preferredName)
    {
        var hierarchy = EflToCaex.AppendInto(_document!, model, UniqueName(preferredName), _settings.ConnectionStyle);
        Bind(hierarchy);
        _diagramIndex = 0;
        _pendingModel = null;
        UpdatePendingLabel();
        Show(hierarchy);
        SetStatus($"Added '{hierarchy.Name}' ({_settings.ConnectionStyle} encoding). Press Ctrl+S to save.");
    }

    private string UniqueName(string preferred)
    {
        var taken = _document!.CAEXFile.InstanceHierarchy.Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(preferred)) return preferred;
        for (var i = 2; ; i++)
            if (!taken.Contains($"{preferred}_{i}")) return $"{preferred}_{i}";
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var json = _pendingModel;
        if (json == null && CurrentHierarchy() is { } hierarchy && CaexToEfl.Read(hierarchy).ElementAtOrDefault(_diagramIndex) is { } stored)
        {
            EflLayout.ArrangeMissing(stored);
            json = EflJson.Write(stored);
        }
        if (json == null)
        {
            SetStatus("Nothing to export.");
            return;
        }

        var dialog = new SaveFileDialog { Filter = "EFL JSON (*.json)|*.json", FileName = (_hierarchyName ?? "diagram") + ".json" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, json);
        SetStatus("Exported " + Path.GetFileName(dialog.FileName));
    }

    private async void ExportSvg_Click(object sender, RoutedEventArgs e)
    {
        // async void is right for an event handler, and only there; everything
        // inside is guarded because an exception here would take the editor down.
        try
        {
            var svg = _modeler == null ? null : await _modeler.RequestSvgAsync(TimeSpan.FromSeconds(10));
            if (string.IsNullOrEmpty(svg))
            {
                SetStatus("The canvas did not answer the request for a picture.");
                return;
            }

            var dialog = new SaveFileDialog { Filter = "SVG (*.svg)|*.svg", FileName = (_hierarchyName ?? "diagram") + ".svg" };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, svg);
            SetStatus("Exported " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            PluginLog.Error("SVG export failed", ex);
            SetStatus("SVG export failed: " + ex.Message);
        }
    }

    // ── findings ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the language's rules and shows the result. Automatic, on load, on
    /// every edit and after Update: a check that has to be remembered is a check
    /// that does not happen. The panel opens by itself only on load and after
    /// Update, never while someone is drawing.
    /// </summary>
    private void ShowFindings(EflModel model, bool open)
    {
        string NameOf(string? id) =>
            id == null ? "" : model.FindNode(id)?.Name ?? model.Flows.FirstOrDefault(f => f.Id == id)?.Name ?? id;

        var rows = EflValidator.Validate(model)
            .Select(f => new FindingRow(f.Severity.ToString(), f.Rule, NameOf(f.ElementId), f.Message, f.ElementId))
            .ToList();
        var errors = rows.Count(r => r.Severity == nameof(EflSeverity.Error));

        FindingsGrid.ItemsSource = rows;
        FindingsButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsButton.Content = errors > 0 ? $"{errors} error(s), {rows.Count - errors} warning(s)" : $"{rows.Count} warning(s)";

        if (rows.Count == 0) SetFindingsVisible(false);
        else if (open && errors > 0) SetFindingsVisible(true);
    }

    private void ClearFindings()
    {
        FindingsGrid.ItemsSource = null;
        FindingsButton.Visibility = Visibility.Collapsed;
        SetFindingsVisible(false);
    }

    private void SetFindingsVisible(bool visible)
    {
        FindingsPanel.Visibility = FindingsSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FindingsRow.Height = new GridLength(visible ? 160 : 0);
    }

    private void FindingsButton_Click(object sender, RoutedEventArgs e) =>
        SetFindingsVisible(FindingsPanel.Visibility != Visibility.Visible);

    private void FindingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindingsGrid.SelectedItem is FindingRow { ElementId: { Length: > 0 } id }) _modeler?.SelectElement(id);
    }

    // ── status, settings, log ───────────────────────────────────────────────

    /// <summary>
    /// One line next to the buttons saying what the last action did. The log sits
    /// on another tab, and an Update that only reported there looked like nothing
    /// had happened.
    /// </summary>
    private void SetStatus(string text) => Dispatcher.Invoke(() => StatusLabel.Text = text);

    private void UpdatePendingLabel() =>
        Dispatcher.Invoke(() => PendingLabel.Text = _pendingModel == null ? "" : "unsaved diagram changes");

    public void OnThemeChanged(ApplicationTheme theme)
    {
        _theme = theme.ToString().Contains("Dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        Dispatcher.Invoke(() => _modeler?.SetTheme(_theme));
    }

    private void FillSettings()
    {
        _fillingSettings = true;
        SaveAfterUpdateToggle.IsChecked = _settings.SaveAfterUpdate;
        AskBeforeLargeToggle.IsChecked = _settings.AskBeforeLargeUpdates;
        ThresholdInput.Text = _settings.LargeUpdateThreshold.ToString(CultureInfo.InvariantCulture);
        ElementStyleToggle.IsChecked = _settings.ConnectionStyle == EflConnectionStyle.Element;
        DebugToggle.IsChecked = _settings.DebugLogging;
        _fillingSettings = false;
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        // Setting IsChecked in FillSettings raises these events too.
        if (_fillingSettings) return;

        _settings.SaveAfterUpdate = SaveAfterUpdateToggle.IsChecked == true;
        _settings.AskBeforeLargeUpdates = AskBeforeLargeToggle.IsChecked == true;
        if (int.TryParse(ThresholdInput.Text, out var threshold) && threshold > 0) _settings.LargeUpdateThreshold = threshold;
        _settings.ConnectionStyle = ElementStyleToggle.IsChecked == true ? EflConnectionStyle.Element : EflConnectionStyle.Link;
        _settings.DebugLogging = PluginLog.DebugEnabled = DebugToggle.IsChecked == true;
        _settings.Save();

        // Write the stored value back, so a typo does not look accepted.
        ThresholdInput.Text = _settings.LargeUpdateThreshold.ToString(CultureInfo.InvariantCulture);
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PluginLog.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PluginLog.Error("Could not open the log folder", ex);
        }
    }

    private void AppendLogLine(string line) =>
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            if (LogBox.LineCount > MaxLogLines)
                LogBox.Text = LogBox.Text[LogBox.GetCharacterIndexFromLineIndex(LogBox.LineCount - MaxLogLines)..];
            LogBox.ScrollToEnd();
        });
}
