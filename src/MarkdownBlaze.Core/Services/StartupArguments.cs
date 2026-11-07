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
/// <para>
/// <b>Switches come first.</b> The command line is the switches, then the documents: the first argument
/// that is not a switch (or the value of <c>--log-level</c>) ends the switches, and everything after it is
/// a path, whatever it looks like. This matters because the <c>markdown://</c> handler is registered as
/// <c>"exe" "%1"</c> and the address is put there by somebody else: a launcher that does not escape the
/// quotes in a link could turn <c>markdown://document/#x" --unregister "</c> into extra arguments, and a
/// switch that is honoured anywhere would let a link change the machine's registration, turn on the
/// developer tools or set the log level. <see cref="SwitchesOf"/> is the one place that decides which
/// arguments are switches, and every reader of the command line goes through it.
/// </para>
/// </summary>
internal sealed record StartupArguments(IReadOnlyList<string> Values)
{
    private const string LogLevelSwitch = "--log-level";
    private const string RegisterSwitch = "--register";
    private const string UnregisterSwitch = "--unregister";
    private const string DevToolsSwitch = "--devtools";
    private const string EndOfSwitches = "--";

    /// <summary>The level the log is kept at when the command line does not ask for another.</summary>
    public const LogLevel DefaultLogLevel = LogLevel.Information;

    /// <summary>The arguments as the process received them, minus <c>--log-level</c> and its value (when they are leading switches).</summary>
    public static StartupArguments From(IEnumerable<string> args)
    {
        var all = args.ToList();
        var leading = LeadingCount(all);
        var values = new List<string>();
        for (var i = 0; i < all.Count; i++)
        {
            var arg = all[i];
            if (i < leading && arg.StartsWith(LogLevelSwitch + "=", StringComparison.OrdinalIgnoreCase)) continue;
            if (i < leading && IsLogLevelSwitch(arg))
            {
                i++; // the value that belongs to the switch
                continue;
            }

            values.Add(arg);
        }

        return new StartupArguments(values);
    }

    /// <summary>
    /// The arguments that are switches: the ones at the start of the command line, up to the first argument
    /// that is not one. A switch starts with <c>--</c>; <c>--log-level</c> without an <c>=</c> takes the
    /// argument after it as its value, which is a switch's too. A bare <c>--</c> ends the switches. What
    /// follows is documents, and is never looked at here.
    /// </summary>
    /// <param name="args">The command-line arguments (without the executable).</param>
    public static IReadOnlyList<string> SwitchesOf(IEnumerable<string> args)
    {
        var all = args.ToList();
        return all[..LeadingCount(all)];
    }

    /// <summary>
    /// The level asked for with <c>--log-level</c>, or <see cref="DefaultLogLevel"/> when there is no such
    /// switch or its value is not a level (there is no log yet to complain to).
    /// </summary>
    public static LogLevel LogLevelOf(IEnumerable<string> args)
    {
        string? value = null;
        string? previous = null;
        foreach (var arg in SwitchesOf(args))
        {
            if (arg.StartsWith(LogLevelSwitch + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(LogLevelSwitch.Length + 1)..];
            else if (IsLogLevelSwitch(previous))
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
        foreach (var arg in SwitchesOf(args))
        {
            if (string.Equals(arg, RegisterSwitch, StringComparison.OrdinalIgnoreCase)) request = true;
            else if (string.Equals(arg, UnregisterSwitch, StringComparison.OrdinalIgnoreCase)) request = false;
        }

        return request;
    }

    /// <summary>Whether <c>--devtools</c> is among the switches: the web view's developer tools are asked for.</summary>
    public static bool DevToolsRequestedBy(IEnumerable<string> args) =>
        SwitchesOf(args).Contains(DevToolsSwitch, StringComparer.OrdinalIgnoreCase);

    /// <summary>How many arguments at the start of the list are switches (and the value of <c>--log-level</c>).</summary>
    private static int LeadingCount(List<string> all)
    {
        var count = 0;
        while (count < all.Count)
        {
            var arg = all[count];
            if (string.Equals(arg, EndOfSwitches, StringComparison.Ordinal) || !arg.StartsWith("--", StringComparison.Ordinal)) break;

            count++;
            if (IsLogLevelSwitch(arg) && count < all.Count) count++; // its value
        }

        return count;
    }

    private static bool IsLogLevelSwitch(string? arg) => string.Equals(arg, LogLevelSwitch, StringComparison.OrdinalIgnoreCase);

    private static string Alias(string value) => value.Trim().ToUpperInvariant() switch
    {
        "INFO" => nameof(LogLevel.Information),
        "WARN" => nameof(LogLevel.Warning),
        "OFF" => nameof(LogLevel.None),
        _ => value.Trim(),
    };
}
