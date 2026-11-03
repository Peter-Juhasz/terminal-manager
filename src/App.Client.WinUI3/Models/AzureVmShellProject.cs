using System.Text;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by the shell of a Linux Azure virtual machine, by the <c>RunShellScript</c> run command.
/// </summary>
public sealed record class AzureVmShellProject : AzureVmProject
{
    [JsonIgnore]
    public override string CommandId => "RunShellScript";

    [JsonIgnore]
    public override string ScriptFileExtension => ".sh";

    [JsonIgnore]
    public override string TerminalDisplayName => "Azure VM (shell)";

    // In a subshell, so a command that exits still has its exit code written after it.
    // The exit code is written on a line of its own, as the output of the command may not end with one.
    private protected override string CreateScript(string body, IReadOnlyDictionary<string, string> environmentVariables, string exitCodeMarker)
    {
        var script = new StringBuilder();
        script.Append("(\n");
        script.Append("cd -- ").Append(QuoteFolder(WorkingFolder)).Append(" || exit\n");
        foreach (var (name, value) in environmentVariables)
        {
            script.Append("export ").Append(name).Append('=').Append(Quote(value)).Append('\n');
        }

        script.Append(body).Append('\n');
        script.Append(")\n");
        script.Append("printf '\\n%s%d\\n' ").Append(Quote(exitCodeMarker)).Append(" \"$?\"\n");
        return script.ToString();
    }

    /// <summary>Quotes <paramref name="value"/> for the shell, so it is taken as it is.</summary>
    internal static string Quote(string value) => $"'{value.Replace("'", @"'\''", StringComparison.Ordinal)}'";

    // a folder in the home folder is still found in it, as ~ is only expanded unquoted
    private static string QuoteFolder(string folder) => folder switch
    {
        "~" => "~",
        _ when folder.StartsWith("~/", StringComparison.Ordinal) => "~/" + Quote(folder[2..]),
        _ => Quote(folder),
    };
}
