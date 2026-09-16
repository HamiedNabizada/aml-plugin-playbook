# 05 The AutomationML Editor plugin (WPF + WebView2)

## In short

- Target `net8.0-windows7.0` with `UseWPF` and `EnableDynamicLoading`; pin the contract `[4.3.0]` with `PrivateAssets=All`; no MEF attributes (see §1.1).
- `DisplayName` uses letters, digits and underscore only and is final before the first install; keep `Metadata.xml` and csproj version equal (see §1.3, §1.4).
- Create the bridge object in the constructor, boot WebView2 in `Loaded`, and never tear anything down in `Unloaded` (see §2.1, §2.2).
- Declare `IsDocumentLoaded` and never raise it; a new wrapper for the same file must not reload the canvas (see §3.1, §3.2).
- Bind to your hierarchy by CAEX ID with the name as fallback, and look the document up by reflection for views created late (see §3.3, §3.5).
- Readiness comes only from the page's `ready` message; buffer pushes before it, and drop readiness on navigation start and renderer crash (see §4.3, §4.4).
- Tell import fallout from user edits by content: the page answers each import with its own export, the host compares `changed` with it (see §5.3).
- Update is explicit, goes through update in place, reports every outcome, asks before large ID changes and suppresses timers during dialogs (see §6).
- No commands on the editor toolbar; Update, Refresh and one language menu live inside the view, with status line, findings list and picker (see §7).
- Never ship `Aml.Editor.Plugin.Contract.dll`; pack private DLLs, WebView2 files and the bundle explicitly, and list the nupkg after every packaging change (see §9).
- Test what can run outside the editor: mapper, bridge in a browser, package contents, contract drift (see §11).
- `starter/plugin/` implements these rules for EFL in one view with one WebView; copy it rather than one of the source plugins (see §12).

Read fully when: you create the plugin project, implement Update, or prepare the first package.
Skim when: the starter plugin already runs for your language and you only chase one symptom (use the section titles and the checklist).

This chapter covers the part of the stack that runs inside the AutomationML Editor: a WPF view that
implements the editor's plugin contract, hosts the browser modeler in a WebView2 control, talks to it
over a message bridge, and calls the mapper to move a diagram into and out of the open CAEX document.
Read it before you write the plugin project, again before you implement the Update button, and again
before you ship the first package. Most of the real bugs of the two source projects happened here, not
in the mapper: lost edits after a tab switch, ghost tabs after a fast document switch, a plugin that
vanished from the editor after a manual copy, import fallout recorded as user edits, a save that saved
the wrong document. Every section below names the failure it prevents. The modeler itself is covered
in [02-modeler-diagram-js.md](02-modeler-diagram-js.md), the wire format in
[03-exchange-format.md](03-exchange-format.md), the CAEX side of Update in
[04-aml-mapping.md](04-aml-mapping.md), test strategy in [07-testing-and-ci.md](07-testing-and-ci.md).

Two source plugins are referenced throughout:

- **AMLPetriNet** (`Aml.Editor.Plugin.PetriNet`, v0.1.13): one WebView for the whole view, a net picker
  when the document holds several nets, PNML text on the wire. Newer and cleaner; start from it.
- **AMLFPB.js** (`Aml.Editor.Plugin.FPB`, v0.7.3): one sub-tab with its own WebView per FPD instance
  hierarchy, FPB.js JSON on the wire, a two second hash poll that follows edits made in the AML tree.
  Older, larger, with more recorded fixes (rebuild races, echo windows, pending caches).

The starter in this repository (`starter/plugin/Aml.Editor.Plugin.Efl`, v0.1.0, display name
`EFLStarter`) condenses both into one view with one WebView, a diagram picker and JSON on the wire; its
test project checks the package. Section 12 maps the starter files to the sections below.

---

## 1. The plugin contract

### 1.1 Packages and target framework

Target `net8.0-windows7.0`, `UseWPF`, `EnableDynamicLoading`. Reference the editor packages with exact
versions and keep their assemblies out of your output.

```xml
<PackageReference Include="Aml.Editor.Plugin.Contract" Version="[4.3.0]">
  <PrivateAssets>All</PrivateAssets>
</PackageReference>
<PackageReference Include="Aml.Engine" Version="[4.*, 5.0.0)">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
<PackageReference Include="Aml.Editor.API" Version="[2.3.0]">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
<PackageReference Include="Aml.Skins" Version="2.*">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:33-44`, identical in
`AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:30-41`)

Why exact versions: both plugins reach into editor internals by reflection (save command, main view
model) and match enum names by string (`ApplicationTheme`). A floating `4.*` would change that surface
with no compile error. The comment that records this decision is at
`AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:24-29`. Bump by hand, then run the
contract tests (section 11).

`Aml.Editor.Plugin.Contract` 4.3.0 ships builds for `net8.0-windows7.0` and `net10.0-windows7.0` and
depends on `Aml.Engine` 4.5.0. Its nuspec states that since contract 4.2 a plugin does **not** export
itself through MEF; the editor loads plugins with an `AssemblyLoadContext`. Neither source plugin has a
single `[Export]` attribute. Do not copy MEF boilerplate from older examples.

### 1.2 Base class and interfaces

```csharp
public partial class PetriNetPlugin : PluginViewBase, ISupportsThemes, INotifyAMLDocumentLoad
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:23`; the FPD plugin has the same list at
`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:22`)

`Aml.Editor.Plugin.WPFBase.PluginViewBase` is a `UserControl` that implements `IAMLEditorView` (and
through it `IAMLEditorPlugin`). The XAML root is the base class itself:

```xml
<aml:PluginViewBase
    x:Class="Aml.Editor.Plugin.PetriNet.PetriNetPlugin"
    xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
    xmlns:aml="clr-namespace:Aml.Editor.Plugin.WPFBase;assembly=Aml.Editor.Plugin.Contract"
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:1-8`)

What the contract 4.3.0 offers (from the package's XML documentation), and what the source plugins use:

| Member | Meaning | Used |
|---|---|---|
| `DisplayName` | Tab title and name in the plugin menu | yes |
| `PackageName` | NuGet id the PlugIn Manager uses to update the package | yes, equals the package id |
| `IsReactive` | Editor calls `ChangeSelectedObject` / `ChangeAMLFilePath` | set `true` |
| `IsAutoActive` | Activate when loaded; the user can change it | default |
| `IsReadonly` | Declares the plugin will not change CAEX objects | not set (the plugins write) |
| `Commands`, `ActivatePlugin`, `TerminatePlugin` | Entries under the plugin menu | base class defaults |
| `InitialDockPosition` | `DockLeft`, `DockRight`, `DockBottom`, `DockTop`, `DockContent`, `DockContentMaximized`, `Floating` | `DockContent` |
| `CanClose` | User may close the pane | `true` |
| `PaneImage` | Header image | not set |
| `ChangeSelectedObject(CAEXBasicObject)` | Tree selection changed | FPD plugin only |
| `ChangeAMLFilePath(string)` | Current file path changed | FPD plugin records it |
| `PublishAutomationMLFileAndObject(string, CAEXBasicObject)` | Called after activation with path and selection | not used |
| `INotifyAMLDocumentLoad` | `DocumentLoaded(CAEXDocument)`, `DocumentUnLoaded()`, `ApplicationClose()`, event `IsDocumentLoaded` | yes |
| `INotifyAMLDocumentSaved` | `DocumentSaved(string)` | not used |
| `ISupportsThemes` | `OnThemeChanged(ApplicationTheme)` | yes |
| `ISupportsUIZoom` | `OnUIZoomChanged(double)` | not used |
| `IToolBarIntegration` | `ToolBarCommands` (`List<PluginCommand>`) on the editor toolbar | removed on purpose, see 7.1 |
| `ISupportsSelection` | Event `Selected` asks the editor to select a tree node | not used |
| `INotifyViewActivation` | `Activate(string)` when the editor activates a view | not used |
| `IEditorCommanding` | Editor sets a callback; `EditorCommandBase.SaveCAEXFile(...)` and friends run File/Save, Open, Close, GetCAEXFile, ImportLibraries, Capture | not used, see 6.6 |
| `IAMLEditorViewCollection` | One plugin, several dockable views | not used |

The identity block both plugins use:

```csharp
public override string PackageName => "Aml.Editor.Plugin.PetriNet";
public override DockPositionEnum InitialDockPosition => DockPositionEnum.DockContent;
public override bool CanClose => true;
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:280-284`)

### 1.3 DisplayName: identifier characters only

```csharp
// No dots or slashes: the editor turns DisplayName into a WPF x:Name and
// an XML element name in its config, and both reject them.
DisplayName = "AMLPetriNet";
IsReactive = true;
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:89-92`)

In early FPD versions a display name with a dot or slash threw an `ArgumentException` on activation
and again when the editor closed. The editor stores the activation state per display name in
`%LOCALAPPDATA%\AutomationML\AutomationMLEditor\config.xml` as `<PLUGIN_<DisplayName>>True</...>` inside a
`<PluginActivation>` block; renaming the plugin leaves the old entry behind (a stale
`PLUGIN_FPB_js_Import_Export` entry still sits next to `PLUGIN_AMLFPBjs` on the development machine).
Pick the final name before the first public install. Letters, digits and underscore only.

### 1.4 Metadata.xml

Both plugins keep a `Metadata.xml` next to the assembly:

```xml
<PlugInPackageMetaData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                       xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <PackageName>Aml.Editor.Plugin.PetriNet</PackageName>
  <DisplayName>AMLPetriNet</DisplayName>
  <Version>0.1.13</Version>
  <Author>VDI 3682 Project</Author>
  <Description>...</Description>
  <MinimumEditorVersion>6.4.0.0</MinimumEditorVersion>
</PlugInPackageMetaData>
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Metadata.xml:1-10`)

