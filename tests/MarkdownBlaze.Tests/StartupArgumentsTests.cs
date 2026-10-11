using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>
/// The command line: the switches, which come first, and the arguments that are left for the documents.
/// SEC-18: anything after the first argument that is not a switch is a path and never a switch.
/// </summary>
public sealed class StartupArgumentsTests
{
    [Theory]
    [InlineData("--log-level=debug", LogLevel.Debug)]
    [InlineData("--log-level=Trace", LogLevel.Trace)]
    [InlineData("--LOG-LEVEL=WARNING", LogLevel.Warning)]
    [InlineData("--log-level=warn", LogLevel.Warning)]
    [InlineData("--log-level=info", LogLevel.Information)]
    [InlineData("--log-level=error", LogLevel.Error)]
    [InlineData("--log-level=off", LogLevel.None)]
    public void The_level_can_be_given_with_an_equals_sign(string argument, LogLevel expected) =>
        Assert.Equal(expected, StartupArguments.LogLevelOf([argument, "notes.md"]));

    [Fact]
    public void The_level_can_be_given_as_the_next_argument()
    {
        Assert.Equal(LogLevel.Debug, StartupArguments.LogLevelOf(["--log-level", "debug", "notes.md"]));
        Assert.Equal(LogLevel.Critical, StartupArguments.LogLevelOf(["--devtools", "--log-level", "critical", "notes.md"]));
    }

    [Theory]
    [InlineData("--log-level=loud")]
    [InlineData("--log-level=")]
    [InlineData("--log-level=7")]
    [InlineData("--log-level")]
    public void Anything_that_is_not_a_level_leaves_the_default(string argument) =>
        Assert.Equal(StartupArguments.DefaultLogLevel, StartupArguments.LogLevelOf([argument]));

    [Fact]
    public void Without_the_switch_the_level_is_the_default()
    {
        Assert.Equal(LogLevel.Information, StartupArguments.LogLevelOf([]));
        Assert.Equal(LogLevel.Information, StartupArguments.LogLevelOf(["--devtools", "notes.md"]));
    }

    [Fact]
    public void The_switch_and_its_value_are_not_mistaken_for_a_document_to_open()
    {
        Assert.Equal(["notes.md", "--devtools"], StartupArguments.From(["--log-level", "debug", "notes.md", "--devtools"]).Values);
        Assert.Equal(["notes.md"], StartupArguments.From(["--log-level=debug", "notes.md"]).Values);
        Assert.Equal(["a.md", "b.md"], StartupArguments.From(["a.md", "b.md"]).Values);
        Assert.Empty(StartupArguments.From(["--log-level"]).Values); // a switch with nothing after it
    }

    [Fact]
    public void The_registration_switches_ask_for_registering_or_unregistering()
    {
        Assert.True(StartupArguments.RegistrationRequestOf(["--register"]));
        Assert.False(StartupArguments.RegistrationRequestOf(["--UNREGISTER"]));
        Assert.True(StartupArguments.RegistrationRequestOf(["--unregister", "--register"])); // the last wins
        Assert.Null(StartupArguments.RegistrationRequestOf([]));
        Assert.Null(StartupArguments.RegistrationRequestOf(["--registers", "--log-level", "debug", "notes.md"]));
    }

    [Fact]
    public void The_developer_tools_are_asked_for_by_a_leading_devtools_switch()
    {
        Assert.True(StartupArguments.DevToolsRequestedBy(["--devtools", "x.md"]));
        Assert.True(StartupArguments.DevToolsRequestedBy(["--log-level", "debug", "--DEVTOOLS", "x.md"]));
        Assert.False(StartupArguments.DevToolsRequestedBy(["x.md"]));
        Assert.False(StartupArguments.DevToolsRequestedBy([]));
    }

    // ---- SEC-18 ---------------------------------------------------------------------------------------------

    /// <summary>
    /// What the protocol handler's <c>"exe" "%1"</c> turns into when a link carries a quote and the launcher does
    /// not escape it: the address, then whatever the link's author put after it.
    /// </summary>
    public static TheoryData<string[]> InjectedAfterTheDocument() =>
    [
        ["markdown://document/#x", "--unregister"],
        ["markdown://document/#x", "--register"],
        ["markdown://document/#x", "--devtools"],
        ["markdown://document/#x", "--log-level=trace"],
        ["markdown://document/#x", "--log-level", "trace"],
        ["notes.md", "--unregister", "--devtools", "--log-level=trace"],
        ["notes.md", "--", "--unregister"],
    ];

    [Theory]
    [MemberData(nameof(InjectedAfterTheDocument))]
    public void A_switch_after_a_document_is_a_path_and_changes_nothing(string[] args)
    {
        Assert.Null(StartupArguments.RegistrationRequestOf(args));
        Assert.False(StartupArguments.DevToolsRequestedBy(args));
        Assert.Equal(StartupArguments.DefaultLogLevel, StartupArguments.LogLevelOf(args));
        Assert.Empty(StartupArguments.SwitchesOf(args));
        Assert.Equal(args, StartupArguments.From(args).Values); // every word of it is left for the documents
    }

    [Fact]
    public void Switches_before_the_first_document_still_work()
    {
        string[] args = ["--devtools", "--log-level", "debug", "--register", "x.md", "--unregister"];

        Assert.True(StartupArguments.RegistrationRequestOf(args)); // the --unregister after x.md is not heard
        Assert.True(StartupArguments.DevToolsRequestedBy(args));
        Assert.Equal(LogLevel.Debug, StartupArguments.LogLevelOf(args));
        Assert.Equal(["--devtools", "--log-level", "debug", "--register"], StartupArguments.SwitchesOf(args));
        Assert.Equal(["--devtools", "--register", "x.md", "--unregister"], StartupArguments.From(args).Values); // only --log-level and its value are taken out
    }

    [Fact]
    public void The_switches_end_at_the_first_argument_that_is_not_one_and_at_a_bare_double_dash()
    {
        Assert.Equal(["--devtools"], StartupArguments.SwitchesOf(["--devtools", "x.md", "--register"]));
        Assert.Equal(["--devtools"], StartupArguments.SwitchesOf(["--devtools", "--", "--register"]));
        Assert.Empty(StartupArguments.SwitchesOf(["x.md"]));
        Assert.Empty(StartupArguments.SwitchesOf(["-register"])); // one dash is not a switch of this app's
        Assert.Equal(["--log-level", "x.md", "--register"], StartupArguments.SwitchesOf(["--log-level", "x.md", "--register"])); // x.md is the value, --register is not
    }

    [Fact]
    public void The_log_level_switch_is_taken_out_only_while_it_leads()
    {
        Assert.Equal(["x.md", "--log-level", "debug"], StartupArguments.From(["x.md", "--log-level", "debug"]).Values);
        Assert.Equal(["x.md"], StartupArguments.From(["--log-level", "debug", "x.md"]).Values);
    }
}
