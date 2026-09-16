using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Aml.Editor.Plugin.Efl.Bridge;

/// <summary>
/// Owns the WebView2 that shows the modeler and the message protocol with it.
///
/// The rules this class keeps, each learned the hard way in AMLFPB.js and
/// AMLPetriNet:
/// <list type="bullet">
/// <item>The page is ready when it says so ("ready"), not when navigation
/// completes. Navigation completes before the page's module script has
/// registered its message listener, and a message posted in between is gone.</item>
/// <item>A reload or a renderer crash makes the page not ready again.</item>
/// <item>A push that arrives before the page is ready is buffered, and only the
/// newest one is kept.</item>
/// <item>Event handlers are removed on dispose; the control may outlive this
/// object.</item>
/// </list>
/// </summary>
public sealed class ModelerView : IDisposable
{
    /// <summary>
    /// The bundle is served from disk under this host name. A virtual host gives
    /// the page a real origin, so ES modules load; file:// URLs refuse module
    /// scripts.
    /// </summary>
    private const string VirtualHost = "efl.local";

    public const string AssetsFolder = "efl-assets";

    private readonly WebView2 _view;

    private bool _navigationStarted;
    private bool _ready;
    private bool _disposed;
    private string? _pendingModel;
    private string? _pendingTheme;
    private TaskCompletionSource<string?>? _svgRequest;
    private bool _inReadyHandler;
    private int _importsDuringReady;

    private EventHandler<CoreWebView2WebMessageReceivedEventArgs>? _onMessage;
    private EventHandler<CoreWebView2NavigationStartingEventArgs>? _onNavigationStarting;
    private EventHandler<CoreWebView2NavigationCompletedEventArgs>? _onNavigationCompleted;
    private EventHandler<CoreWebView2ProcessFailedEventArgs>? _onProcessFailed;

    public ModelerView(WebView2 view) => _view = view;

    /// <summary>The page booted (first load, reload, recovery). The host decides what to show.</summary>
    public event Action? Ready;

    /// <summary>An import finished. The payload is the page's own export of it, the echo baseline.</summary>
    public event Action<string?, IReadOnlyList<string>>? Imported;

    /// <summary>A user edit, with the whole diagram.</summary>
    public event Action<string>? Changed;

    public event Action<string>? Error;
    public event Action<string>? Info;
    public event Action<string, string>? PageLog;

    public bool IsReady => _ready;

    public static string AssetsPath
    {
        get
        {
            var location = typeof(ModelerView).Assembly.Location;
            var directory = string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            return Path.Combine(directory ?? AppContext.BaseDirectory, AssetsFolder);
        }
    }

    public async Task InitAsync()
    {
        if (_navigationStarted || _disposed) return;

        try
        {
            // The editor's default user data folder sits next to its executable.
            // That works for a per-user install; point it elsewhere through a
            // CoreWebView2Environment if the editor lives in a read-only folder.
            await _view.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            RaiseError("The WebView2 runtime is missing or failed to start: " + ex.Message);
            return;
        }

        // The await can finish after the view was torn down.
        if (_disposed) return;

        var assets = AssetsPath;
        if (!File.Exists(Path.Combine(assets, "index.html")) || !File.Exists(Path.Combine(assets, "efl.esm.js")))
        {
            RaiseError($"The modeler bundle is missing in {assets}. Was 'npm run build' run before packing?");
            return;
        }

        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

        _onMessage = (_, e) => OnMessage(e.WebMessageAsJson);
        _onNavigationStarting = (_, _) => _ready = false;
        _onNavigationCompleted = (_, e) =>
        {
            if (e.IsSuccess) return;
            _ready = false;
            RaiseError("Navigation failed: " + e.WebErrorStatus);
        };
        _onProcessFailed = (_, e) =>
        {
            _ready = false;
            RaiseError($"The WebView2 process failed ({e.ProcessFailedKind}).");
            // A renderer can be brought back by a reload; the Ready handler then
            // restores the diagram, unsaved edits included.
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                _view.Dispatcher.BeginInvoke(() => core.Reload());
            }
        };

        core.WebMessageReceived += _onMessage;
        core.NavigationStarting += _onNavigationStarting;
        core.NavigationCompleted += _onNavigationCompleted;
        core.ProcessFailed += _onProcessFailed;

