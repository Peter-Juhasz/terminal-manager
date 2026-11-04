using System.Text;

namespace App.Client.WinUI3.Models;

/// <summary>
/// Writes the parts of a script run by a POSIX shell, such as <c>sh</c>, elsewhere than on this machine.
/// </summary>
internal static class ShellScript
{
    extension(StringBuilder script)
    {
        /// <summary>Changes to <paramref name="folder"/>, exiting when it can't, then exports <paramref name="environmentVariables"/>, each on a line of its own.</summary>
        public StringBuilder AppendShellPreamble(string folder, IReadOnlyDictionary<string, string> environmentVariables)
        {
            script.Append("cd -- ").Append(QuoteFolder(folder)).Append(" || exit\n");
            foreach (var (name, value) in environmentVariables)
            {
                script.Append("export ").Append(name).Append('=').Append(Quote(value)).Append('\n');
            }

            return script;
        }
    }

    /// <summary>Quotes <paramref name="value"/> for the shell, so it is taken as it is.</summary>
    public static string Quote(string value) => $"'{value.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    // a folder in the home folder is still found in it, as ~ is only expanded unquoted
    private static string QuoteFolder(string folder) => folder switch
    {
        "~" => "~",
        _ when folder.StartsWith("~/", StringComparison.Ordinal) => "~/" + Quote(folder[2..]),
        _ => Quote(folder),
    };
}
