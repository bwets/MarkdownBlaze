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
    private static void Main(string[] args)
    {
        Diag.Reset();
        Diag.Log($"start; args=[{string.Join(", ", args)}]");

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        builder.Services.AddLogging();
        builder.Services.AddFluentUIComponents();

        builder.Services.AddSingleton<PreferencesStore>();
        builder.Services.AddSingleton<HistoryStore>();
        builder.Services.AddSingleton<MarkdownService>();
        builder.Services.AddSingleton<NavigationService>();

        builder.RootComponents.Add<App>("app");

        var app = builder.Build();

        app.MainWindow
            .SetTitle("MarkdownBlaze")
            .SetSize(1280, 860);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine("Unhandled exception: " + e.ExceptionObject);

        Diag.Log("window configured; about to Run()");
        app.Run();
    }
}
