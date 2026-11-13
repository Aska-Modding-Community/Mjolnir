using Mjolnir.Core;

namespace Mjolnir.SDK;

/// <summary>A mod's log: lines go to the shared LogOutput.log with the mod's name as prefix.</summary>
public sealed class ModLogger
{
    private readonly string _source;

    internal ModLogger(string source) => _source = source;

    public void Debug(string message) => Log.Write(LogLevel.Debug, _source, message);
    public void Info(string message) => Log.Write(LogLevel.Info, _source, message);
    public void Warn(string message) => Log.Write(LogLevel.Warn, _source, message);
    public void Error(string message) => Log.Write(LogLevel.Error, _source, message);
}
