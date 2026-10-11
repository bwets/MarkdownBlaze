using MarkdownBlaze.Components;
using MarkdownBlaze.Components.Settings;
using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Platform.Windows;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Photino.Blazor;
using Photino.NET;

namespace MarkdownBlaze;

internal static partial class Program
{
    private static readonly ILogger Logger = Log.For(nameof(Program));

    private const int ExitRegistryRefused = 1;
    private const int ExitNotApplicable = 2;

    /// <summary>Chromium switches for WebView2: the disk cache is limited to one byte.</summary>
    internal const string WebViewBrowserArguments = "--disk-cache-size=1";

    /// <summary>
    /// WebView2 on Windows requires an STA thread; top-level statements run as MTA,
    /// which leaves the WebView uninitialized (blank/black window).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (StartupArguments.RegistrationRequestOf(args) is { } register)
            return ChangeRegistration(register, StartupArguments.LogLevelOf(args));

        var builder = PhotinoBlazorApp.CreateBuilder(args);

        // Serve the UI from markdown:// instead of PhotinoX's default app://localhost. The scheme is
        // the app's own, and the WebView's address is what the browser prints in the page footer —
        // so a printed sheet says markdown://document/<file> rather than a meaningless app://localhost.
        _ = builder.ConfigureBlazor(options => options.AppBaseUri = UriScheme.AppBase);
        var assets = new AssetScope();
        var shell = new ShellLauncher();
        _ = builder.ConfigureMainWindow(window => ConfigureWebView(window, IsDevToolsWanted(args), assets, shell));

        var logLevel = StartupArguments.LogLevelOf(args);
        ConfigureServices(builder.Services, args, logLevel, assets, shell);
        builder.RootComponents.Add<App>("app");

        // PhotinoX owns disposal: Run() returns after the message loop exits without disposing, so the
        // app is disposed here (which also tears down the Blazor windows and the service provider).
        using var app = builder.Build();

        // From here on there is a log: static helpers reach it through Log, and the failures nobody caught
        // end up in it instead of on a console that a WinExe does not have.
        Log.Attach(app.Services.GetRequiredService<ILoggerFactory>());
        HookUnhandledExceptions();
        LogStarted(Logger, MarkdownService.AppVersion ?? "dev build", Environment.OSVersion.VersionString, logLevel);

        _ = app.MainWindow.SetTitle("MarkdownBlaze");

        // Restore the window's saved size/position/maximized state (defaults to maximized on first run).
        app.Services.GetRequiredService<WindowStateService>().Attach(app.MainWindow);

        SetWindowIcon(app.MainWindow);

        // Expose the window so components can use native dialogs (e.g. the "open file" button).
        app.Services.GetRequiredService<NativeDialogs>().Attach(app.MainWindow);

