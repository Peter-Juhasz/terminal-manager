using System.Text;
using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Models;

/// <summary>
/// Writes the parts of a script run by PowerShell, whose output is read by the app.
/// </summary>
internal static partial class PowerShellScript
{
    /// <summary>Has the output written as UTF-8, without progress bars, which are only meant for a console.</summary>
    public const string EncodingPrelude = """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'

        """;

    /// <summary>As <see cref="EncodingPrelude"/>, and without colors, which are only meant for a console too; <c>$PSStyle</c> was added in PowerShell 7.2.</summary>
    public const string PlainTextPrelude = EncodingPrelude + """
        $PSStyle.OutputRendering = 'PlainText'

        """;

    extension(StringBuilder script)
    {
        /// <summary>Changes to <paramref name="folder"/>, stopping when it can't, then sets <paramref name="environmentVariables"/>, each on a line of its own.</summary>
        public StringBuilder AppendPowerShellPreamble(string folder, IReadOnlyDictionary<string, string> environmentVariables)
        {
            script.Append("Set-Location -LiteralPath ").Append(Quote(folder)).Append(" -ErrorAction Stop\n");
            foreach (var (name, value) in environmentVariables)
            {
                script.Append("${env:").Append(name.Replace("}", "`}", StringComparison.Ordinal)).Append("} = ").Append(Quote(value)).Append('\n');
            }

            return script;
        }
    }

    /// <summary>Quotes <paramref name="value"/> for PowerShell, so it is taken as it is; each kind of single quote it takes is doubled.</summary>
    public static string Quote(string value) => $"'{SingleQuotes().Replace(value, "$0$0")}'";

    [GeneratedRegex("['‘’‚‛]")]
    private static partial Regex SingleQuotes();
}
