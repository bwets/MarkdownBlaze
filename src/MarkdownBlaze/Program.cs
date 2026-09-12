using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Photino.Blazor;
using MarkdownBlaze;
using MarkdownBlaze.Services;

internal static class Program
{
    // WebView2 on Windows requires an STA thread; top-level statements run as MTA,
    // which leaves the WebView uninitialized (blank/black window).
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = PhotinoBlazorApp.CreateBuilder(args);

        // Serve the UI from markdown:// instead of PhotinoX's default app://localhost. The scheme is
        // the app's own, and the WebView's address is what the browser prints in the page footer —
        // so a printed sheet says markdown://document/<file> rather than a meaningless app://localhost.
        builder.ConfigureBlazor(options => options.AppBaseUri = UriScheme.AppBase);

        // The WebView's own zoom scales the whole window — toolbar, sidebar and all — which is not
        // what Ctrl+wheel should do in a document viewer. It is turned off here (a browser setting,
        // so it has to be applied before the WebView is created) and app.js zooms the document
        // instead. The window itself is not zoomable at all.
        builder.ConfigureMainWindow(window => window.SetZoomEnabled(false));

        builder.Services.AddLogging();
        builder.Services.AddFluentUIComponents();

        builder.Services.AddSingleton<PreferencesStore>();
        builder.Services.AddSingleton<HistoryStore>();
        builder.Services.AddSingleton<MarkdownService>();
        builder.Services.AddSingleton<NavigationService>();
        builder.Services.AddSingleton<FileTreeService>();
        builder.Services.AddSingleton<WindowHost>();
        builder.Services.AddSingleton<ShellIntegration>();
        builder.Services.AddSingleton<WindowStateService>();

        builder.RootComponents.Add<App>("app");

        // PhotinoX owns disposal: Run() returns after the message loop exits without disposing, so the
        // app is disposed here (which also tears down the Blazor windows and the service provider).
        using var app = builder.Build();

        app.MainWindow.SetTitle("MarkdownBlaze");

        // Restore the window's saved size/position/maximized state (defaults to maximized on first run).
        app.Services.GetRequiredService<WindowStateService>().Attach(app.MainWindow);

        // Set the window icon. On Windows, push the .ico onto the HWND via WM_SETICON once the native
        // window exists (Photino's SetIconFile only sets it there); other platforms use SetIconFile.
        if (OperatingSystem.IsWindows())
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(ico))
                app.MainWindow.RegisterCreatedHandler((_, _) => NativeIcon.Apply(app.MainWindow.WindowHandle, ico));
        }
        else
        {
            var png = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            if (File.Exists(png))
                try { app.MainWindow.SetIconFile(png); } catch { /* best-effort */ }
        }

        // Expose the window so components can use native dialogs (e.g. the "open file" button).
        app.Services.GetRequiredService<WindowHost>().Window = app.MainWindow;

        // Explorer's "Open with MarkdownBlaze" and the markdown:// handler are part of the app being
        // installed, so they are put in place on every launch rather than offered as a setting. Off
        // the startup path: the registry work is a few short-lived processes and nothing waits on it.
        var shell = app.Services.GetRequiredService<ShellIntegration>();
        _ = Task.Run(shell.EnsureRegistered);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine("Unhandled exception: " + e.ExceptionObject);

        return app.Run();
    }
}
