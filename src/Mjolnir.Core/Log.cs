namespace Mjolnir.Core;

internal enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

/// <summary>Writes through the native logger so native and managed lines share LogOutput.log.</summary>
internal static unsafe class Log
{
    private static delegate* unmanaged<int, char*, void> s_sink;

    internal static void Init(delegate* unmanaged<int, char*, void> sink) => s_sink = sink;

    public static void Write(LogLevel level, string source, string message)
    {
        if (s_sink == null) return;
        string line = $"[{source}] {message}";
        fixed (char* p = line) s_sink((int)level, p);
    }

    public static void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public static void Warn(string source, string message) => Write(LogLevel.Warn, source, message);
    public static void Error(string source, string message) => Write(LogLevel.Error, source, message);
}