It is copied to the build output (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:63`)
but the packages built from both projects do not contain it (checked by listing them). The name, description
and version the PlugIn Manager shows come from the nuspec, which MSBuild derives from `<Title>`,
`<Description>`, `<Version>`, `<Authors>`, `<PackageTags>`. Keep `Metadata.xml` in sync with the csproj
version by hand anyway; both projects do (0.1.13 and 0.7.3), and a drift has confused manual installs.

---

## 2. Construction order and view lifecycle

### 2.1 Create the bridge in the constructor, boot it in Loaded

```csharp
// Created here, not in Loaded: the editor can call DocumentLoaded before
// the view is loaded, and a null bridge would drop that net on the floor
// instead of buffering it until the page boots.
CreateBridge();

Loaded += async (_, __) =>
{
    ...
    await EnsureBridgeAsync();

    if (_document == null)
    {
        // A view constructed after the editor fired DocumentLoaded
        // (undock, late install, session restore) never receives that
        // callback, so look the document up directly.
        var found = DocumentDiscoverer.TryFindCurrentDocument();
        if (found != null) AttachDocument(found);
    }
};
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:124-151`)

Split the bridge in two: `CreateBridge` wires events synchronously and is cheap; `InitAsync` boots
WebView2 and is slow (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:163-200`,
`:227-231`). Before this split the Petri net plugin could receive `DocumentLoaded` before `Loaded`, find
`_bridge == null`, and silently lose the net while the log claimed it was buffered.

### 2.2 Never tear down on Unloaded

```csharp
// Deliberately no Unloaded handler. This editor fires Loaded/Unloaded on
// every reparent of the visual tree, tab switches and panel resizes
// included, and the FPD plugin learned the same lesson: tearing the
// bridge down here reloaded the page each time and threw away every
// unsaved canvas edit with it. The WebView lives as long as the view;
// DocumentUnLoaded and ApplicationClose are the ends of its life.
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:153-158`)

The FPD plugin records the other symptom: disposing on `Unloaded` caused an endless cycle
(Unloaded, dispose, Loaded, discover, rebuild) that accumulated state inside FPB.js
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:169-180`). It keeps only a detach of the log
subscription in `Unloaded` and re-subscribes defensively in `Loaded` with `-=` before `+=`
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:156-159`). The Petri net plugin shipped with a
teardown on `Unloaded` first; its own audit found eight re-navigations in one editor session in the log,
each re-importing the document state over unsaved edits. Lifetime rule: the WebView lives as long as
the view object. Real ends are `DocumentUnLoaded` (per document state) and `ApplicationClose` (everything).

### 2.3 The ends of life

```csharp
public void ApplicationClose()
{
    _bridge?.Dispose();
    _bridge = null;
    try { PluginLog.Shutdown(); }
    catch { /* nothing useful left to do while shutting down */ }
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:369-375`)

`DocumentUnLoaded` resets per-document state, clears findings, shows the placeholder and invalidates
command state (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:346-367`). It marshals to
the UI thread first: the contract does not document which thread calls it.

### 2.4 Threading

Everything that touches WPF, WebView2 or the CAEX document runs on the UI thread. `Aml.Engine` is not
thread safe, so the mapper runs synchronously on the UI thread too
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:284-287`). Marshal every editor callback:

```csharp
private void AttachDocument(CAEXDocument document)
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.Invoke(() => AttachDocument(document));
        return;
    }
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:382-388`)

Known cost: an Update on a large FPD document froze the editor for up to 23 s (mapper, validation and
save in one UI-thread call); the save part is timed in the log for that reason
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/EditorSaver.cs:76-92`). The FPD plugin times each phase
separately (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:350-356`). If you move read-only work
(validation) off the thread, marshal the result back.

---

## 3. Getting the document and the selection

### 3.1 DocumentLoaded, and the feedback loop you must not create

```csharp
/// Part of the contract for other subscribers. Deliberately never raised
/// from DocumentLoaded: the editor listens to it and answers by calling
/// DocumentLoaded again, which feeds itself (the FPD plugin measured 40
/// calls a second before this was understood).
#pragma warning disable CS0067 // required by INotifyAMLDocumentLoad, raised by nobody here
public event EventHandler<CAEXDocument>? IsDocumentLoaded;
#pragma warning restore CS0067
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:288-296`; the original note is at
`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:289-293`)

`IsDocumentLoaded` means "the plugin loaded a document and wants the editor to open it". Declare it,
never raise it from `DocumentLoaded`.

### 3.2 The same file can arrive as a new wrapper

The editor sometimes hands the plugin a new `CAEXDocument` object for the file that is already open.
Reattaching on reference inequality reloads the canvas and throws away unsaved edits.

```csharp
public void DocumentLoaded(CAEXDocument document)
{
    if (document == null)
    {
        DocumentUnLoaded();
        return;
    }
    if (ReferenceEquals(document, _document)) return;

    if (IsSameDocument(document, _document))
    {
        PluginLog.Debug("DocumentLoaded: same document, new wrapper. Keeping the current diagram.");
        _document = document;
        return;
    }

    AttachDocument(document);
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:298-320`)

Identity is `(SourceDocumentInformation.OriginID, CAEXFile.FileName)`
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:327-344`). Neither half works alone:
`OriginID` names the **authoring tool** and is the same GUID in every file the editor ever saved. The
FPD plugin first deduplicated on `OriginID` alone; a second showcase file then looked "already rebuilt",
its tabs never rendered, and Update wrote into the wrong document
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:386-398`). Two new, unsaved documents have an empty
identity and must count as distinct (`:339-340`).

Open weakness, still in both plugins: `FileName` is the bare name, so two files with the same name in
different folders count as the same document. `ChangeAMLFilePath` delivers the full path; the FPD
plugin records it (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:196-200`) but does not yet use it
for identity. In a new plugin, key identity on the full path from `ChangeAMLFilePath`, with
`(OriginID, FileName)` as a fallback for unsaved documents. The starter compares `OriginID` plus
`FileName` like the Petri net plugin and treats an empty `FileName` as distinct
(`starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:220-237`), so it shares the bare-name
weakness; replace that helper when you adapt it.

### 3.3 Views constructed after the document was opened

A view that is created after `DocumentLoaded` fired (undock and redock, editor restart with a restored
session, installing the plugin while a file is open) never gets that callback. Both plugins look the
document up by reflection on the main window's view model:

```csharp
var mainWindow = Application.Current?.MainWindow;
...
var vm = mainWindow.DataContext;
...
var doc = FindCaexDocumentOnObject(vm, depth: 0);
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/DocumentDiscoverer.cs:43-77`)

The walk takes any property typed `CAEXDocument`, then descends at most two levels into properties named
`CurrentDocument`, `ActiveDocument`, `Document`, `CAEXDocument`, `AMLDocument`, `OpenedDocument`,
`SelectedDocument` (`:26-35`, `:79-118`). Every failure is logged and returns null; the worst case is the
old behaviour (user reopens the file). The editor's view model is `Aml.Editor.ViewModels.MainViewModel`
(visible in the editor's own `ErrorLog.txt` stack traces).

Run discovery at `DispatcherPriority.Background`, and re-check that no `DocumentLoaded` raced in, because
reflecting into the view model while the editor is still loading a file can crash `Aml.Engine`:

```csharp
Dispatcher.BeginInvoke(new Action(() =>
{
    if (_currentDocument != null) return; // DocumentLoaded raced in: abort discovery.
    var doc = DocumentDiscoverer.TryFindCurrentDocument();
    if (doc == null) return;
    if (_currentDocument != null) return; // double-check after the async reflection call.
    ...
}), System.Windows.Threading.DispatcherPriority.Background);
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:257-271`; the Petri net plugin calls it directly
from `Loaded` at `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:140-147` and would
benefit from the deferral)

### 3.4 Selection

With `IsReactive = true` the editor calls `ChangeSelectedObject(CAEXBasicObject)` on every tree click.
The FPD plugin uses it as a second source for the document (`selectedObject.CAEXDocument`) and ignores a
cleared selection so that the bound document survives
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:202-219`). Always call `base.ChangeSelectedObject`.

Treat this callback as re-entrant. Running the editor's save command from inside an Update click can make
the editor call `ChangeSelectedObject` synchronously, which in the FPD plugin could dispose the very view
whose click handler was still running. Guard with `_disposed` after every call that leaves your code:

```csharp
var result = FpbJsonToCaex.UpdateInPlace(_doc, snapshot, _ih, mapperOptions);
swMapper.Stop();
// A re-entrant ChangeSelectedObject during the mapper call can
// have disposed this view. Bail out before touching now-null
// _doc/_ih any further.
if (_disposed) { PluginLog.Debug($"[{_ihLabel}] Update aborted post-mapper: view disposed during operation."); return; }
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:308-313`, same after validation `:329` and after
save `:337-341`, and in the catch block `:366-370`)

The Petri net plugin does not override `ChangeSelectedObject` and binds only through `DocumentLoaded`
plus discovery; that is enough for a plugin that shows the document's nets regardless of the tree
selection. If you want "click an element in the tree, select it on the canvas", this is the hook; the
reverse direction is `ISupportsSelection.Selected` (not used by either plugin, so unverified).

### 3.5 Find your hierarchies, bind by ID

Find the instance hierarchies that carry your language (the mapper answers this:
`CaexToPtNet.ContainsNet`, `CaexToFpbJson.FindFpdInstanceHierarchies`). Bind by the hierarchy's CAEX ID,
keep the name for display and as a fallback, and follow renames:

```csharp
if (!string.IsNullOrEmpty(_hierarchyId))
{
    var byId = hierarchies.FirstOrDefault(ih => ih.ID == _hierarchyId);
    if (byId != null)
    {
        // Follow a rename rather than fighting it.
        if (byId.Name != _hierarchyName) { ... _hierarchyName = byId.Name; }
        return byId;
    }
}
return _hierarchyName == null ? null : hierarchies.FirstOrDefault(ih => ih.Name == _hierarchyName);
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:431-453`)

