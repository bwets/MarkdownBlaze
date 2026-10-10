using MarkdownBlaze.Services;
using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Tests;

/// <summary>The command line: <c>--log-level</c> and the arguments that are left for the documents.</summary>
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
        Assert.Equal(expected, StartupArguments.LogLevelOf(["notes.md", argument]));

    [Fact]
    public void The_level_can_be_given_as_the_next_argument()
    {
        Assert.Equal(LogLevel.Debug, StartupArguments.LogLevelOf(["--log-level", "debug", "notes.md"]));
        Assert.Equal(LogLevel.Critical, StartupArguments.LogLevelOf(["notes.md", "--log-level", "critical"]));
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
        Assert.Equal(LogLevel.Information, StartupArguments.LogLevelOf(["notes.md", "--devtools"]));
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
        Assert.Null(StartupArguments.RegistrationRequestOf(["notes.md", "--registers", "--log-level", "debug"]));
    }
}
