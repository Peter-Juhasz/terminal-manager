using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>A project whose commands are run by PowerShell (pwsh) in a running Docker container.</summary>
public sealed record class DockerPowerShellProject : DockerProject
{
    [JsonIgnore]
    public override string TerminalDisplayName => "Docker (PowerShell)";

    private protected override IEnumerable<string> CreateShellArguments(string body, IReadOnlyDictionary<string, string> environmentVariables) => RunByPowerShell(body, environmentVariables);
}