Binding by name alone meant a rename in the editor tree lost the binding, and the next Update created a
second hierarchy. The mapper must write an ID on hierarchies it creates for this to work
([04-aml-mapping.md](04-aml-mapping.md)). The FPD plugin raises `LabelChanged` when it notices a rename
after a push and retitles the tab (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:225-235`).

`Aml.Engine` wrappers are not reference stable: every access can return a new wrapper object. Compare
IDs, or the underlying `XElement` (`.Node`), never wrapper references.

---

## 4. Hosting WebView2

### 4.1 Boot sequence

```csharp
public async Task InitAsync()
{
    if (_navStarted || _disposed) return;
    try { await _view.EnsureCoreWebView2Async(); }
    catch (Exception ex) { ReportError("WebView2 runtime missing or failed to start: " + ex.Message); return; }

    // The await above can complete after teardown; touching Settings on a
    // disposed control throws.
    if (_disposed) return;

    var settings = _view.CoreWebView2.Settings;
    settings.AreDevToolsEnabled = true;
    settings.AreDefaultContextMenusEnabled = true;
    settings.IsStatusBarEnabled = false;

    var assets = ResolveAssetsPath();
    ... // fail loudly if the folder, index.html or the bundle is missing
    _view.CoreWebView2.SetVirtualHostNameToFolderMapping(
        VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);
    ... // create handlers, then subscribe (section 4.4)
    _navStarted = true;
    _view.CoreWebView2.Navigate($"https://{VirtualHost}/index.html");
}
```
(condensed from `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:77-158`; same structure in
`AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:45-122`)

Order that works:

1. `EnsureCoreWebView2Async()` inside try/catch (a missing runtime is a user-facing error, not a crash).
2. Re-check `_disposed` after the await.
3. Settings. Keep DevTools on: right click in the canvas, Inspect, is the fastest way to debug the page
   inside the editor.
4. Resolve and check the assets folder (4.2).
5. Map the virtual host.
6. Subscribe `WebMessageReceived`, `NavigationCompleted`, `NavigationStarting`, `ProcessFailed`.
7. Navigate. The page announces itself with `ready` (4.3); only then is the bridge usable.

### 4.2 Assets: virtual host mapping of the bundled folder

Ship the modeler bundle inside the package, in a folder next to the plugin DLL, and serve it through
`SetVirtualHostNameToFolderMapping` under a private host (`ptnjs.local`, `fpbjs.local`). An `https://`
origin lets ES modules, import maps and CSS load without `file://` restrictions and without a local web
server. Resolve the folder from the plugin assembly, not from the editor's base directory:

```csharp
private static string ResolveAssetsPath()
{
    var location = typeof(PtWebView).Assembly.Location;
    var pluginDir = !string.IsNullOrEmpty(location) ? Path.GetDirectoryName(location) : null;
    return Path.Combine(pluginDir ?? AppContext.BaseDirectory, AssetsFolder);
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:160-165`)

In the installed editor that resolves to
`%APPDATA%\AutomationMLEditor\PlugIns6\<PackageId>.<Version>\lib\net8.0-windows7.0\ptnjs-assets`
(confirmed from the startup log of an installed plugin). Log the resolved path on every boot
(`OnInfo?.Invoke("Assets path: " + assets)`); it answers most "blank panel" reports at once.

Rules for the bundle ([02-modeler-diagram-js.md](02-modeler-diagram-js.md) has the details):

- One ESM file, no async chunks, nothing from a CDN. The editor may run offline.
- The Petri net bundle keeps minification **off**: diagram-js' injector falls back to constructor
  parameter names where `$inject` is missing, and a minified build died at boot
  (`AMLPetriNet: web/build.mjs:24-43`).
- Bare specifiers (`react`, `react-dom`) need an import map in `index.html` and vendored files. The
  esm.sh build of `react-dom` carried an absolute `/react@x/...` import that 404s under the virtual host;
  the FPD csproj rewrites it before every build
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:126-140`).
- Fonts and icons inlined into the stylesheet, so one `<link>` suffices
  (`AMLPetriNet: web/build.mjs:62-77`).

### 4.3 The ready handshake

`Navigate` returning, or `NavigationCompleted` succeeding, does **not** mean the page can receive
messages. A `PostWebMessageAsJson` before the page has registered its listener is dropped without any
error. Readiness is asserted only by the page:

```js
window.chrome?.webview?.addEventListener('message', async (e) => { ... });

post({ type: 'ready', url: location.href });
```
(`AMLPetriNet: web/src/bridge.js:188-239`: the listener is registered first, `ready` is the last
statement)

On the host, buffer everything that arrives before `ready`, and report whether a push went out:

```csharp
public bool ImportPnml(string pnml)
{
    if (string.IsNullOrEmpty(pnml) || _disposed) return false;

    if (!_ready || _view.CoreWebView2 == null)
    {
        _pendingPnml = pnml;
        return false;
    }

    Post(new { type = JsMessageType.ImportPnml, pnml });
    return true;
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:172-184`)

The boolean matters for echo suppression (5.3): a buffered push posts later, so it must not arm anything
now. Theme is buffered the same way (`:219-230`). On `ready` the bridge sets `_ready`, raises `Ready`,
then flushes the buffers (`:260-265`, `:293-305`).

Ordering trap: the host's `Ready` handler runs **before** the flush. In the FPD plugin the `Ready`
handler pushes the current hierarchy (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:129-134`)
and the flush afterwards pushes the older buffered payload over it
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:228-233`, `:271-275`); this is recorded as an
open issue. The Petri net plugin avoids the double push by checking `HasPendingImport` before pushing
from `Ready`:

```csharp
if (_pendingPnml != null)
{
    _restoringPending = true;
    _bridge.ImportPnml(_pendingPnml);
}
else if (!_bridge.HasPendingImport)
{
    var hierarchy = CurrentHierarchy();
    if (hierarchy != null) PushToModeler(hierarchy);
}
if (_lastTheme != null) _bridge.SendTheme(_lastTheme);
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:208-225`)

The starter moves the rule into the bridge: on `ready` it takes the buffered push into a local, clears
the buffer, runs the host's `Ready` handler while counting the imports it posts (`_inReadyHandler`,
`_importsDuringReady`), and posts the buffered push only if the handler posted no import itself
(`starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:217-245`). The handler is therefore
authoritative and needs no pending-import check: it always shows the unsaved edits (`_pendingModel`,
with `_restoring` set) or reads the bound hierarchy from the document again
(`starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:130-146`), both newer than the buffer.

Treat `Ready` as "a page booted", not "the first page booted". It fires again after F5, a context menu
reload, or a renderer crash recovery. Each time the new page knows nothing: resend the theme, and prefer
the unsaved diagram over the document (the `_restoringPending` flag keeps the import acknowledgement from
clearing the pending state, `:184-197`).

### 4.4 Reloads and renderer crashes

```csharp
// A fresh navigation (first load, or F5 from the context menu) tears
// down the page's message listener; anything posted before the new page
// announces itself would be dropped.
_navigationStartingHandler = (_, __) => _ready = false;
// A renderer crash otherwise leaves a blank tab with _ready still true,
// and every later push vanishes without a trace.
_processFailedHandler = (_, e) =>
{
    _ready = false;
    ReportError($"WebView2 process failed ({e.ProcessFailedKind}); reloading the page.");
    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
        || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
    {
        _view.Dispatcher.BeginInvoke(Reload);
    }
};
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:131-148`)

Both handlers were missing in the FPD plugin until its stability sprint; a crashed renderer left a white
tab with `_ready` still true and every later push silently lost. The FPD version only reports and asks
for a manual refresh (`AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:106-113`); the Petri net
version reloads for renderer failures, which the host's `Ready` handler then refills. A dead browser
process cannot be fixed by a reload.

A failed navigation also drops readiness and logs `WebErrorStatus`
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:123-130`).

### 4.5 User data folder

Neither plugin passes a `CoreWebView2Environment`, so WebView2 uses its default user data folder, which
for this editor is `AMLEditor.exe.WebView2` next to `AMLEditor.exe` (observed on the development
machine). Consequences to plan for:

- All WebView2 controls of all plugins in the editor process share that folder and its browser process.
  If you ever create your own environment, every control in the process that uses the same folder must
  use compatible options, or creation fails.
- `localStorage` persists there across sessions and is shared by pages on the same virtual host name.
  Give each plugin its own host name. The FPD page writes the theme there
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:304`); do not rely on it for anything the
  document should carry.
- If the editor is installed in a folder the user cannot write to, the default location is not writable.
  This case was not tested in either project. If you need to support it, pass an explicit user data
  folder under `%LOCALAPPDATA%` via `CoreWebView2Environment.CreateAsync` before
  `EnsureCoreWebView2Async`, and log it.

### 4.6 Disposal and leaks

Keep handler delegates in fields so they can be unsubscribed; `Dispose` detaches them, drops readiness
and clears buffers, and tolerates a `CoreWebView2` that is already gone
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:313-341`).

Detaching handlers does **not** end the browser process. When views are created and destroyed at runtime
(the FPD plugin creates one per hierarchy on every document switch, New Process and Import), dispose the
control as well:

```csharp
// _bridge.Dispose only detaches the CoreWebView2 event handlers; the
// WebView2 control itself (and its msedgewebview2 child process) lives
// on until finalisation. Over a long session every doc-switch / New
// Process / Import spawns a fresh IhView, so without an explicit dispose
// the browser processes accumulate and drag the editor down.
try { WebView?.Dispose(); } catch { /* best effort */ }
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:862-871`)

Also stop timers (`StopLiveSync`, `:861`) and check `_disposed` at the top of every entry point that can
be reached from a message, a timer tick or a click.

### 4.7 One WebView, or one per hierarchy

