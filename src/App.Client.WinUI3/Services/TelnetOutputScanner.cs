using System.Globalization;
using System.Text;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Finds what a command run over Telnet wrote in the text the terminal sends, as it is read: what comes before the line of <paramref name="startMarker"/>,
/// such as the banner, the prompt and the echo of what was typed, is left out, and so is the line of <paramref name="exitCodeMarker"/>,
/// which tells the exit code, with the line break written before it.
/// </summary>
/// <remarks>
/// The line breaks of the terminal, a carriage return followed by a line feed, are read as a line feed, so a command that writes a line feed has it shown as it wrote it;
/// a carriage return on its own, as a progress bar writes it, is kept. Text that may be the start of the marker is held back, until what follows tells it isn't.
/// </remarks>
public sealed class TelnetOutputScanner(string startMarker, string exitCodeMarker)
{
    // the text the marker is found in, which it is written after, at the start of a line of its own
    private readonly string _exitCodeLine = "\n" + exitCodeMarker;

    // read, but not yet passed on, as it may be the start of a marker, or a carriage return a line feed may still follow
    private readonly StringBuilder _pending = new();

    /// <summary>Whether the line of the start marker was read, so what follows it is what the command wrote.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>The exit code of the command, once the line of the exit code marker was read in full, or <see langword="null"/> before.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>What was read before the start marker, with its line breaks as they are read, such as the messages of the login; it is kept as long as the start marker isn't read.</summary>
    public string Preamble => IsStarted ? string.Empty : _pending.ToString();

    /// <summary>Reads <paramref name="text"/>, which continues what was read before, and returns what the command wrote in it, as far as it is known to be.</summary>
    public string Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (ExitCode is not null)
        {
            return string.Empty;
        }

        _pending.Append(text);
        Normalize();
        if (!IsStarted && !TryStart())
        {
            return string.Empty;
        }

        var pending = _pending.ToString();
        var markerStart = pending.IndexOf(_exitCodeLine, StringComparison.Ordinal);
        if (markerStart >= 0)
        {
            var codeStart = markerStart + _exitCodeLine.Length;
            var lineEnd = pending.IndexOf('\n', codeStart);

            // the exit code is only known in full at the end of its line
            if (lineEnd < 0)
            {
                return Take(markerStart);
            }

            ExitCode = int.TryParse(pending.AsSpan(codeStart, lineEnd - codeStart).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exitCode) ? exitCode : 1;
            var written = pending[..markerStart];
            _pending.Clear();
            return written;
        }

        return Take(pending.Length - HeldBackLength(pending));
    }

    /// <summary>What was read, and held back, once nothing more is read, as the connection was closed before the exit code was told.</summary>
    public string Flush()
    {
        if (!IsStarted || ExitCode is not null)
        {
            return string.Empty;
        }

        var rest = _pending.ToString();
        _pending.Clear();
        return rest;
    }

    // a carriage return at the end may still be followed by a line feed, so it is held back by HeldBackLength
    private void Normalize() => _pending.Replace("\r\n", "\n");

    private bool TryStart()
    {
        var pending = _pending.ToString();
        var markerStart = pending.IndexOf(startMarker, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            return false;
        }

        var lineEnd = pending.IndexOf('\n', markerStart + startMarker.Length);
        if (lineEnd < 0)
        {
            return false;
        }

        _pending.Remove(0, lineEnd + 1);
        IsStarted = true;
        return true;
    }

    private string Take(int length)
    {
        var taken = _pending.ToString(0, length);
        _pending.Remove(0, length);
        return taken;
    }

    // the longest end of the text that the line of the exit code starts with, or the carriage return it ends with
    private int HeldBackLength(string pending)
    {
        for (var length = Math.Min(_exitCodeLine.Length - 1, pending.Length); length > 0; length--)
        {
            if (pending.AsSpan(pending.Length - length).SequenceEqual(_exitCodeLine.AsSpan(0, length)))
            {
                return length;
            }
        }

        return pending.EndsWith('\r') ? 1 : 0;
    }
}
