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
    public override CommandLanguage Language => CommandLanguage.Shell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Azure VM (shell)";

    // In a subshell, so a command that exits still has its exit code written after it.
    // The exit code is written on a line of its own, as the output of the command may not end with one.
    private protected override string CreateScript(string body, IReadOnlyDictionary<string, string> environmentVariables, string exitCodeMarker)
    {
        var script = new StringBuilder();
        script.Append("(\n");
        script.AppendShellPreamble(WorkingFolder, environmentVariables);
        script.Append(body).Append('\n');
        script.Append(")\n");
        script.Append("printf '\\n%s%d\\n' ").Append(ShellScript.Quote(exitCodeMarker)).Append(" \"$?\"\n");
        return script.ToString();
    }
}