| | One WebView, picker (AMLPetriNet) | One WebView per hierarchy (AMLFPB.js) |
|---|---|---|
| Browser processes | one | one per hierarchy, leak risk (4.6) |
| Switching | reimport, confirm if unsaved (7.4) | instant, each tab keeps its state |
| Unsaved edits on document switch | one pending buffer | per-hierarchy cache keyed by document identity and hierarchy ID (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:58-71`, `:491-509`) |
| Rebuild races | none | needs a generation token (7.5) |

Start with one WebView and a picker unless users genuinely edit several hierarchies side by side.

---

## 5. Bridge messages

### 5.1 Protocol

Keep the message set small, typed by a `type` field, and write it down at the top of the JS bridge:

```js
 * Host -> page:
 *   { type: 'importPNML', pnml }   replace the diagram
 *   { type: 'setTheme', theme }    'light' | 'dark'
 *   { type: 'selectElement', id }  select and scroll to a model element
 *   { type: 'requestExport' }      ask for the current PNML on demand
 *   { type: 'requestSvg' }         ask for the diagram as an SVG picture
 *
 * Page -> host:
 *   { type: 'ready', url }
 *   { type: 'imported', pnml }     import finished; pnml is the echo baseline
 *   { type: 'changed', pnml }      user edit
 *   { type: 'svg', svg }           answer to requestSvg
 *   { type: 'log', level, message }
 *   { type: 'error', message }
```
(`AMLPetriNet: web/src/bridge.js:8-21`)

Mirror the tags as constants and the payload as one DTO on the C# side
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/BridgeMessage.cs:10-42`). Dispatch with a `switch`, and
report unknown types as errors (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:242-291`).
The FPD plugin adds `processSwitched` (layer changes inside FPB.js,
`AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/BridgeMessage.cs:24-37`).

Payload choice: a string field carrying the exchange document (PNML text) is simplest. When the payload
is JSON, never build the envelope by string concatenation; parse it and let the serializer embed it, so
invalid mapper output fails on the host with a clear message:

```csharp
using var doc = JsonDocument.Parse(json);
envelope = JsonSerializer.Serialize(new
{
    type = JsMessageType.ImportJSON,
    data = doc.RootElement
});
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:155-170`)

Page-side graphs with cycles (FPB.js business objects reference each other) need a replacer that turns
references into IDs before `JSON.stringify`
(`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:189-233`). An exchange format with its own
serializer (PNML) avoids that class of problem entirely.

### 5.2 Requests with answers

For a question with one answer (the SVG picture), use a `TaskCompletionSource` with a timeout rather
than an event subscription, so a crashed page cannot hang a dialog:

```csharp
var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
_svgRequest = answer;
try
{
    Post(new { type = JsMessageType.RequestSvg });
    var finished = await Task.WhenAny(answer.Task, Task.Delay(timeout)).ConfigureAwait(true);
    return finished == answer.Task ? await answer.Task.ConfigureAwait(true) : null;
}
finally
{
    _svgRequest = null;
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:201-217`)

### 5.3 Telling import fallout from user edits

This is the single most bug-prone part of the plugin. Importing a model into diagram-js fires a burst of
change events (and layout settling fires more, later). If the host records those as edits, a phantom
"unsaved changes" state appears after every load, and a later Update writes a stale or wrong diagram.

The path the FPD plugin took, so you do not repeat it:

1. A fixed time window after each push (3 s, widened after late settling events on slow machines,
   `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:47-52`). It swallowed genuine edits made inside
   the window.
2. The window was stamped before the push, so a push that was only buffered opened a fake window that
   ate real edits. Fixed by stamping only when the push really went out
   (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:204-222`).
3. The page's own `importing` flag was reset in `requestAnimationFrame`. WebView2 pauses rAF while the
   tab is hidden, so an import into a hidden sub-tab left the flag stuck and swallowed every later edit.
   Fixed with a `setTimeout` fallback and a guard so only the first path settles
   (`AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:275-299`).
4. Final design: **content-based echo baseline.** The page answers each import with `imported`, carrying
   its own export of what it just loaded. A later `changed` with byte-identical content is fallout;
   anything else is an edit, no matter how soon.

Do step 4 from the start. Page side:

```js
const runImport = async (pnml) => {
  importing = true;
  clearTimeout(pending);
  try {
    await modeler.importPNML(pnml);
  } catch (err) {
    post({ type: 'error', message: 'importPNML failed: ' + (err?.stack || err) });
  } finally {
    importing = false;
  }
  post({ type: 'imported', pnml: await exportPnml() });
};
```
(`AMLPetriNet: web/src/bridge.js:162-173`)

Host side:

```csharp
private void OnDiagramChanged(string pnml)
{
    if (string.Equals(pnml, _echoBaseline, StringComparison.Ordinal))
    {
        PluginLog.Debug("Change matched the import baseline; treated as import fallout.");
        return;
    }

    _pendingPnml = pnml;
    UpdatePendingLabel();
    ...
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:233-255`)

Details that each fixed a real bug:

- **Debounce changes on the page** (300 ms after the last `commandStack.changed`) and export once
  (`AMLPetriNet: web/src/bridge.js:26`, `:148-157`). One message per gesture, not per command.
- **Serialize imports.** Two imports in quick succession (document switch right after a refresh)
  interleaved at the `await`; the first `finally` cleared the flag while the second import was running
  and its traffic was reported as edits (`AMLPetriNet: web/src/bridge.js:193-200`).
- **Suppress changes during simulation or any other mode that writes through the command stack.** The
  Petri net modeler's token replay writes markings through the command stack; without suppression a
  mid-simulation marking reached the document on the next Update
  (`AMLPetriNet: web/src/bridge.js:130-137`, `:175-186`, and `requestExport` refuses during a replay,
  `:201-215`). Look for the equivalent in your modeler.
- **Re-anchor the baseline after an Update** on the content just written. Otherwise an undo back to the
  pre-update state matches the old baseline and is silently dropped
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:318-323`).
- **Clear a stale pending state when the canvas returns to the baseline** (the user undid everything),
  or Update writes the reverted edit (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:611-633`).
  The starter clears the pending model on a change equal to the baseline only when that baseline is the
  document's state (`_baselineIsDocument`, false after a restore of unsaved edits,
  `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:148-158`, `:160-173`).
- **An import that fails must not acknowledge the old content as the new baseline.** In the Petri net
  bridge `imported` is posted even after `importPNML` threw, carrying the previous net; its audit rates
  this latent (normalised IDs keep host-originated documents from failing). The starter bridge posts
  only `error` after a failed import and no `imported`, because the host treats `imported` as "the
  canvas shows what I sent" and would drop its unsaved edits (`starter/web/src/bridge.js:111-127`,
  checked in `starter/web/tools/verify-bridge.mjs:71-82`). Do the same in a new bridge.

### 5.4 Diagnostics through the bridge

Forward the page's console, `window.error` and `unhandledrejection` to the host log, keeping native
console calls working and never letting logging throw
(`AMLPetriNet: web/src/bridge.js:65-99`, `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html:100-143`).
Show fatal boot errors in the page and post them (`showFatal`, `AMLPetriNet: web/src/bridge.js:101-112`).
On the host, map levels: `error` and `warn` always, `log` and `debug` only with verbose logging
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:175-183`). Strip CR/LF from forwarded
lines so a page message cannot forge log records (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/PluginLog.cs:139-144`).
The `ready` message carries `location.href`; logging it verifies the virtual host mapping in one line.

The FPD plugin also shows a banner over the canvas with a "Refresh from AML" button when the page
reports an error (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml:100-133`,
`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:136-145`). An empty canvas with no explanation was
the most common complaint before.

---

## 6. The update flow

### 6.1 Direction and granularity

- **AML to canvas** is automatic: on attach, on hierarchy switch, on Refresh, on page boot.
- **Canvas to AML** is explicit: edits accumulate as one pending snapshot (the latest full export), and the
  **Update** button writes it. No auto-write on every change: the plugin cannot undo a document change,
  and the editor's own undo does not know about it.
- Update always goes through the mapper's **update in place**, never remove and re-append, so attributes,
  links and elements that others added survive ([04-aml-mapping.md](04-aml-mapping.md)).

### 6.2 Pending state must be visible

Show it next to the buttons (`PendingLabel`, `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:130-135`)
or in the tab header (`● ` prefix, `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:518-524`). Keep
the header a plain string so the editor skin's `TabItem` template still applies.

### 6.3 The Update handler

```csharp
private void UpdateButton_Click(object sender, RoutedEventArgs e)
{
    if (_document == null) { ...; SetStatus("No AML document is open."); return; }
    if (_pendingPnml == null) { ...; SetStatus("Nothing to write back: the diagram has no unsaved changes."); return; }

    try
    {
        var net = Pnml.Read(_pendingPnml);
        var hierarchy = CurrentHierarchy();
        if (hierarchy != null && !LargeUpdateConfirmed(hierarchy, net))
        {
            SetStatus("Update cancelled; the diagram still has its changes.");
            return;
        }

        PtUpdateSummary summary;
        if (hierarchy == null) { hierarchy = PtNetToCaex.AppendInto(_document, net, PtNetToCaex.DefaultHierarchyName); ... }
        else { summary = PtNetUpdater.UpdateInPlace(_document, hierarchy, net, _selectedNetIndex); }
        BindHierarchy(hierarchy);

        _pendingPnml = null;
        _echoBaseline = null;
        ...
        foreach (var note in summary.Notes) PluginLog.Warn(note);
        var saved = _settings.SaveAfterUpdate && EditorSaver.TrySaveActiveDocument();
        SetStatus($"Updated '{hierarchy.Name}': {summary}." + (saved ? " Saved." : " Press Ctrl+S to save."));
    }
    catch (Exception ex) { PluginLog.Error("Failed to write the net into AML", ex); SetStatus("Update failed: " + ex.Message); }
}
```
(condensed from `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:609-683`)

Requirements this encodes:

- Say something in the status line for every outcome, including "nothing to do" and "cancelled".
- Report the mapper's summary (added, updated, removed) and its notes. A note means the update reached
  outside the hierarchy (for example removed a foreign link to a deleted element); the user should hear
  about it, not discover it.
- Disable the button while the update runs and restore it in `finally`
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:288-289`, `:375-383`).
- On exception: log with stack, tell the user, keep the pending edits.
- Before writing, the mapper must check its preconditions and only then modify. A throw halfway through
  leaves a half-written live document that the next Ctrl+S persists; the Petri net audit found exactly
  this in an earlier `UpdateInPlace`.

### 6.4 Confirmation for large updates

The dangerous case is a pending snapshot that does not belong to the document any more: a canvas that
was reloaded with another hierarchy, a phantom pending state from import fallout, an edit left over from
before lunch. Applying it deletes the difference. Both plugins ask first when the update is large:

```csharp
var before = stored.Nodes.Select(n => n.Id).Concat(stored.Arcs.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);
var after = incoming.Nodes.Select(n => n.Id).Concat(incoming.Arcs.Select(a => a.Id)).ToHashSet(StringComparer.Ordinal);

var added = after.Except(before, StringComparer.Ordinal).Count();
var removed = before.Except(after, StringComparer.Ordinal).Count();
if (added + removed <= _settings.LargeUpdateThreshold) return true;

return MessageBox.Show(
    $"This update adds {added} element(s) and removes {removed}. "
    + "Removed elements are gone from the document, together with anything attached to them.\n\n"
    + "Apply it?",
    "Update InstanceHierarchy", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:1133-1145`)