        // Nothing is registered with Windows from here. The installer does that for an installed copy,
        // and a portable one does it when the reader asks (Settings, or --register) — see ShellIntegration.
        return app.Run();
    }

    /// <summary>
    /// <c>--register</c> and <c>--unregister</c>: do the Windows registration's work and leave without
    /// opening a window, for scripts and for someone who unzipped the app and wants it gone again.
    /// Exit codes: 0 done, 1 the registry refused something (see the log), 2 not applicable — not
    /// Windows, or the Microsoft Store build, whose manifest owns the registration and whose registry
    /// writes would land in the package's private hive.
    /// </summary>
    private static int ChangeRegistration(bool register, LogLevel logLevel)
    {
        if (!OperatingSystem.IsWindows() || PackageIdentity.IsPackaged) return ExitNotApplicable;

        // The one place the app runs without the container: it only needs the log, which a failure
        // is worth writing to since no window is there to say so.
        using var loggers = LoggerFactory.Create(logging =>
        {
            _ = logging.AddProvider(new FileLoggerProvider(AppPaths.LogFolder));
            _ = logging.SetMinimumLevel(logLevel);
        });
        Log.Attach(loggers);

        var shell = new ShellIntegration();
        return (register ? shell.RegisterAll() : shell.UnregisterAll()) ? 0 : ExitRegistryRefused;
    }

    /// <summary>What the browser engine inside the window is, and is not, allowed to do.</summary>
    private static void ConfigureWebView(PhotinoWindow window, bool devTools, AssetScope assets, ShellLauncher shell)
    {
        // The WebView's own zoom scales the whole window — toolbar, sidebar and all — which is not
        // what Ctrl+wheel should do in a document viewer. It is turned off here (a browser setting,
        // so it has to be applied before the WebView is created) and the page zooms the document (js/zoom.js)
        // instead. The window itself is not zoomable at all.
        _ = window.SetZoomEnabled(enabled: false);

        // The page shows documents nobody vetted, so the browser engine is kept to the one thing it is
        // here for. Developer tools exist in debug builds, or with --devtools (F12 / Ctrl+Shift+I); a
        // released build has none. The engine's own context menu (Reload, View source, Inspect) is
        // never shown — the app has menus of its own. Nothing the page asks the browser for —
        // camera, location, notifications — is granted.
        _ = window.SetDevToolsEnabled(devTools);
        _ = window.SetContextMenuEnabled(enabled: false);
        _ = window.SetGrantBrowserPermissions(grant: false);

        // The same-origin policy and the rest of the engine's web security stay on. PhotinoX has them on
        // by default; it is said here so that nobody has to wonder, and so that a newer PhotinoX that
        // changed the default (the setting exists for apps that load their own local pages) cannot turn
        // the protection off underneath a window that shows documents from anywhere.
        _ = window.SetWebSecurityEnabled(enable: true);

        // Windows: the engine's own folder (cache, cookies, storage) belongs to the app, not to the
        // "Photino" folder every PhotinoX program on the machine shares — see AppPaths.WebViewFolder and
        // PRIVACY.md. The disk cache is kept to the least the engine accepts: the pictures of a document
        // that come from the web are fetched when it is opened and are not worth keeping copies of.
        if (OperatingSystem.IsWindows())
        {
            _ = window.SetUserDataFolder(AppPaths.WebViewFolder);
            _ = window.SetBrowserControlInitParameters(WebViewBrowserArguments);
        }

        // The window may only ever show the app. A script or a stray link that tries to take it
        // somewhere else — a web page inside the app's own frame — is stopped here.
        _ = window.RegisterNavigationStartingHandler((_, e) =>
        {
            if (!IsAppAddress(e.Uri)) e.Cancel = true;
        });

        // target="_blank" and window.open: no extra windows. A web address goes to the browser,
        // and only if ExternalLinks allows it; anything else is dropped.
        _ = window.RegisterNewWindowRequestedHandler((_, e) =>
        {
            if (ExternalLinks.TryGetOpenable(e.Uri.AbsoluteUri, out var uri)) _ = shell.OpenExternal(uri);
        });

        // The pictures of the open document are not carried in the page: its <img>s point at
        // mdasset://doc/<token>/<path> and the WebView asks this handler for the bytes. A second custom
        // scheme beside markdown:// (the UI's own), registered the same way; what it will serve is
        // decided by AssetHandler, with the rules of LocalPath, for the render AssetScope names.
        var assetHandler = new AssetHandler(assets);
        _ = window.RegisterCustomSchemeHandler(
            AssetHandler.Scheme,
            (_, _, url, out contentType) => assetHandler.Serve(url, out contentType));
    }

    private static void ConfigureServices(IServiceCollection services, string[] args, LogLevel logLevel, AssetScope assets, IShellLauncher shell)
    {
        _ = services.AddSingleton(StartupArguments.From(args));

        // One rolling file under %LOCALAPPDATA%\MarkdownBlaze\logs (see FileLoggerProvider). --log-level
        // picks how much goes in; Information by default, so a failed best-effort operation (always a
        // warning) is there to be found when someone reports that "it just does nothing".
        _ = services.AddLogging(logging =>
        {
            _ = logging.ClearProviders();
            _ = logging.AddProvider(new FileLoggerProvider(AppPaths.LogFolder));
            _ = logging.SetMinimumLevel(logLevel);
        });
        _ = services.AddFluentUIComponents();

        _ = services.AddSingleton(assets);
        _ = services.AddSingleton(shell);
        _ = services.AddSingleton<PreferencesStore>();
        _ = services.AddSingleton<HistoryStore>();
        _ = services.AddSingleton<MarkdownService>();
        _ = services.AddSingleton<IDocumentRenderer>(provider => provider.GetRequiredService<MarkdownService>());
        _ = services.AddSingleton<NavigationService>();
        _ = services.AddSingleton<FileTreeService>();
        _ = services.AddSingleton<NativeDialogs>();
        if (OperatingSystem.IsWindows()) _ = services.AddSingleton<ShellIntegration>();
        _ = services.AddSingleton<ShellIntegrationActions>();
        _ = services.AddSingleton<WindowStateService>();

        // The viewer's own parts: one set per window. ViewerInterop owns the connection to the page's
        // JavaScript (it needs the window's IJSRuntime, which is scoped), and the rest build on it.
        _ = services.AddScoped<ViewerInterop>();
        _ = services.AddScoped<ViewerState>();
        _ = services.AddScoped<ViewerActions>();
        _ = services.AddScoped<ViewerMenus>();
        _ = services.AddScoped<Commands>();
        _ = services.AddScoped<PageEvents>();
    }

    /// <summary>
    /// Set the window icon. On Windows, push the .ico onto the HWND via WM_SETICON once the native
    /// window exists (Photino's SetIconFile only sets it there); other platforms use SetIconFile.
    /// </summary>
    private static void SetWindowIcon(PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows())
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(ico))
                _ = window.RegisterCreatedHandler((_, _) => WindowIcon.Apply(window.WindowHandle, ico));
        }
        else
        {
            var png = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            if (!File.Exists(png)) return;

            try
            {
                _ = window.SetIconFile(png);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
            {
                // Best-effort: the window keeps the default icon.
                LogIconNotSet(Logger, ex);
            }
        }
    }

    private static bool IsDevToolsWanted(string[] args) =>
        IsDebugBuild() || StartupArguments.DevToolsRequestedBy(args);

    /// <summary>
    /// Sends what nobody caught to the log. A WinExe has no console, so the <c>Console.Error</c> line that
    /// used to be here went nowhere: a crash left no trace at all.
    /// </summary>
    private static void HookUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
            LogUnhandled(Logger, ex, e.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnobserved(Logger, e.Exception);
            e.SetObserved();
        };
    }

    private static bool IsDebugBuild() =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Whether <paramref name="address"/> is the app's own origin — the only place the window may be.</summary>
    private static bool IsAppAddress(Uri address) =>
        string.Equals(address.Scheme, UriScheme.AppBase.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(address.Host, UriScheme.AppBase.Host, StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Information, Message = "MarkdownBlaze {Version} started on {Os}; log level {Level}")]
    private static partial void LogStarted(ILogger log, string version, string os, LogLevel level);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {Terminating})")]
    private static partial void LogUnhandled(ILogger log, Exception ex, bool terminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "A task failed and nobody was waiting for it")]
    private static partial void LogUnobserved(ILogger log, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not set the window icon")]
    private static partial void LogIconNotSet(ILogger log, Exception ex);
}
