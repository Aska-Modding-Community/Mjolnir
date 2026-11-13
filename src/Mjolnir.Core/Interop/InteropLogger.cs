using System;
using Microsoft.Extensions.Logging;
using MjolnirLevel = Mjolnir.Core.LogLevel;
using MsLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Mjolnir.Core.Interop;

/// <summary>Routes Il2CppInterop's logging into LogOutput.log.</summary>
internal sealed class InteropLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => NoScope.Instance;
    public bool IsEnabled(MsLevel logLevel) => logLevel >= MsLevel.Information;

    public void Log<TState>(MsLevel logLevel, EventId eventId, TState state, Exception? exception,
                            Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var level = logLevel switch
        {
            MsLevel.Warning => MjolnirLevel.Warn,
            MsLevel.Error or MsLevel.Critical => MjolnirLevel.Error,
            _ => MjolnirLevel.Info,
        };
        string message = formatter(state, exception);
        if (exception != null) message += Environment.NewLine + exception;
        Core.Log.Write(level, "Il2CppInterop", message);
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();
        public void Dispose() { }
    }
}