Count added and removed IDs, not element totals: the FPD plugin compares totals
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:423-430`), which misses a snapshot that removes
five elements and adds five different ones. Moves and renames must not ask. Default threshold 5, user
configurable, persisted. The FPD plugin additionally warns when the pending snapshot is older than 30
minutes (`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:386-410`).

### 6.5 Dialogs pump the dispatcher

`MessageBox.Show` runs a nested message loop. In the FPD plugin the live-sync timer ticked while the
confirmation dialog was open, saw an external change, dropped the pending snapshot, and the handler then
applied its local copy and overwrote that external edit. Suppress every timer driven path for the whole
operation, dialogs included:

```csharp
// Suppress live-sync for the ENTIRE operation, confirm dialogs included.
// MessageBox.Show pumps the dispatcher, so without this the poll timer
// could fire while a dialog is open, see an external tree change, and
// drop _pendingSnapshot: after which we'd apply our local `snapshot`
// copy and silently clobber that external edit.
_liveSyncSuppressed = true;
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:261-267`, reset in `finally` at `:375-383`)

The same applies to bridge messages arriving during a dialog: take the snapshot into a local variable at
the start of the handler and do not re-read the field afterwards.

A related open race in the Petri net plugin: `changed` is debounced by 300 ms, and clicking the WPF
Update button can commit an in-progress label edit on the canvas whose `changed` arrives after the handler
already wrote the older snapshot. `PtWebView.RequestExport` exists for this and has no caller yet
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs:186-191`). In a new plugin, make Update ask
the page for a fresh export and wait for it (with a timeout, as in 5.2) before writing.

### 6.6 Saving after Update

The contract has no "document modified" notification; the editor does not know that the plugin changed
the document, so its own dirty state and save prompt may not reflect the update. Both plugins run the
editor's save command by reflection, as a user setting:

```csharp
foreach (var name in CandidateProperties)   // SaveAMLCommand, SaveCommand, SaveActiveDocumentCommand, SaveCurrentAMLFileCommand
{
    var prop = vmType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
    if (prop == null) continue;
    if (prop.GetValue(vm) is not ICommand cmd) { ...; continue; }
    if (!cmd.CanExecute(null))
    {
        // Keep looking: a later candidate may be executable even when this one isn't.
        continue;
    }
    ... cmd.Execute(null) on the UI thread, timed ...
    PluginLog.Info($"Editor save command invoked via {vmType.Name}.{name} " +
                   "(editor may no-op if document was already clean).");
    return true;
}
```
(condensed from `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/EditorSaver.cs:22-111`)

Facts and rules:

- The reflection target resolves in the current editor: startup logs of both plugins show
  `MainViewModel.SaveAMLCommand resolved`.
- `CanExecute == false` on the first candidate must not end the search; returning early there disabled
  auto-save (`:65-74`).
- Report "save command invoked", not "saved". `CanExecute` says nothing about whether the editor wrote.
- The save command saves the **active** document, which may not be the one the plugin is bound to when
  several files are open. Neither plugin guards this yet. Before invoking, compare the bound document with
  the one discovery returns and skip the save (with a status message) when they differ.
- Defaults differ, and both are defensible: the Petri net plugin saves by default because an unsaved
  update is lost when the editor closes (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/PluginSettings.cs:15-20`);
  the FPD plugin does not, because a saved update cannot be undone by closing without saving
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/PluginSettings.cs:18`). Whatever the default, say in the
  status line which happened: "Saved." or "Press Ctrl+S to save."
- New Net and Import also change the document; decide whether they save too, and be consistent.

Untested alternative: contract 4.3.0 declares `IEditorCommanding` (the editor sets an `EditorCommand`
callback) and `EditorCommandBase.SaveCAEXFile(IEditorCommanding, out SaveCAEXFileCommandArguments)`,
documented as identical to File/Save. Neither source plugin tried it. Try it first in a new plugin; keep
the reflection path as fallback and log which one ran.

### 6.7 Changes made in the AML tree

`Aml.Engine` has no public change event. The FPD plugin polls: every 2 s, per hierarchy, SHA-256 over the
hierarchy's XML; on a change it re-pushes the hierarchy to the canvas
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:655-716`, hash at `:742-759`). If the canvas has
pending edits at that moment, they are dropped, but first written to
`%TEMP%\fpb-plugin\pending-backup\` with the path in the status line
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:689-703`, `:724-740`).

Known limits of polling: hashing large hierarchies every 2 s on the UI thread causes micro stalls; a
hierarchy deleted in the tree still hashes its detached XML, so the tab stays and Update writes into a
detached tree; a new hierarchy added in the tree gets no tab. If the hash computation fails it must log,
because returning an empty hash makes every later tick look unchanged
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:750-757`).

The Petri net plugin does not poll; the user presses **Refresh**, which asks before discarding pending
edits (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:685-707`). Start there. Add polling
only if users edit the same hierarchy in the tree while the canvas is open, and then include the backup.

---

## 7. UI conventions that worked

### 7.1 No commands on the editor toolbar; one menu per language

The editor toolbar shows the `ToolBarCommands` of **one plugin at a time**. With both plugins installed,
the FPD commands appeared inside the Petri net tab; with one plugin, New Process and Import appeared twice
(on the toolbar and inside the view). Both plugins removed `IToolBarIntegration`
(`AMLFPB.js` commit `a96fc36`, "no commands on the editor toolbar") and put occasional commands into one
drop-down button inside their own view:

```xml
<Button x:Name="NetMenuButton" Click="NetMenuButton_Click" ToolTip="Create, import or export a Petri net.">
    <StackPanel Orientation="Horizontal">
        <TextBlock Style="{StaticResource IconText}" Text="&#xE700;" Foreground="#606060"/>
        <TextBlock Text="Net &#x25BE;" VerticalAlignment="Center"/>
    </StackPanel>
    <Button.ContextMenu>
        <ContextMenu x:Name="NetMenu">
            <MenuItem x:Name="NewNetItem" Header="New Net" .../>
            <MenuItem x:Name="ImportPnmlItem" Header="Import PNML..." .../>
            <Separator/>
            <MenuItem Header="Export PNML..." Click="ExportButton_Click" .../>
            <MenuItem Header="Export SVG..." Click="ExportSvgButton_Click" .../>
            <MenuItem Header="Save Conformance Report..." Click="SaveReportButton_Click" .../>
        </ContextMenu>
    </Button.ContextMenu>
</Button>
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:69-96`; opened below the button in code at
`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:978-984`; FPD equivalent "Process ▾" at
`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml:55-79`)

Layout of the view toolbar, identical in both plugins so they read as one family: **Update** and
**Refresh** as buttons of their own (used every session), a separator, the language menu, the picker
(7.4), the findings button, the status line. Glyphs from `Segoe MDL2 Assets` next to short labels, in a
WPF `ToolBar` so overflow goes into the overflow menu instead of being cut off.

If you still use `PluginCommand`: `CommandButtonContent` is a `FrameworkElement`, not a string, and a
command without content or icon rendered as an invisible empty button in earlier FPD versions.

Commands that need a document use `RelayCommand<object>` from the contract with a `CanExecute` on the
bound document, and call `CommandManager.InvalidateRequerySuggested()` whenever a document attaches or
detaches (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:121-122`, `:394`). The empty
placeholder offers the ways out of the empty state (New, Import) as buttons bound to the same command
objects (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml:39-49`, `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:129-132`).

### 7.2 Status line

```csharp
/// A short line next to the buttons saying what the last action did.
/// Everything also goes to the log, but the log sits on the second tab, and
/// an Update that reported its result only there looked as if nothing had
/// happened.
private void SetStatus(string text) =>
    Dispatcher.Invoke(() =>
    {
        if (StatusLabel != null) StatusLabel.Text = text;
    });
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:259-269`)

On load, say what was loaded and what was repaired, for example
`"Station 1: 6 places, 5 transitions, 12 arcs. 11 nodes had no layout and were arranged."`
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:495-497`).

### 7.3 Findings list with element names, automatic validation

Validate automatically on load, after Update and (count only) while the user draws; a Validate button
that had to be remembered meant nets were usually shown unchecked
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:711-783`). Open the panel by itself only
after a load or Update with errors, never while the user is drawing (`:780-781`).

Show the element by the **name the canvas shows**, not by ID. Documents from other tools carry GUIDs, and
a list of GUIDs says nothing:

```csharp
string NameOf(string? id)
{
    if (string.IsNullOrEmpty(id)) return "";
    var node = net.FindNode(id);
    if (node != null) return string.IsNullOrWhiteSpace(node.Name) ? id : node.Name!;
    var arc = net.Arcs.FirstOrDefault(a => a.Id == id);
    if (arc != null) return string.IsNullOrWhiteSpace(arc.Name) ? id : arc.Name!;
    return id;
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:735-743`)

Double-click selects the element on the canvas via `selectElement`
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:843-847`). The ID sent must be the
canvas ID: the FPD jump silently missed for every mapper-built element until CAEX braces were stripped
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/FindingRow.cs:41-44`). An unknown ID is a log line on the page,
not an error (`AMLPetriNet: web/src/bridge.js:221-232`).

Clear findings whenever a different net is loaded or written; a stale finding that still selects an
element is worse than none (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:795-809`).
Add "things the reader left off the canvas" as findings too, so a partially readable document is visible
(`:745-749`).

WPF detail: a `GridSplitter` resizes only a row with a concrete height; the findings row is sized in
pixels and collapsed to 0 when hidden (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:25-28`,
`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:785-790`). An `Auto` row did not resize.

### 7.4 Picker for several hierarchies or diagrams

A document may hold several diagrams (one net per station, in hierarchies of their own). Showing the first
and mentioning the rest in the log left them in the document and out of reach. List every diagram of
every matching hierarchy in a `ComboBox`, label `"<hierarchy> / <diagram>"` only when a hierarchy holds
several, hide the picker when there is one
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:521-569`). Guard against the selector
reacting to its own repopulation (`_updatingNetSelector`), and ask before a switch discards pending edits,
restoring the previous selection on cancel (`:575-605`).

### 7.5 Several views: generation token against rebuild races

If you build one view per hierarchy, rebuilding awaits WebView2 init per view. Those awaits let a second
rebuild (fast document switch, New Process, `DocumentUnLoaded`) interleave on the UI thread, and the stale
continuation adds ghost tabs bound to a closed document. The FPD plugin's main stability fix:

```csharp
DisposeAllIhViews();

