using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>A project whose commands are run by PowerShell (pwsh) in a running Docker container.</summary>
public sealed record class DockerPowerShellProject : DockerProject
{
    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.PowerShell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Docker (PowerShell)";
}
