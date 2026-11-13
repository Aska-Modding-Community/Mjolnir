using System.Collections.Generic;

namespace Mjolnir.SDK.UI;

/// <summary>The lines run in the console, for Up and Down (pure): the newest last, a repeat of the last one kept once.</summary>
internal sealed class CommandHistory
{
    private const int MaxLines = 100;
    private readonly List<string> _lines = [];
    private int _at;
    private string _typing = "";

    /// <summary>A line run: kept, and returned trimmed; null for an empty line.</summary>
    public string? Add(string line)
    {
        line = line.Trim();
        _typing = "";
        if (line.Length == 0)
        {
            _at = _lines.Count;
            return null;
        }
        if (_lines.Count == 0 || _lines[^1] != line) _lines.Add(line);
        if (_lines.Count > MaxLines) _lines.RemoveAt(0);
        _at = _lines.Count;
        return line;
    }

    /// <summary>The line before the one shown (Up); what was being typed is kept for Down. Null when there is none.</summary>
    public string? Back(string typing)
    {
        if (_at == 0) return null;
        if (_at == _lines.Count) _typing = typing;
        return _lines[--_at];
    }

    /// <summary>The line after the one shown (Down), and at the end what was being typed. Null when already there.</summary>
    public string? Forward()
    {
        if (_at >= _lines.Count) return null;
        _at++;
        return _at == _lines.Count ? _typing : _lines[_at];
    }
}