var myGen = ++_rebuildGeneration;
...
foreach (var ih in ihs)
{
    if (myGen != _rebuildGeneration) { ...; return; }
    ...
    await view.BindAsync(doc, ih, label, _settings);

    if (myGen != _rebuildGeneration)
    {
        try { view.Dispose(); } catch { /* best effort */ }
        return;
    }
```
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:411-468`; `DocumentUnLoaded` bumps the token at `:308-310`)

Claim the token after the synchronous teardown, check it after every await, dispose the orphan view,
and bump it on unload. Do not use a semaphore instead: a hung WebView2 init would never release it
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:39-47`). Fire-and-forget rebuilds must catch and log
their own exceptions (`:351-374`), or they vanish in the unobserved task path.

After New Process or Import, rebuild against the document object the mapper returned, not the field: the
editor may have swapped wrappers in the meantime (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:563-580`).

### 7.6 Theme

```csharp
public void OnThemeChanged(ApplicationTheme theme)
{
    var name = theme.ToString().Contains("Dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
    _lastTheme = name;
    if (Dispatcher.CheckAccess()) _bridge?.SendTheme(name);
    else Dispatcher.Invoke(() => _bridge?.SendTheme(name));
}
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:1077-1086`)

Contract 4.3.0 values: `Default`, `Office2010Blue`, `Office2013`, `Metro`, `RoyalLight`, `RoyalDark`,
`MetroDark`, `NoTheme`. Map by substring, keep the last value in a field and resend it after every page
boot (4.3). The page sets `data-theme` on `<html>` and styles canvas ground and chrome for dark; check
label and arc contrast, which the Petri net audit found at about 1.4:1 on the dark ground. Leave WPF
controls to the editor's `Aml.Skins` theme (no custom `TabControl` styles,
`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml:23-26`).

### 7.7 Settings in %APPDATA%

```csharp
public static string FilePath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "AutomationMLEditor", "PetriNetPlugin", "settings.json");
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/PluginSettings.cs:38-40`)

Small JSON with snake_case keys, defaults on missing or unreadable file (a preference is not worth an
error dialog), best-effort save under a lock (`:42-74`). Petri net keys: `save_after_update`,
`ask_before_large_updates`, `large_update_threshold`, `debug_logging`. FPD adds `run_vdi_validation`,
`pending_age_warning_minutes`, `disabled_validation_rules`, `validation_min_severity`
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/PluginSettings.cs:17-44`). Expose the toggles on the
"Status / Diagnostics" tab; after a numeric input, write the stored value back so a typo does not look
accepted (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:1102-1115`). Load settings
before initialising logging if the debug toggle should affect startup output
(`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:79-84`).

### 7.8 Logging and startup self-check

`PluginLog` (identical in both plugins apart from names): one `StreamWriter` kept open with `AutoFlush`
so a crash still leaves the tail on disk, `FileShare.Read` so the file can be tailed while the editor
runs, rollover at 5 MB, `DEBUG` lines only when enabled, an `OnLine` event feeding the status tab
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/PluginLog.cs:18-236`). Location:
`%TEMP%\petrinet-plugin\petrinet-plugin-debug.log`, `%TEMP%\fpb-plugin\fpb-plugin-debug.log`. Put the path
and an "Open log folder" button on the diagnostics tab
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml:239-254`). Bound the on-screen log to 500
lines (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:1164-1182`).

Log the plugin version first thing in the constructor
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:97-98`), then run a self-check that
names every assumption that would otherwise fail late and obscurely:

- `System.Text.Json` round trip (not packed; the editor process provides it,
  `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/StartupCheck.cs:32-54`)
- web assets present (`:56-77`)
- `Aml.Engine` and WebView2 versions (`:79-98`)
- contract members, theme enum, save command reflection path, mapper constants
  (`AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/ApiCompatCheck.cs:28-118`)

Run the reflection checks again in `Loaded`, when `MainWindow.DataContext` is populated, and show failures
as a banner on the diagnostics tab (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:143-154`, `:707-727`).
Keep the candidate name lists of the check and of `EditorSaver` identical, or the check reports false
alarms (`AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/ApiCompatCheck.cs:91-95`).

Add a mapper trace callback and route it to `Debug` so one Update can be reconstructed from the log
(`AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:298-305`).

---

## 8. Import and export commands

All file commands follow one pattern: check preconditions, `OpenFileDialog`/`SaveFileDialog` with a
filter and a proposed file name, do the work in try/catch, log, status line.

- **Import** reads the exchange file as bytes (`File.OpenRead`, so the XML declaration decides the
  encoding; `ReadAllText` turned Latin-1 umlauts into replacement characters), takes **all** diagrams in
  the file (taking the first silently dropped the rest), arranges nodes without layout before writing so
  the document never holds geometry-free elements, and appends a new hierarchy with a unique name
  (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:932-976`, `:1061-1073`). Errors go into
  a `MessageBox`, since the user just chose a file.
- **New** appends an empty hierarchy, binds it and pushes it (`:909-930`). The FPD plugin asks for the
  process name with a small input dialog (`AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs:542-589`).
