using System.Text;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by PowerShell on a Windows Azure virtual machine, by the <c>RunPowerShellScript</c> run command.
/// </summary>
/// <remarks>
/// The command is run in a <c>try</c> block, so its exit code can still be written after it; a terminating error ends it with an exit code of 1.
/// </remarks>
public sealed record class AzureVmPowerShellProject : AzureVmProject
{
    [JsonIgnore]
    public override string CommandId => "RunPowerShellScript";

    [JsonIgnore]
    public override string ScriptFileExtension => ".ps1";

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.PowerShell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Azure VM (PowerShell)";

    // The exit code is written by the console rather than the pipeline, so it is still written when the command exits.
    private protected override string CreateScript(string body, IReadOnlyDictionary<string, string> environmentVariables, string exitCodeMarker)
    {
        var script = new StringBuilder();
        script.Append("$ProgressPreference = 'SilentlyContinue'\n");
        script.Append("$global:LASTEXITCODE = 0\n");
        script.Append("try {\n");
        script.AppendPowerShellPreamble(WorkingFolder, environmentVariables);
        script.Append(body).Append('\n');
        script.Append("}\n");
        script.Append("catch {\n");
        script.Append("Write-Error -ErrorRecord $_ -ErrorAction Continue\n");
        script.Append("$global:LASTEXITCODE = 1\n");
        script.Append("}\n");
        script.Append("finally {\n");
        script.Append("[Console]::Out.Write(\"`n\" + ").Append(PowerShellScript.Quote(exitCodeMarker)).Append(" + [string]$global:LASTEXITCODE + \"`n\")\n");
        script.Append("}\n");
        return script.ToString();
    }
}
