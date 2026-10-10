using System.Text.Json;
using MarkdownBlaze.Models;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;

namespace MarkdownBlaze.Tests;

/// <summary>The reader's settings, kept in the app-data folder (here: a scratch folder).</summary>
public sealed class PreferencesStoreTests : IDisposable
{
    private readonly TempFolder _data = new();

    public void Dispose() => _data.Dispose();

    private string FilePath => Path.Combine(_data.Path, "preferences.json");

    [Fact]
    public void A_first_run_gets_the_defaults()
    {
        using var store = Store();
        var current = store.Current;

        Assert.True(current.SidebarPinned);
        Assert.Null(current.SidebarWidth);
        Assert.False(current.TourSeen);
        Assert.Equal(0, current.DocumentsOpened);
        Assert.Equal(ThemeMode.System, current.ThemeMode); // a new install follows the system, as Settings and the app's fallback imply
        Assert.Equal(ContentThemes.Auto, current.ContentTheme);
        Assert.Equal(ViewMode.Continuous, current.ViewMode);
        Assert.Equal(PageFit.Width, current.PageFit);
        Assert.Equal(1, current.DocumentZoom);
        Assert.True(current.WindowMaximized);
        Assert.Equal(1280, current.WindowWidth);
        Assert.Equal(860, current.WindowHeight);
        Assert.Null(current.WindowX);
        Assert.Null(current.WindowY);
    }