- **Export** of the exchange format writes what is on screen if the user edited it, otherwise the selected
  diagram from the document prepared exactly like the canvas got it (ID normalisation, auto layout), so the
  export never differs from the picture (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:878-905`,
  `:1028-1059`).
- **SVG export** asks the page to render (`requestSvg`, 10 s timeout) so the file shows the same layout the
  canvas shows (`:986-1026`). Add a margin around diagram-js' `saveSVG` bounding box, which otherwise cuts
  half a stroke and puts labels flush on the edge (`AMLPetriNet: web/src/svg.js:1-20`).
- **Conformance report** writes the findings of the net that is shown, kept from the last validation run
  so the report matches the panel (`AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs:79-83`,
  `:811-839`).

Because the sandboxed page cannot save files, every download is done by the host from data the page
returns. Do not implement downloads in the page.

---

## 9. Packaging and installing

### 9.1 Output and package

```xml
<!-- Anchored at this file, not at the solution: SolutionDir is empty when
     the project is built on its own, and the package then landed in a
     second build tree inside the project folder. -->
<BaseOutputPath>$(MSBuildThisFileDirectory)..\build\Plugins\$(MSBuildProjectName)</BaseOutputPath>
<GeneratePackageOnBuild>True</GeneratePackageOnBuild>
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:16-20`; the FPD csproj still
uses `$(SolutionDir)` at `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj:16`)

Every build writes `build/Plugins/<Project>/<Configuration>/<Package>.<Version>.nupkg`. The PlugIn
Manager installs from a local folder source pointing at that directory.

### 9.2 What goes into the nupkg

NuGet packing does not bundle project references or `PrivateAssets` packages, and anything left as a
nuspec dependency must be resolvable from a feed at install time (a project reference without
`PrivateAssets=all` failed the install with "Unable to resolve dependency"). Pack every private DLL
explicitly into `lib\<tfm>`:

```xml
<None Include="$(OutputPath)$(AssemblyName).deps.json" Pack="true" PackagePath="lib\$(TargetFramework)" />
<None Include="$(OutputPath)PtMapper.Conversion.dll" Pack="true" PackagePath="lib\$(TargetFramework)" />
...
<!-- NuGet.Pack drops items that point at runtimes/win-x64/native/, so the
     loader is staged under obj/ first and packed from there by file name. -->
<None Include="$(IntermediateOutputPath)bundled\WebView2Loader.dll"
      Link="WebView2Loader.dll"
      CopyToOutputDirectory="PreserveNewest" Visible="false"
      Pack="true" PackagePath="lib\$(TargetFramework)\WebView2Loader.dll" />
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:59-88`, staging target at
`:104-110`)

WebView2: `PackageReference` with `PrivateAssets=all` and `GeneratePathProperty="true"`, which exposes
`$(PkgMicrosoft_Web_WebView2)`; pack `Core.dll`, `Wpf.dll`, `Core.winmd` from the package folder and the
native `WebView2Loader.dll` via the staging copy. Without the path property the loader and winmd reached
neither `bin` nor the package. The packed file name in `PackagePath` is required for the native loader; a
folder-only `PackagePath` was silently dropped. Use `Link` metadata for every item included from outside
the project folder, or `CopyToOutputDirectory` does not know where to put it.

Known wrinkle: both csproj files pack `Microsoft.Web.WebView2.Wpf.dll` from `lib\net462`, while the build
compiles against a different target folder of the package. It works in the editor today; if you change
it, pack `$(OutputPath)Microsoft.Web.WebView2.Wpf.dll` instead and retest in the editor.

Web assets as content under `lib\<tfm>\<name>-assets\`, with a build target that fails early and says
what to run when the bundle is missing:

```xml
<None Include="$(PtnJsDistDir)\**\*"
      Link="ptnjs-assets\%(RecursiveDir)%(Filename)%(Extension)"
      CopyToOutputDirectory="PreserveNewest"
      Pack="true"
      PackagePath="lib\$(TargetFramework)\ptnjs-assets\%(RecursiveDir)%(Filename)%(Extension)" />
...
<Target Name="VerifyPtnJsDist" BeforeTargets="BeforeBuild">
  <Error Condition="!Exists('$(PtnJsDistDir)\ptnjs.esm.js')"
         Text="The modeler bundle is missing. Run 'npm install &amp;&amp; npm run build' in web\." />
</Target>
```
(`AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj:90-102`)

Resulting package (AMLPetriNet 0.1.13, 17 entries): plugin DLL, `deps.json`, `runtimeconfig.json`,
mapper DLL, the four WebView2 files, `ptnjs-assets/index.html`, `ptnjs.css`, `ptnjs.esm.js`, `LICENSE`,
`THIRD-PARTY-NOTICES.md`. Its nuspec lists `Aml.Editor.API`, `Aml.Engine`, `Aml.Skins` as dependencies with
`exclude="Runtime,Build,Analyzers"`, and **no** contract dependency. List the nupkg after every packaging
change (`unzip -l`), and install it once in a clean editor profile.

### 9.3 The contract assembly pitfall

Never ship `Aml.Editor.Plugin.Contract.dll` next to the plugin. The build output contains it (the contract
is `PrivateAssets` but still a runtime asset, and the `deps.json` the build writes lists
`lib/net8.0-windows7.0/Aml.Editor.Plugin.Contract.dll`), the package does not. When the build output was
copied wholesale into the installed plugin folder, the editor loaded a second copy of the contract into the
plugin's load context, the plugin's `PluginViewBase` no longer matched the editor's, and the plugin
disappeared from the editor without an error message. The README says so:

> When copying build output into an installed plugin folder by hand, leave out
> `Aml.Editor.Plugin.Contract.dll`. A second copy of that assembly makes the
> editor ignore the plugin without an error message.

(`AMLPetriNet: README.md:81-83`)

Rules: install through the PlugIn Manager from the nupkg. If you copy by hand (9.5), copy exactly the
package's file list. If the plugin is missing after an install, check the plugin folder for
`Aml.Editor.Plugin.Contract.dll` before anything else. Do not remove `PrivateAssets` from the contract
reference: a nuspec dependency on the contract would let the PlugIn Manager fetch it into the plugin
folder.

Observed on the development machine: the installed Petri net folder contains dependency assemblies the
package does not carry (`Aml.Editor.API.dll`, `Aml.Engine.dll`, `Aml.Skins.dll`, `MahApps.Metro.dll`,
`System.Text.Json.dll` and others), so the PlugIn Manager resolves nuspec dependencies into the plugin
folder at install time. Those did not break loading; the contract copy did. Keep the dependency list
minimal.

### 9.4 Where the editor keeps plugins and state

Map all of these before chasing an install problem. Several hours were lost once by fixing one state file
after another; the fix came only after listing every location and reading each format.

| What | Location |
|---|---|
| Installed plugins (the real load source) | `%APPDATA%\AutomationMLEditor\PlugIns6\<PackageId>.<Version>\lib\net8.0-windows7.0\` |
| PlugIn Manager state: installed plugins, package sources | `%APPDATA%\AutomationMLEditor\PlugInManager6.xml` (DataContract XML, namespace `Aml.Editor.PlugInManager.ViewModels`) |
| Activation per plugin | `%LOCALAPPDATA%\AutomationML\AutomationMLEditor\config.xml`, block `<PluginActivation>`, entries `PLUGIN_<DisplayName>` |
| Editor exceptions | `%LOCALAPPDATA%\AutomationML\AutomationMLEditor\ErrorLog.txt` |
| WebView2 profile | `AMLEditor.exe.WebView2\` next to the editor executable (4.5) |
| Plugin settings | `%APPDATA%\AutomationMLEditor\<YourPlugin>\settings.json` (7.7) |
| Plugin log | `%TEMP%\<your-plugin>\` (7.8) |

`%APPDATA%\AutomationML\Plugins\` (other name, no "Editor") is **not** read. An old onboarding note
recommended a post-build copy there; it never worked.

Do not hand-edit `PlugInManager6.xml` to register a plugin. The FPD project wrote a generator for it
(`AMLFPB.js: GeneratePluginXml/Program.cs:7-114`) to reproduce the editor's serialization and its
`RemoveAll(p => !p.IsInstalled)` filter (`:101-110`); the conclusion was that a hand-written state breaks
the manager, and the clean path is Uninstall and Install in the PlugIn Manager UI. Add a package source
pointing at the `Release` folder once, through the UI.

### 9.5 Iterating without a version bump

The PlugIn Manager will not reinstall a package with a version it already has, and
`GeneratePackageOnBuild` leaves older nupkgs in the output folder (two different artefacts labelled
0.1.2 existed at one point). For quick iteration at the same version, copy the package's exact file set
from the build output into the installed folder:

- plugin DLL, `.deps.json`, `.runtimeconfig.json`, mapper and other private DLLs, the four WebView2 files,
  the assets folder;
- never the contract DLL, never the whole output folder.

If the editor is running, the DLLs are locked; rename each old file (`x.dll` to `x.dll.old`), copy the new
one, restart the editor, delete the `.old` files. For anything you hand to someone else, bump the version.

---

## 10. Versioning

- One version per release in three places: `<Version>` in the csproj (drives assembly and package
  version), `Metadata.xml`, and the git tag `v<version>`. CI builds the nupkg on the tag and attaches it to
  the GitHub release (`AMLPetriNet: .github/workflows/ci.yml:70-77`, `AMLFPB.js: .github/workflows/build.yml:85-94`).
- Log `Assembly.GetName().Version.ToString(3)` at construction, so every log file says which build ran.
- Clean `build/Plugins/<Project>/Release/` before packaging a release, or upload by exact file name.
- Pin editor packages exactly (1.1) and treat a contract bump as a change that needs the contract tests and
  an editor run.
- Keep the web bundle and the host in step: a host that expects `imported` with a baseline must tolerate an
  older cached page that sends none (the FPD host falls back to the timed window when the baseline is null,
  `AMLFPB.js: Aml.Editor.Plugin.FPB/Bridge/FpbWebView.cs:250-259`,
  `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs:634-639`). Easier: version the bundle with the
  plugin and never mix.
- `MinimumEditorVersion` 6.4.0.0 in both metadata files; the FPD README states AutomationML Editor 6.4.0 or
  newer and the WebView2 runtime as requirements (`AMLFPB.js: README.md:20-22`).

---

## 11. Testing outside the editor

The editor cannot be driven by a test runner, and neither project automates it. Split what can be tested
and keep the rest small and logged.

1. **Mapper**: all conversion and update logic lives in the conversion library with xunit tests
   ([07-testing-and-ci.md](07-testing-and-ci.md)). Disable test parallelization; `Aml.Engine` races on
   parallel document construction:
   ```csharp
   [assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
   ```
   (`AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/TestCollection.cs:1-5`)
2. **Bridge protocol in a real browser**: install a stand-in for `window.chrome.webview` before the page
   boots and drive the same messages the C# host sends:
   ```js
   await page.addInitScript(() => {
     const listeners = [];
     window.__hostMessages = [];
     window.chrome = {
       webview: {
         postMessage: (message) => window.__hostMessages.push(message),
         addEventListener: (_type, handler) => listeners.push(handler),
       },
     };
     // What PostWebMessageAsJson does on the C# side: deliver an object as e.data.
     window.__hostSend = (message) => listeners.forEach((h) => h({ data: message }));
   });
   ```
   (`AMLPetriNet: web/tools/verify-bridge.mjs:30-43`). It checks: `ready` carries the URL; an import is
   acknowledged with a baseline and raises no `changed` after the debounce window (`:78-83`); a user edit
   is reported; two back-to-back imports report no edits (`:116-130`); simulation reports nothing and
   `requestExport` is refused during it; a broken import is an error and does not kill the modeler. Before
   this test the whole host-facing surface of the page was untested. The same test is the quickest way to
   check that a bundler change (minification) did not break the boot. The starter avoids the stand-in:
   `connectBridge(modeler, source)` returns its `handle` function, the page exposes it on `window.efl`,
   and `post` also dispatches an `efl-bridge-out` DOM event that the test records
   (`starter/web/src/bridge.js:40-47`, `:166-171`; `starter/web/tools/harness.mjs:50-54`). Its six checks
   cover `ready`, baseline without `changed`, one debounced change, two back-to-back imports, a broken
   import (an error and no `imported`), and the export, SVG and selection requests
   (`starter/web/tools/verify-bridge.mjs:17-99`).
3. **Contract drift by reflection**: the test project cannot reference the contract directly (the plugin's
   reference is `PrivateAssets`), so it touches the plugin type and finds the contract among the loaded
   assemblies:
   ```csharp
   _ = typeof(FpbPlugin).FullName;
   return AppDomain.CurrentDomain.GetAssemblies()
       .First(a => a.GetName().Name == "Aml.Editor.Plugin.Contract");
   ```
   (`AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs:16-25`), then asserts the
   `INotifyAMLDocumentLoad` members, Dark and Light names in `ApplicationTheme`, and `DisplayName` on the
   base type. Test projects need their own reference to the mapper project for the same reason
   (`AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/Aml.Editor.Plugin.FPB.Tests.csproj:22-25`).
4. **Runtime reflection targets** (save command, document discovery) cannot be tested offline: the editor
   ships as a single-file executable, so there is no assembly to reflect on in a test. Cover them with the
   startup self-check (7.8) and read the log after each editor update.
5. **WPF view logic**: neither project instantiates the plugin view in a test. If you do, create WPF objects
   on a dedicated STA thread with a running `Dispatcher` (xunit test threads are MTA), and keep WebView2 out
   of it by constructing the view with the bridge behind an interface. The cheaper route, and the one that
   paid off, is to move decisions out of the view into plain classes: identity comparison, large-update
   counting, finding row projection, echo baseline comparison. Those are then ordinary unit tests.
6. **Package contents**: a test project that references the plugin with `ReferenceOutputAssembly="false"`
   (so it only builds it) opens the newest nupkg of the csproj version and asserts: no
   `Aml.Editor.Plugin.Contract.dll`, no `Aml.Engine.dll`, the plugin DLL, mapper DLL, WebView2 files and
   bundle files present, `Metadata.xml` version equal to the csproj version, and `DisplayName` matching
   `^[A-Za-z_][A-Za-z0-9_]*$` in both `Metadata.xml` and the code
   (`starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:47-96`). Each of these failures is silent
   inside the editor.
7. **Manual editor script** before every release, in a clean profile: install from nupkg; open a document
   with no diagram (placeholder, New); open one with two diagrams (picker); edit, switch editor tabs,
   return (edits still pending); Update with and without save; Refresh with pending edits (asks); undock
   and redock; F5 in the canvas (diagram and theme come back); open a second file with the same name from
   another folder; close the editor with pending edits. Keep the log open while doing it.

---

## 12. The starter plugin

`starter/plugin/` is the shortest working form of this chapter: `Aml.Editor.Plugin.Efl.sln` with the plugin
and `Aml.Editor.Plugin.Efl.Tests`. It follows the Petri net plugin's single-WebView design and leaves out
the startup self-check, the conformance report and tree polling. Where it implements a rule:

| Rule | Starter location |
|---|---|
| Packages pinned, contract `PrivateAssets`, WebView2 pinned exactly and packed, no `Aml.Skins` reference (§1.1, §9.2) | `starter/plugin/Aml.Editor.Plugin.Efl/Aml.Editor.Plugin.Efl.csproj:27-80` |
| `BaseOutputPath` anchored at the project, bundle as content, build fails without the bundle (§9.1, §9.2) | same file `:19-24`, `:82-96` |
| `Metadata.xml` in step with the csproj version (§1.4) | `starter/plugin/Aml.Editor.Plugin.Efl/Metadata.xml:1-10` |
| `DisplayName`, bridge in the constructor, discovery in `Loaded`, no `Unloaded` handler (§1.3, §2.1, §2.2) | `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs:59-98` |
| `Ready` handler restores unsaved edits or rereads the document and resends the theme; content-based echo baseline; undone edits clear the pending model (§4.3, §5.3) | `EflPlugin.xaml.cs:126-186` |
| `IsDocumentLoaded` never raised, new wrapper for the same file kept (`OriginID` plus `FileName`), UI-thread marshalling (§3.1, §3.2, §2.4) | `EflPlugin.xaml.cs:190-297` |
| Hierarchy bound by ID with name fallback (§3.5) | `EflPlugin.xaml.cs:299-321` |
| Picker, Refresh and switch ask before discarding (§7.4) | `EflPlugin.xaml.cs:359-426` |
| Update through `EflUpdater.UpdateInPlace`, large-update confirmation by IDs, save as setting (§6.3, §6.4, §6.6) | `EflPlugin.xaml.cs:430-512` |
| New Diagram, Import JSON (arranged before writing), Export JSON, Export SVG (§8) | `EflPlugin.xaml.cs:516-615` |
| Automatic findings with element names, double-click selects (§7.3) | `EflPlugin.xaml.cs:619-662` |
| Toolbar inside the view, findings row sized in pixels, Diagnostics tab with settings (§7.1, §7.3, §7.7) | `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml:20-100` |
| WebView2 boot, virtual host `efl.local`, `ready`, buffers, reload on renderer failure, dispose (§4) | `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/ModelerView.cs:78-138`, `:140-172`, `:199-303` |
| SVG as a request with a timeout (§5.2) | `ModelerView.cs:174-194` |
| Message DTO and type constants (§5.1) | `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/BridgeMessage.cs:1-39` |
| Document discovery and save command by reflection (§3.3, §6.6) | `starter/plugin/Aml.Editor.Plugin.Efl/Bridge/EditorAccess.cs:32-100` |
| Log under `%TEMP%\efl-plugin\`, settings under `%APPDATA%\AutomationMLEditor\EflPlugin\` (§7.7, §7.8) | `starter/plugin/Aml.Editor.Plugin.Efl/Diagnostics/PluginLog.cs`, `PluginSettings.cs` |
| Package tests (§11) | `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs:47-96` |

The page side is `starter/web/src/bridge.js` (protocol at `:8-21`: `importModel`, `requestExport`,
`requestSvg`, `selectElement`, `setTheme`; `ready`, `imported` with `model` and `warnings`, `changed`,
`svg`, `log`, `error`). Known gaps compared with this chapter: identity by `OriginID` and the bare
`FileName` instead of the full path (§3.2), no fresh export before Update (§6.5), and no check that the
active document is the bound one before saving (§6.6).

---

## Checklist

Contract and lifecycle
- [ ] `net8.0-windows7.0`, `UseWPF`, `EnableDynamicLoading`; contract `[4.3.0]` with `PrivateAssets=All`; Engine, API, Skins with `ExcludeAssets=runtime`
- [ ] No MEF attributes; view derives from `PluginViewBase`, implements `INotifyAMLDocumentLoad` and `ISupportsThemes`
- [ ] `DisplayName` uses letters, digits, underscore only, and is final before the first install
- [ ] `PackageName` equals the package id; `IsReactive = true`; `InitialDockPosition = DockContent`
- [ ] Bridge object created in the constructor, WebView2 booted in `Loaded`
- [ ] No teardown in `Unloaded`; teardown in `DocumentUnLoaded` (per document) and `ApplicationClose`
- [ ] `IsDocumentLoaded` declared and never raised
- [ ] Every editor callback marshalled to the UI thread
- [ ] Document identity by full path (from `ChangeAMLFilePath`), with `(OriginID, FileName)` fallback; new wrapper for the same file keeps the canvas
- [ ] Document discovery by reflection for late-constructed views, at background priority, with race re-checks
- [ ] Hierarchy bound by CAEX ID, name as fallback, renames followed
- [ ] `_disposed` checked after every call that can re-enter (mapper, validation, save)

WebView2
- [ ] Assets folder resolved from the plugin assembly location and logged; folder, `index.html` and bundle existence checked
- [ ] Virtual host mapping with a host name unique to this plugin
- [ ] `ready` posted by the page after its listener is registered; host readiness only from `ready`
- [ ] Pushes and theme buffered before `ready`; push method returns whether it posted
- [ ] `Ready` handler does not double-push over a buffered import; theme and unsaved diagram restored on every boot
- [ ] `NavigationStarting` and `ProcessFailed` drop readiness; renderer failure reloads
- [ ] Handlers stored in fields and detached in `Dispose`; `WebView.Dispose()` for views created at runtime
- [ ] DevTools enabled; page console, errors and rejections forwarded to the host log

Bridge and update
- [ ] Protocol documented at the top of the JS bridge and mirrored as constants in C#
- [ ] Changes debounced on the page; imports serialized; simulation or replay modes suppress changes
- [ ] `imported` carries the page's own export; host compares `changed` content with it
- [ ] Baseline re-anchored after Update; pending cleared when canvas returns to baseline; failed import does not become a baseline
- [ ] Pending state visible (label or tab marker)
- [ ] Update: status for every outcome, mapper summary and notes logged, button disabled while running, pending kept on failure
- [ ] Update gets a fresh export from the page before writing (no 300 ms stale window)
- [ ] Large-update confirmation counts added and removed IDs; threshold persisted
- [ ] Timers and live sync suppressed during the whole Update, dialogs included
- [ ] Save after update as a setting; reflection tries all candidates; status says whether save ran; skipped when the active document is not the bound one
- [ ] Refresh and diagram switch ask before discarding pending edits
- [ ] If polling the tree: backup of dropped pending edits, logged hash failures

UI
- [ ] No `IToolBarIntegration`; Update and Refresh buttons plus one language menu inside the view
- [ ] Status line next to the buttons
- [ ] Findings list: automatic validation, element names, double-click selects on canvas, cleared on reload
- [ ] Picker for several diagrams, hidden when there is one
- [ ] Settings JSON under `%APPDATA%\AutomationMLEditor\<Plugin>\`, defaults on read failure
- [ ] Log file under `%TEMP%`, verbose toggle, open-folder button, version and startup self-check logged

Import and export
- [ ] Import reads bytes, takes all diagrams, arranges missing layout before writing, unique hierarchy name
- [ ] Export writes what the canvas shows, prepared the same way
- [ ] SVG export requested from the page with a timeout, margin added
- [ ] All file writes by the host, never by the page

Packaging and release
- [ ] `BaseOutputPath` anchored at `$(MSBuildThisFileDirectory)`; `GeneratePackageOnBuild`
- [ ] Private DLLs, `deps.json`, WebView2 Core, Wpf, winmd and staged loader packed into `lib\<tfm>`
- [ ] Web bundle packed as content with `Link`; build fails with a clear message when the bundle is missing
- [ ] nupkg listed after every packaging change; contract DLL not in it
- [ ] Manual copies use the package file list, never the contract DLL
- [ ] Version identical in csproj, `Metadata.xml`, tag; Release folder cleaned before packaging
- [ ] Contract reflection tests, bridge browser test, mapper tests in CI; manual editor script before release
- [ ] Package tests: no contract or `Aml.Engine` DLL, runtime files present, `Metadata.xml` version equals csproj version, `DisplayName` is a valid XML and WPF name

---

## Where to look

| Topic | File |
|---|---|
| Starting point for a new plugin (all of the above, condensed) | `starter/plugin/Aml.Editor.Plugin.Efl/EflPlugin.xaml.cs`, `Bridge/ModelerView.cs`, `Bridge/EditorAccess.cs`, `Aml.Editor.Plugin.Efl.csproj` |
| Package tests | `starter/plugin/Aml.Editor.Plugin.Efl.Tests/PackageTests.cs` |
| Page side of the starter bridge and its browser test | `starter/web/src/bridge.js`, `starter/web/tools/verify-bridge.mjs` |
| Plugin view, lifecycle, update, UI (single WebView) | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml.cs` |
| Toolbar with language menu, picker, findings, diagnostics tab | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/PetriNetPlugin.xaml` |
| WebView2 wrapper: boot, virtual host, ready, buffers, crash handling, dispose | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/PtWebView.cs` |
| Message DTO and type constants | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/BridgeMessage.cs` |
| Page side of the bridge: debounce, import queue, baseline, simulation guard | `AMLPetriNet: web/src/bridge.js` |
| Bridge test with a stand-in host | `AMLPetriNet: web/tools/verify-bridge.mjs` |
| Document lookup by reflection | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/DocumentDiscoverer.cs` |
| Editor save by reflection | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Bridge/EditorSaver.cs` |
| Log, settings, startup check | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Diagnostics/` |
| Packaging (WebView2 bundling, assets, output path) | `AMLPetriNet: Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` |
| Bundle build, minification note | `AMLPetriNet: web/build.mjs` |
| Multi-hierarchy tabs, rebuild token, pending cache, document dedupe | `AMLFPB.js: Aml.Editor.Plugin.FPB/FpbPlugin.xaml.cs` |
| Per-hierarchy view: echo window history, live-sync poll, backups, large-update and stale checks, re-entrancy guards | `AMLFPB.js: Aml.Editor.Plugin.FPB/Views/IhView.xaml.cs` |
| Page with import map, cycle-breaking snapshot, rAF plus timeout settle | `AMLFPB.js: Aml.Editor.Plugin.FPB/fpbjs-assets/index.html` |
| Compatibility self-check and banner | `AMLFPB.js: Aml.Editor.Plugin.FPB/Diagnostics/ApiCompatCheck.cs` |
| Contract drift tests | `AMLFPB.js: Aml.Editor.Plugin.FPB.Tests/ApiContractTests.cs` |
| Vendor patch target (react-dom import) | `AMLFPB.js: Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` |
| PlugIn Manager state format (investigation tool) | `AMLFPB.js: GeneratePluginXml/Program.cs` |
| CI: sibling checkouts, bundle build, nupkg release | `AMLFPB.js: .github/workflows/build.yml`, `AMLPetriNet: .github/workflows/ci.yml` |
