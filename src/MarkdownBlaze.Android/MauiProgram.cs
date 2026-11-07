using MarkdownBlaze.Components.Settings;
using MarkdownBlaze.Components.Viewer;
using MarkdownBlaze.Interop;
using MarkdownBlaze.Platform;
using MarkdownBlaze.Security;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;

namespace MarkdownBlaze;

/// <summary>
/// The Android host's start-up: the counterpart of the desktop host's <c>Program</c>. It names the app's folders,
/// registers what the components in MarkdownBlaze.Core need (the same services as the desktop, and the Android
/// answers to the seams in Core's Platform folder) and starts the MAUI application, whose one page is the
/// <c>BlazorWebView</c> (<see cref="MainPage"/>).
/// </summary>
internal static partial class MauiProgram
{
    private static readonly ILogger Logger = Log.For(nameof(MauiProgram));

    /// <summary>Builds the MAUI app; called once, by <see cref="MainApplication"/>.</summary>
    public static MauiApp CreateMauiApp()
    {
        // Before anything reads them: the preferences, the history and the log live under the app's own
        // folder, and the manual and the change log are copies of the APK's assets in it.
        AppPaths.Configure(FileSystem.AppDataDirectory);
        BundledDocuments.CopyTo(Android.App.Application.Context, Path.Combine(AppPaths.DataFolder, "bundled"));
        AppPaths.ConfigureBundledFolder(Path.Combine(AppPaths.DataFolder, "bundled"));

        var builder = MauiApp.CreateBuilder();
        _ = builder.UseMauiApp(services => new MarkdownBlazeApp(services.GetRequiredService<WebViewPolicy>(), services.GetRequiredService<AndroidPrinter>()));
        _ = builder.Services.AddMauiBlazorWebView();
        _ = builder.Services.AddSingleton(new HostInfo(".NET MAUI"));
#if DEBUG
        // The WebView's remote debugging (chrome://inspect, and the DevTools socket the checks of the Android
        // port use). A released build has none.
        _ = builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        ConfigureServices(builder.Services);

        var app = builder.Build();

        // From here on there is a log: static helpers reach it through Log, and the failures nobody caught
        // end up in it.
        Log.Attach(app.Services.GetRequiredService<ILoggerFactory>());

        // Mirrors of folders the reader has since taken the permission back for, and single files nothing points at.
        app.Services.GetRequiredService<AndroidStorage>().CleanUp(app.Services.GetRequiredService<HistoryStore>().Entries.Select(entry => entry.Path));
        HookUnhandledExceptions();
        LogStarted(Logger, MarkdownService.AppVersion ?? "dev build", Environment.OSVersion.VersionString);
        return app;
    }

    /// <summary>The services the components need; the desktop host's <c>ConfigureServices</c>, with the Android seams.</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // What the debug build was asked to open (see MainActivity); nothing for a released one, which is
        // started by the launcher and shows the manual.
        _ = services.AddSingleton(_ => StartupArguments.From(DebugStartup.Arguments));

        // One rolling file under the app's folder (see FileLoggerProvider).
        _ = services.AddLogging(logging =>
        {
            _ = logging.ClearProviders();
            _ = logging.AddProvider(new FileLoggerProvider(AppPaths.LogFolder));
            _ = logging.SetMinimumLevel(DebugStartup.IsDebugBuild ? LogLevel.Debug : StartupArguments.DefaultLogLevel);
        });
        _ = services.AddFluentUIComponents();

        _ = services.AddSingleton<AssetScope>();
        _ = services.AddSingleton<IShellLauncher>(_ => new AndroidShellLauncher());
        _ = services.AddSingleton<PreferencesStore>();
        _ = services.AddSingleton<HistoryStore>();
        _ = services.AddSingleton<MarkdownService>();
        _ = services.AddSingleton<IDocumentRenderer>(provider => provider.GetRequiredService<MarkdownService>());
        _ = services.AddSingleton<NavigationService>();
        _ = services.AddSingleton<FileTreeService>();
        _ = services.AddSingleton(AndroidAddresses.Addresses);

        // What differs from one host to another (see MarkdownBlaze.Core's Platform folder). Settings leaves its shell integration row
        // out on Android, and the policy of the WebView serves the pictures, through the same AssetHandler as on the desktop.
        _ = services.AddSingleton<Platform.IFilePicker>(provider => new AndroidFilePicker(
            provider.GetRequiredService<AndroidStorage>(),
            provider.GetRequiredService<DocumentOpener>()));
        _ = services.AddSingleton(_ => new AndroidPrinter());
        _ = services.AddSingleton<IPrinter>(provider => provider.GetRequiredService<AndroidPrinter>());
        _ = services.AddSingleton<IWindowControl>(_ => new AndroidWindowControl());
        _ = services.AddSingleton<ShellIntegrationActions>();
        _ = services.AddSingleton<AssetHandler>();

        // Folders the reader grants one by one, the copy the rest of the app reads them from, and the way documents
        // handed to the app by intents reach the viewer (see AndroidStorage, DocumentOpener and HostInbox).
        _ = services.AddSingleton<HostInbox>();
        _ = services.AddSingleton<HostWindow>();
        _ = services.AddSingleton(provider => new AndroidStorage(Android.App.Application.Context, provider.GetRequiredService<HostInbox>()));
        _ = services.AddSingleton<IFolderAccess>(provider => provider.GetRequiredService<AndroidStorage>());
        _ = services.AddSingleton(provider => new DocumentOpener(
            Android.App.Application.Context,
            provider.GetRequiredService<AndroidStorage>(),
            provider.GetRequiredService<HostInbox>()));
        _ = services.AddSingleton(provider => new IntentRouter(
            provider.GetRequiredService<DocumentOpener>(),
            provider.GetRequiredService<HostInbox>()));
        _ = services.AddSingleton(provider => new WebViewPolicy(
            provider.GetRequiredService<HostAddresses>(),
            provider.GetRequiredService<AssetHandler>(),
            provider.GetRequiredService<IShellLauncher>()));

        // The viewer's own parts: one set per window (see the desktop host).
        _ = services.AddScoped<ViewerInterop>();
        _ = services.AddScoped<ViewerState>();
        _ = services.AddScoped<ViewerActions>();
        _ = services.AddScoped<ViewerMenus>();
        _ = services.AddScoped<Commands>();
        _ = services.AddScoped<PageEvents>();
    }

    /// <summary>Sends what nobody caught to the log, as the desktop host does.</summary>
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

    [LoggerMessage(Level = LogLevel.Information, Message = "MarkdownBlaze {Version} started on {Os}")]
    private static partial void LogStarted(ILogger log, string version, string os);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {Terminating})")]
    private static partial void LogUnhandled(ILogger log, Exception ex, bool terminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "A task failed and nobody was waiting for it")]
    private static partial void LogUnobserved(ILogger log, Exception ex);
}
