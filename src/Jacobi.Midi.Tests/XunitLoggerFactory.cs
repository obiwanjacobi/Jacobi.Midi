using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jacobi.Midi.Tests;

/// <summary>
/// An <see cref="ILoggerFactory"/> that routes all log output to the xUnit <see cref="ITestOutputHelper"/>
/// so each test displays its own log.
/// </summary>
public sealed class XunitLoggerFactory : ILoggerFactory
{
    private readonly ITestOutputHelper _output;
    private readonly LogLevel _minLevel;

    public XunitLoggerFactory(ITestOutputHelper output, LogLevel minLevel = LogLevel.Trace)
    {
        _output = output;
        _minLevel = minLevel;
    }

    public bool LogScope { get; set; }

    public void AddProvider(ILoggerProvider provider)
    { }

    public ILogger CreateLogger(string categoryName)
        => new XunitLogger(_output, categoryName, _minLevel, LogScope);

    public void Dispose()
    { }

    private sealed class XunitLogger(ITestOutputHelper output, string category, LogLevel minLevel, bool logScope) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => logScope ? new LogScope(this, JsonSerializer.Serialize(state)) : null;

        private sealed class LogScope : IDisposable
        {
            private readonly XunitLogger _logger;
            private readonly string _state;

            public LogScope(XunitLogger logger, string stateJson)
            {
                _logger = logger;
                _state = stateJson;

                _logger.LogTrace("Start Log Scope: {State}", _state);
            }
            public void Dispose()
            {
                _logger.LogTrace("End Log Scope: {State}", _state);
            }
        }

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = $"[{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
                message += Environment.NewLine + exception;

            try
            {
                output.WriteLine(message);
            }
            catch (InvalidOperationException)
            {
                // test has already completed
            }
        }
    }
}
