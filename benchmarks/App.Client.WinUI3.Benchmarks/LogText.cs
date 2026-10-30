using System.Globalization;
using System.Text;

namespace App.Client.WinUI3.Benchmarks;

internal static class LogText
{
    /// <summary>
    /// Creates <paramref name="length"/> characters of lines like those of a build, with a progress sequence before every
    /// <paramref name="linesPerProgress"/>th, or none if it is 0.
    /// </summary>
    public static string Create(int length, int linesPerProgress)
    {
        var text = new StringBuilder(length + 256);
        for (var line = 0; text.Length < length; line++)
        {
            if (linesPerProgress > 0 && line % linesPerProgress == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\e]9;4;1;{line / linesPerProgress % 100}\a");
            }

            text.Append(CultureInfo.InvariantCulture, $"  Compiling src/module{line % 37}/file{line}.cs");
            text.Append('.', line % 7 * 10);
            text.Append("\r\n");
        }

        return text.ToString(0, length);
    }

    /// <summary>Splits <paramref name="text"/> into pieces of <paramref name="length"/>, the way it would be read.</summary>
    public static string[] Split(string text, int length) =>
        [.. text.Chunk(length).Select(static piece => new string(piece))];
}