    [Fact]
    public void Nothing_is_written_until_the_preferences_are_saved()
    {
        using var store = Store();

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Saved_preferences_come_back_after_a_restart()
    {
        using (var store = Store())
        {
            store.Current.SidebarPinned = false;
            store.Current.SidebarWidth = 312.5;
            store.Current.ThemeMode = ThemeMode.Light;
            store.Current.ContentTheme = "book";
            store.Current.ViewMode = ViewMode.Page;
            store.Current.PageFit = PageFit.Page;
            store.Current.DocumentZoom = 1.25;
            store.Current.WindowMaximized = false;
            store.Current.WindowX = -1200;
            store.Current.WindowY = 40;
            store.Current.DocumentsOpened = 7;
            store.Save();
            store.Flush();
        }

        using var again = Store();
        var current = again.Current;

        Assert.False(current.SidebarPinned);
        Assert.Equal(312.5, current.SidebarWidth);
        Assert.Equal(ThemeMode.Light, current.ThemeMode);
        Assert.Equal("book", current.ContentTheme);
        Assert.Equal(ViewMode.Page, current.ViewMode);
        Assert.Equal(PageFit.Page, current.PageFit);
        Assert.Equal(1.25, current.DocumentZoom);
        Assert.False(current.WindowMaximized);
        Assert.Equal(-1200, current.WindowX);
        Assert.Equal(40, current.WindowY);
        Assert.Equal(7, current.DocumentsOpened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json")]
    [InlineData("{\"SidebarPinned\": ")]
    [InlineData(/*lang=json,strict*/ "{\"SidebarPinned\": \"yes\"}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    public void A_preferences_file_that_cannot_be_read_gives_the_defaults_and_is_kept_as_corrupt(string content)
    {
        File.WriteAllText(FilePath, content);
        var log = new TestLogger();

        using var store = Store(log);
        var current = store.Current;

        Assert.True(current.SidebarPinned);
        Assert.Equal(1280, current.WindowWidth);
        Assert.Equal(ContentThemes.Auto, current.ContentTheme);
        // Not thrown away: kept beside the file for diagnosis, and the log says where.
        Assert.False(File.Exists(FilePath));
        Assert.Equal(content, File.ReadAllText(FilePath + ".corrupt"));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(FilePath, StringComparison.Ordinal));
    }

    [Fact]
    public void A_damaged_file_is_replaced_by_the_next_save()
    {
        File.WriteAllText(FilePath, "this is not json");
        using (var store = Store())
        {
            store.Current.DocumentsOpened = 3;
            store.Save();
            store.Flush();
        }

        using var again = Store();
        Assert.Equal(3, again.Current.DocumentsOpened);
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("this is not json", File.ReadAllText(FilePath + ".corrupt"));
    }

    // ---- the enums and the strings they are kept as --------------------------------------------------------

    /// <summary>
    /// A file as the version before the enums wrote it: the same property names, the strings the old
    /// code held. It has to read as it did.
    /// </summary>
    [Fact]
    public void A_file_written_before_the_enums_still_reads()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "preferences", "written-before-the-enums.json"), FilePath);

        using var store = Store();
        var current = store.Current;

        Assert.Equal(ThemeMode.Dark, current.ThemeMode);
        Assert.Equal("book", current.ContentTheme);
        Assert.Equal(ViewMode.Page, current.ViewMode);
        Assert.Equal(PageFit.Page, current.PageFit);
        Assert.False(current.SidebarPinned);
        Assert.Equal(12, current.DocumentsOpened);
        Assert.Equal(30, current.WindowY);
        Assert.False(File.Exists(FilePath + ".corrupt"));
    }

    /// <summary>The other way round: what is written now has to be readable by the version before it.</summary>
    [Fact]
    public void The_enums_are_written_as_the_strings_the_file_has_always_held()
    {
        using (var store = Store())
        {
            store.Current.ThemeMode = ThemeMode.Dark;
            store.Current.ViewMode = ViewMode.Page;
            store.Current.PageFit = PageFit.Width;
            store.Current.ContentTheme = "academic";
            store.Save();
            store.Flush();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = document.RootElement;
        Assert.Equal("Dark", root.GetProperty("ThemeMode").GetString());
        Assert.Equal("page", root.GetProperty("ViewMode").GetString());
        Assert.Equal("width", root.GetProperty("PageFit").GetString());
        Assert.Equal("academic", root.GetProperty("ContentTheme").GetString());
    }

    [Theory]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("LIGHT", ThemeMode.Light)]
    [InlineData("System", ThemeMode.System)]
    [InlineData("Sepia", ThemeMode.System)] // not a theme: the default, not a damaged file
    [InlineData("1", ThemeMode.System)]
    [InlineData("", ThemeMode.System)]
    public void The_app_theme_is_read_by_name_and_an_unknown_one_is_the_default(string written, ThemeMode expected)
    {
        File.WriteAllText(FilePath, $$"""{"ThemeMode": "{{written}}", "DocumentZoom": 1.5}""");

        using var store = Store();

        Assert.Equal(expected, store.Current.ThemeMode);
        Assert.Equal(1.5, store.Current.DocumentZoom); // the rest of the file was not lost with it
        Assert.False(File.Exists(FilePath + ".corrupt"));
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{"ViewMode": "Page", "PageFit": "PAGE"}""", "Page", "Page")]
    [InlineData(/*lang=json,strict*/ """{"ViewMode": "pages", "PageFit": "wide"}""", "Continuous", "Width")]
    [InlineData(/*lang=json,strict*/ """{"ViewMode": 1, "PageFit": 1}""", "Continuous", "Width")]
    [InlineData(/*lang=json,strict*/ """{"ViewMode": null, "PageFit": {"a": [1, 2]}, "DocumentZoom": 2}""", "Continuous", "Width")]
    [InlineData(/*lang=json,strict*/ """{"ViewMode": true, "PageFit": []}""", "Continuous", "Width")]
    public void The_view_is_read_by_name_and_anything_else_is_the_default(string json, string mode, string fit)
    {
        File.WriteAllText(FilePath, json);

        using var store = Store();

        Assert.Equal(Enum.Parse<ViewMode>(mode), store.Current.ViewMode);
        Assert.Equal(Enum.Parse<PageFit>(fit), store.Current.PageFit);
        Assert.False(File.Exists(FilePath + ".corrupt"));
    }

    [Theory]
    [InlineData("book", "book")]
    [InlineData("night", "night")]
    [InlineData("auto", "auto")]
    [InlineData("Book", "auto")] // ids are exact
    [InlineData("a-theme-from-the-future", "auto")]
    [InlineData("", "auto")]
    public void The_document_theme_is_checked_against_the_themes_there_are_when_the_file_is_loaded(string written, string expected)
    {
        File.WriteAllText(FilePath, $$"""{"ContentTheme": "{{written}}"}""");

        using var store = Store();

        Assert.Equal(expected, store.Current.ContentTheme);
    }

    [Fact]
    public void Settings_that_are_missing_or_unknown_do_not_matter()
    {
        File.WriteAllText(FilePath, /*lang=json,strict*/ "{\"DocumentZoom\": 2, \"SomethingFromAFutureVersion\": [1, 2]}");

        using var store = Store();
        var current = store.Current;

        Assert.Equal(2, current.DocumentZoom);
        Assert.True(current.SidebarPinned);
        Assert.False(File.Exists(FilePath + ".corrupt"));
    }

    [Fact]
    public void The_folder_is_created_when_it_does_not_exist_yet()
    {
        var nested = Path.Combine(_data.Path, "a", "b");

        using var store = new PreferencesStore(nested);
        store.Save();
        store.Flush();

        Assert.True(File.Exists(Path.Combine(nested, "preferences.json")));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_does_not_stop_the_app_and_is_logged()
    {
        var log = new TestLogger();
        using var store = Store(log);
        store.Current.DocumentZoom = 1.5;
        _ = Directory.CreateDirectory(FilePath); // a folder where the file should be: every write fails

        store.Save();
        store.Flush();

        Assert.Equal(1.5, store.Current.DocumentZoom);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    // ---- saving: atomic and debounced ---------------------------------------------------------------------

    [Fact]
    public void A_save_that_dies_half_way_leaves_the_previous_file_and_never_an_empty_one()
    {
        var log = new TestLogger();
        using var store = Store(log);
        store.Current.DocumentZoom = 2;
        store.Save();
        store.Flush();
        var good = File.ReadAllText(FilePath);

        // The new content goes to preferences.json.tmp first. With a directory standing where that file
        // belongs the write fails at once - as a power cut would, half-way - and the old file must still
        // be there, whole, rather than truncated by a write in place.
        _ = Directory.CreateDirectory(FilePath + ".tmp");
        store.Current.DocumentZoom = 3;
        store.Save();
        store.Flush();

        Assert.Equal(good, File.ReadAllText(FilePath));
        Assert.NotEqual(0, new FileInfo(FilePath).Length);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);

        // and when the obstacle is gone, the next save goes through and leaves no temporary file behind
        Directory.Delete(FilePath + ".tmp");
        store.Save();
        store.Flush();
        Assert.Contains("\"DocumentZoom\": 3", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Wheel_spam_is_one_write()
    {
        using var store = new PreferencesStore(_data.Path, debounce: TimeSpan.FromMilliseconds(200));

        for (var i = 1; i <= 60; i++)
        {
            store.Current.DocumentZoom = 1 + (i / 100.0); // a zoom tick
            store.Save();
        }

        Assert.Equal(0, store.WriteCount); // not on every tick
        _ = SpinWait.SpinUntil(() => store.WriteCount > 0, TimeSpan.FromSeconds(10));
        Thread.Sleep(500);

        Assert.Equal(1, store.WriteCount);
        Assert.Contains("1.6", File.ReadAllText(FilePath), StringComparison.Ordinal); // and it is the last value
    }

    [Fact]
    public void Flushing_writes_at_once_and_only_when_there_is_something_to_write()
    {
        using var store = new PreferencesStore(_data.Path, debounce: TimeSpan.FromMinutes(5));

        store.Flush();
        Assert.Equal(0, store.WriteCount);

        store.Save();
        Assert.False(File.Exists(FilePath));
        store.Flush();
        Assert.Equal(1, store.WriteCount);
        Assert.True(File.Exists(FilePath));

        store.Flush(); // nothing new
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public void Disposing_the_store_writes_what_was_still_waiting()
    {
        using (var store = new PreferencesStore(_data.Path, debounce: TimeSpan.FromMinutes(5)))
        {
            store.Current.DocumentsOpened = 9;
            store.Save();
            Assert.False(File.Exists(FilePath));
        }

        using var again = Store();
        Assert.Equal(9, again.Current.DocumentsOpened);
    }

    private PreferencesStore Store(ILogger? log = null) => new(_data.Path, log);
}
