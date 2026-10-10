using System.Globalization;
using System.Text.Json;
using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>The list of documents the reader has opened, kept in the app-data folder (here: a scratch folder).</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly TempFolder _documents = new();

    public void Dispose()
    {
        _data.Dispose();
        _documents.Dispose();
    }

    private string FilePath => Path.Combine(_data.Path, "history.json");

    [Fact]
    public void The_most_recently_opened_document_comes_first()
    {
        using var store = Store();
        var one = _documents.File("one.md", "# 1");
        var two = _documents.File("two.md", "# 2");
        var three = _documents.File("three.md", "# 3");

        store.Record(one, "One");
        store.Record(two, "Two");
        store.Record(three, "Three");

        Assert.Equal([three, two, one], Paths(store));
        Assert.Equal(["Three", "Two", "One"], store.Entries.Select(e => e.Title), StringComparer.Ordinal);
    }

    [Fact]
    public void Opening_a_document_again_keeps_its_place_and_refreshes_its_title_and_time()
    {
        using var store = Store();
        var one = _documents.File("one.md", "# 1");
        var two = _documents.File("two.md", "# 2");
        store.Record(one, "One");
        store.Record(two, "Two");
        var firstOpened = store.Entries.Single(e => PathIs(e.Path, one)).LastOpenedUtc;

        store.Record(one, "One, renamed");

        Assert.Equal([two, one], Paths(store)); // not moved to the top: the list does not reshuffle under the reader
        var entry = store.Entries.Single(e => PathIs(e.Path, one));
        Assert.Equal("One, renamed", entry.Title);
        Assert.True(entry.LastOpenedUtc >= firstOpened);
    }

    [Fact]
    public void A_path_is_recorded_in_full_and_compared_without_regard_to_case()
    {
        using var store = Store();
        var doc = _documents.File("Doc.md", "# d");

        store.Record(doc, "Doc");
        store.Record(doc.ToUpperInvariant(), "Doc again");

        _ = Assert.Single(store.Entries);
    }

    [Fact]
    public void A_missing_title_is_the_file_name_and_a_blank_path_is_ignored()
    {
        using var store = Store();
        var doc = _documents.File("my notes.md", "# n");

        store.Record(doc, "  ");
        store.Record("", "Nothing");
        store.Record("   ", "Nothing");

        var entry = Assert.Single(store.Entries);
        Assert.Equal("my notes", entry.Title);
    }

    [Fact]
    public void The_list_is_capped_and_the_oldest_entries_are_the_ones_dropped()
    {
        using var store = Store();

        for (var i = 0; i < 205; i++)
        {
            var path = Path.Combine(_documents.Path, string.Create(CultureInfo.InvariantCulture, $"doc{i:D3}.md"));
            store.Record(path, string.Create(CultureInfo.InvariantCulture, $"Doc {i}"));
        }

        Assert.Equal(200, store.Entries.Count);
        Assert.Equal("Doc 204", store.Entries[0].Title);
        Assert.Equal("Doc 5", store.Entries[^1].Title);
    }

    [Fact]
    public void An_entry_can_be_removed_and_the_whole_list_cleared_and_each_is_announced()
    {
        using var store = Store();
        var one = _documents.File("one.md", "# 1");
        var two = _documents.File("two.md", "# 2");
        store.Record(one, "One");
        store.Record(two, "Two");
        var announced = 0;
        store.Changed += (_, _) => announced++;

        store.Remove(one);
        Assert.Equal([two], Paths(store));
        Assert.Equal(1, announced);

        store.Remove(one); // already gone: nothing to announce
        store.Remove("");
        Assert.Equal(1, announced);

        store.Clear();
        Assert.Empty(store.Entries);
        Assert.Equal(2, announced);

        store.Clear();
        Assert.Equal(2, announced);
    }

    [Fact]
    public void The_list_survives_a_restart_in_the_same_order()
    {
        var one = _documents.File("one.md", "# 1");
        var two = _documents.File("two.md", "# 2");
        using (var first = Store())
        {
            first.Record(one, "One");
            first.Record(two, "Two");
            first.Flush();
        }

        using var second = Store();

        Assert.Equal([two, one], Paths(second));
        Assert.Equal(["Two", "One"], second.Entries.Select(e => e.Title), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not json")]
    [InlineData("[{\"Path\": ")]
    [InlineData(/*lang=json,strict*/ "{\"not\": \"a list\"}")]
    [InlineData("null")]
    public void A_history_file_that_cannot_be_read_is_an_empty_history_and_is_kept_as_corrupt(string content)
    {
        File.WriteAllText(FilePath, content);
        var log = new TestLogger();

        using (var store = Store(log))
        {
            Assert.Empty(store.Entries);
            store.Record(_documents.File("one.md", "# 1"), "One"); // and it is usable afterwards
            store.Flush();
        }

        using var again = Store();
        _ = Assert.Single(again.Entries);
        // The damaged file is not thrown away: it is there to be looked at, and the log says so.
        Assert.Equal(content, File.ReadAllText(FilePath + ".corrupt"));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(".corrupt", StringComparison.Ordinal));
    }

    [Fact]
    public void Entries_are_a_snapshot_that_later_changes_do_not_touch()
    {
        using var store = Store();
        store.Record(_documents.File("one.md", "# 1"), "One");
        var snapshot = store.Entries;

        store.Record(_documents.File("two.md", "# 2"), "Two");

        _ = Assert.Single(snapshot);
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public void Reading_the_entries_hands_out_the_same_snapshot_until_something_changes()
    {
        using var store = Store();
        store.Record(_documents.File("one.md", "# 1"), "One");

        Assert.Same(store.Entries, store.Entries); // no copy per read: a render asks twice

        var before = store.Entries;
        store.Record(_documents.File("two.md", "# 2"), "Two");
        Assert.NotSame(before, store.Entries);
    }

    [Fact]
    public void Pruning_drops_the_documents_that_are_gone_and_says_so()
    {
        var kept = _documents.File("kept.md", "# k");
        var gone = _documents.File("gone.md", "# g");
        using (var store = Store())
        {
            store.Record(kept, "Kept");
            store.Record(gone, "Gone");
            File.Delete(gone);
            using var announced = new ManualResetEventSlim();
            store.Changed += (_, _) => announced.Set();

            store.PruneMissingInBackground();

            Assert.True(announced.Wait(TimeSpan.FromSeconds(10)), "the prune never announced a change");
            Assert.Equal([kept], Paths(store));
            store.Flush();
        }

        using var again = Store();
        Assert.Equal([kept], Paths(again)); // and it was saved
    }

    [Fact]
    public void Pruning_a_history_with_nothing_missing_announces_nothing()
    {
        using var store = Store();
        store.Record(_documents.File("kept.md", "# k"), "Kept");
        using var announced = new ManualResetEventSlim();
        store.Changed += (_, _) => announced.Set();

        store.PruneMissingInBackground();

        Assert.False(announced.Wait(TimeSpan.FromMilliseconds(500)));
        _ = Assert.Single(store.Entries);
    }

    // ---- saving: atomic and debounced ---------------------------------------------------------------------

    [Fact]
    public void A_burst_of_changes_is_one_write_a_moment_after_the_last()
    {
        using var store = new HistoryStore(_data.Path, debounce: TimeSpan.FromMilliseconds(150));

        for (var i = 0; i < 20; i++)
            store.Record(Path.Combine(_documents.Path, string.Create(CultureInfo.InvariantCulture, $"doc{i}.md")), "Doc");

        Assert.False(File.Exists(FilePath)); // nothing yet
        _ = SpinWait.SpinUntil(() => store.WriteCount > 0, TimeSpan.FromSeconds(10));
        Thread.Sleep(400); // and no second write follows

        Assert.Equal(1, store.WriteCount);
        Assert.Equal(20, ArrayLength());
    }

    [Fact]
    public void Disposing_the_store_writes_what_was_still_waiting()
    {
        var doc = _documents.File("one.md", "# 1");
        using (var store = new HistoryStore(_data.Path, debounce: TimeSpan.FromMinutes(5)))
        {
            store.Record(doc, "One");
            Assert.False(File.Exists(FilePath));
        }

        Assert.True(File.Exists(FilePath));
        using var again = Store();
        Assert.Equal([doc], Paths(again));
    }

    [Fact]
    public void A_save_that_dies_half_way_leaves_the_previous_file_and_never_an_empty_one()
    {
        var one = _documents.File("one.md", "# 1");
        var log = new TestLogger();
        using var store = Store(log);
        store.Record(one, "One");
        store.Flush();
        var good = File.ReadAllText(FilePath);

        // The new content is written to history.json.tmp first. With a directory standing where that file
        // belongs the write fails at once - as a power cut would, half-way - and the old file must still
        // be there, whole, rather than truncated by a write in place.
        _ = Directory.CreateDirectory(FilePath + ".tmp");
        store.Record(_documents.File("two.md", "# 2"), "Two");
        store.Flush();

        Assert.Equal(good, File.ReadAllText(FilePath));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
        Directory.Delete(FilePath + ".tmp");
        store.Record(_documents.File("three.md", "# 3"), "Three");
        store.Flush();
        Assert.Equal(3, ArrayLength());
    }

    private int ArrayLength()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        return document.RootElement.GetArrayLength();
    }

    private HistoryStore Store(ILogger? log = null) => new(_data.Path, log);
    private static List<string> Paths(HistoryStore store) => [.. store.Entries.Select(e => e.Path)];

    private static bool PathIs(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
