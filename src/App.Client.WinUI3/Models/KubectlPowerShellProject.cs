using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>A project whose commands are run by PowerShell (pwsh) in a container of a Kubernetes pod.</summary>
public sealed record class KubectlPowerShellProject : KubectlProject
{
    [JsonIgnore]
    public override string TerminalDisplayName => "Kubernetes (PowerShell)";

    private protected override IEnumerable<string> CreateShellArguments(string body, IReadOnlyDictionary<string, string> environmentVariables) => RunByPowerShell(body, environmentVariables);
}
