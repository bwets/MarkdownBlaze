using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarkdownBlaze.Services;

/// <summary>
/// A way for static helpers to log. Services get their <see cref="ILogger{TCategoryName}"/> from the
/// container; code that is created before it exists or that is static (<c>ShellLauncher</c>,
/// the registry plumbing) has no constructor to inject it through, and threading a logger through every
/// such call site only for the sake of a warning would make them worse. So the factory is handed to this
/// class once, by <c>Program.Main</c> as soon as the container exists, and static code asks it for a
/// logger. Until then — and in tests that do not care — it hands out loggers that discard everything.
/// <para>
/// The logger a static class keeps in a field can be created before the factory is attached (a static
/// field is initialised on first use), so what <see cref="For(string)"/> returns looks the factory up
/// when it is used, not when it is made.
/// </para>
/// </summary>
internal static class Log
{
    private static ILoggerFactory s_factory = NullLoggerFactory.Instance;

    /// <summary>Makes <paramref name="factory"/> the source of every logger handed out from now on, and of those already handed out.</summary>
    public static void Attach(ILoggerFactory factory) => s_factory = factory;

    /// <summary>A logger categorised by <typeparamref name="T"/>, for code that cannot have one injected.</summary>
    /// <typeparam name="T">The class the messages are about.</typeparam>
    public static ILogger For<T>() => For(typeof(T).FullName ?? typeof(T).Name);

    /// <summary>
    /// A logger with the given category, for static classes — which cannot be a type argument, so
    /// <c>For&lt;T&gt;()</c> is not available to them. Pass <c>nameof(TheClass)</c>.
    /// </summary>
    public static ILogger For(string category) => new LateBoundLogger(category);

    private sealed class LateBoundLogger(string category) : ILogger
    {
        private readonly Lock _lock = new();
        private ILoggerFactory? _factory;
        private ILogger _inner = NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => Current().BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Current().IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Current().Log(logLevel, eventId, state, exception, formatter);

        private ILogger Current()
        {
            var factory = s_factory;
            lock (_lock)
            {
                if (!ReferenceEquals(factory, _factory))
                {
                    _inner = factory.CreateLogger(category);
                    _factory = factory;
                }

                return _inner;
            }
        }
    }
}