        _navigationStarted = true;
        core.Navigate($"https://{VirtualHost}/index.html");
    }

    /// <summary>
    /// Shows a diagram. Returns false when it was only buffered because the page
    /// is not ready yet; it goes out when the page announces itself.
    /// </summary>
    public bool ImportModel(string json)
    {
        if (_disposed || string.IsNullOrEmpty(json)) return false;
        if (!_ready)
        {
            _pendingModel = json;
            return false;
        }
        if (_inReadyHandler) _importsDuringReady++;
        Post(new { type = MessageType.ImportModel, model = json });
        return true;
    }

    public void SelectElement(string id)
    {
        if (_disposed || !_ready || string.IsNullOrEmpty(id)) return;
        Post(new { type = MessageType.SelectElement, id });
    }

    public void SetTheme(string theme)
    {
        if (_disposed) return;
        if (!_ready)
        {
            _pendingTheme = theme;
            return;
        }
        Post(new { type = MessageType.SetTheme, theme });
    }

    /// <summary>
    /// Asks the page for an SVG picture. A request with an answer, so it is
    /// awaited; a timeout keeps a dead page from hanging the caller.
    /// </summary>
    public async Task<string?> RequestSvgAsync(TimeSpan timeout)
    {
        if (_disposed || !_ready) return null;

        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _svgRequest = answer;
        try
        {
            Post(new { type = MessageType.RequestSvg });
            var first = await Task.WhenAny(answer.Task, Task.Delay(timeout));
            return first == answer.Task ? await answer.Task : null;
        }
        finally
        {
            _svgRequest = null;
        }
    }

    private void Post(object message) =>
        _view.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    private void OnMessage(string json)
    {
        if (_disposed) return;

        PageMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<PageMessage>(json);
        }
        catch (JsonException ex)
        {
            RaiseError("Unreadable message from the page: " + ex.Message);
            return;
        }
        if (message == null) return;

        switch (message.Type)
        {
            case MessageType.Ready:
                _ready = true;
                Info?.Invoke("Modeler ready at " + message.Url);

                // The host's handler runs first: it knows whether there are
                // unsaved edits to bring back. A diagram buffered before the boot
                // goes out only if the handler sent nothing, because the buffer is
                // older than whatever the handler decided just now. Flushing it
                // after the handler's push overwrote fresh edits in AMLFPB.js.
                var buffered = _pendingModel;
                _pendingModel = null;
                _importsDuringReady = 0;
                _inReadyHandler = true;
                try
                {
                    Ready?.Invoke();
                }
                finally
                {
                    _inReadyHandler = false;
                }
                if (buffered != null && _importsDuringReady == 0) ImportModel(buffered);

                if (_pendingTheme is { } theme)
                {
                    _pendingTheme = null;
                    SetTheme(theme);
                }
                break;

            case MessageType.Imported:
                Imported?.Invoke(message.Model, message.Warnings ?? new List<string>());
                break;

            case MessageType.Changed:
                if (!string.IsNullOrEmpty(message.Model)) Changed?.Invoke(message.Model!);
                break;

            case MessageType.Svg:
                _svgRequest?.TrySetResult(message.Svg);
                break;

            case MessageType.Log:
                PageLog?.Invoke(message.Level ?? "log", message.Message ?? "");
                break;

            case MessageType.Error:
                RaiseError(message.Message ?? "(no message)");
                break;

            default:
                RaiseError($"Unknown message type from the page: '{message.Type}'.");
                break;
        }
    }

    private void RaiseError(string message)
    {
        if (_view.Dispatcher.CheckAccess()) Error?.Invoke(message);
        else _view.Dispatcher.Invoke(() => Error?.Invoke(message));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ready = false;
        _pendingModel = null;

        try
        {
            var core = _view.CoreWebView2;
            if (core != null)
            {
                if (_onMessage != null) core.WebMessageReceived -= _onMessage;
                if (_onNavigationStarting != null) core.NavigationStarting -= _onNavigationStarting;
                if (_onNavigationCompleted != null) core.NavigationCompleted -= _onNavigationCompleted;
                if (_onProcessFailed != null) core.ProcessFailed -= _onProcessFailed;
            }
        }
        catch
        {
            // The core may already be gone; there is nothing left to unsubscribe.
        }

        _svgRequest?.TrySetResult(null);
    }
}
