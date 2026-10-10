using Microsoft.Extensions.Logging;

namespace MarkdownBlaze.Services;

/// <summary>
/// The command-line arguments the process was started with, as <c>Main</c> received them — handed to
/// the services that need them rather than each of them re-reading the process's own command line.
/// <para>
/// <c>--log-level &lt;level&gt;</c> (or <c>--log-level=&lt;level&gt;</c>) is the one switch that takes a
/// value; <see cref="From"/> takes it out, so that the word after it is never mistaken for a folder to
/// open. The level is one of trace, debug, information, warning, error, critical, none.
/// </para>
/// </summary>
internal sealed record StartupArguments(IReadOnlyList<string> Values)
{
    private const string LogLevelSwitch = "--log-level";
    private const string RegisterSwitch = "--register";
    private const string UnregisterSwitch = "--unregister";

    /// <summary>The level the log is kept at when the command line does not ask for another.</summary>
    public const LogLevel DefaultLogLevel = LogLevel.Information;

    /// <summary>The arguments as the process received them, minus <c>--log-level</c> and its value.</summary>
    public static StartupArguments From(IEnumerable<string> args)
    {
        var values = new List<string>();
        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var arg = enumerator.Current;
            if (arg.StartsWith(LogLevelSwitch + "=", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(arg, LogLevelSwitch, StringComparison.OrdinalIgnoreCase))
            {
                _ = enumerator.MoveNext(); // the value that belongs to the switch
                continue;
            }

            values.Add(arg);
        }

        return new StartupArguments(values);
    }

    /// <summary>
    /// The level asked for with <c>--log-level</c>, or <see cref="DefaultLogLevel"/> when there is no such
    /// switch or its value is not a level (there is no log yet to complain to).
    /// </summary>
    public static LogLevel LogLevelOf(IEnumerable<string> args)
    {
        string? value = null;
        string? previous = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith(LogLevelSwitch + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(LogLevelSwitch.Length + 1)..];
            else if (string.Equals(previous, LogLevelSwitch, StringComparison.OrdinalIgnoreCase))
                value = arg;

            previous = arg;
        }

        return value is not null && Enum.TryParse<LogLevel>(Alias(value), ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : DefaultLogLevel;
    }

    /// <summary>
    /// Whether the command line asks to change the Windows registration instead of opening a window:
    /// true for <c>--register</c>, false for <c>--unregister</c>, null for neither. With both, the last wins.
    /// </summary>
    public static bool? RegistrationRequestOf(IEnumerable<string> args)
    {
        bool? request = null;
        foreach (var arg in args)
        {
            if (string.Equals(arg, RegisterSwitch, StringComparison.OrdinalIgnoreCase)) request = true;
            else if (string.Equals(arg, UnregisterSwitch, StringComparison.OrdinalIgnoreCase)) request = false;
        }

        return request;
    }

    private static string Alias(string value) => value.Trim().ToUpperInvariant() switch
    {
        "INFO" => nameof(LogLevel.Information),
        "WARN" => nameof(LogLevel.Warning),
        "OFF" => nameof(LogLevel.None),
        _ => value.Trim(),
    };
}
