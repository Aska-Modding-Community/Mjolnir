using System;

namespace Mjolnir.SDK;

/// <summary>
/// Thrown by a command for words it cannot use: the console shows the message and the command's usage, and the mod stays
/// enabled.
/// </summary>
public sealed class DevCommandException(string message) : Exception(message);
