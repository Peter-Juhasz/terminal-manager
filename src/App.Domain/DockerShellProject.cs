using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>A project whose commands are run by <c>sh</c> in a running Docker container.</summary>
public sealed record class DockerShellProject : DockerProject
{
    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Shell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Docker (shell)";
}
