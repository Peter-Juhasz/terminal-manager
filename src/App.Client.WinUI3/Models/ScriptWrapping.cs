namespace App.Client.WinUI3.Models;

/// <summary>
/// Puts the code of a project before and after each command, for projects whose commands are code passed as a whole.
/// </summary>
internal static class ScriptWrapping
{
    /// <summary>The code that is run for <paramref name="command"/>, with <paramref name="prepend"/> before it and <paramref name="append"/> after it, each on lines of their own.</summary>
    public static string Wrap(string? prepend, string command, string? append)
    {
        ArgumentNullException.ThrowIfNull(command);

        var prefix = prepend is null or "" ? string.Empty : prepend + "\n";
        return append is null or "" ? prefix + command : prefix + command + "\n" + append;
    }
}
