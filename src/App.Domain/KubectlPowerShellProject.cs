using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>A project whose commands are run by PowerShell (pwsh) in a container of a Kubernetes pod.</summary>
public sealed record class KubectlPowerShellProject : KubectlProject
{
    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.PowerShell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Kubernetes (PowerShell)";
}
