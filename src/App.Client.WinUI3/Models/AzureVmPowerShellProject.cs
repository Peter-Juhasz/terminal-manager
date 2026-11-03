using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by PowerShell on a Windows Azure virtual machine, by the <c>RunPowerShellScript</c> run command.
/// </summary>
/// <remarks>
/// The command is run in a <c>try</c> block, so its exit code can still be written after it; a terminating error ends it with an exit code of 1.
/// </remarks>
public sealed partial record class AzureVmPowerShellProject : AzureVmProject
{
    [JsonIgnore]
    public override string CommandId => "RunPowerShellScript";

    [JsonIgnore]
    public override string ScriptFileExtension => ".ps1";

    [JsonIgnore]
    public override string TerminalDisplayName => "Azure VM (PowerShell)";

    /// <summary>
    /// What is put before a command when it is highlighted, so what <see cref="AzureVmProject.PrependScript"/> defines, or leaves open, is taken into account.
    /// </summary>
    [JsonIgnore]
    public string EditorPrefix => PrependScript is null or "" ? string.Empty : PrependScript + "\n";

    // The exit code is written by the console rather than the pipeline, so it is still written when the command exits.
    private protected override string CreateScript(string body, IReadOnlyDictionary<string, string> environmentVariables, string exitCodeMarker)
    {
        var script = new StringBuilder();
        script.Append("$ProgressPreference = 'SilentlyContinue'\n");
        script.Append("$global:LASTEXITCODE = 0\n");
        script.Append("try {\n");
        script.Append("Set-Location -LiteralPath ").Append(Quote(WorkingFolder)).Append(" -ErrorAction Stop\n");
        foreach (var (name, value) in environmentVariables)
        {
            script.Append("${env:").Append(name.Replace("}", "`}", StringComparison.Ordinal)).Append("} = ").Append(Quote(value)).Append('\n');
        }

        script.Append(body).Append('\n');
        script.Append("}\n");
        script.Append("catch {\n");
        script.Append("Write-Error -ErrorRecord $_ -ErrorAction Continue\n");
        script.Append("$global:LASTEXITCODE = 1\n");
        script.Append("}\n");
        script.Append("finally {\n");
        script.Append("[Console]::Out.Write(\"`n\" + ").Append(Quote(exitCodeMarker)).Append(" + [string]$global:LASTEXITCODE + \"`n\")\n");
        script.Append("}\n");
        return script.ToString();
    }

    /// <summary>Quotes <paramref name="value"/> for PowerShell, so it is taken as it is; each kind of single quote it takes is doubled.</summary>
    internal static string Quote(string value) => $"'{SingleQuotes().Replace(value, "$0$0")}'";

    [GeneratedRegex("['‘’‚‛]")]
    private static partial Regex SingleQuotes();
}
