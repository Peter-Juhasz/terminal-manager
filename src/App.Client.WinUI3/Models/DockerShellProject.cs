using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>A project whose commands are run by <c>sh</c> in a running Docker container.</summary>
public sealed record class DockerShellProject : DockerProject
{
    [JsonIgnore]
    public override string TerminalDisplayName => "Docker (shell)";

    private protected override IEnumerable<string> CreateShellArguments(string body, IReadOnlyDictionary<string, string> environmentVariables) => RunByShell(body, environmentVariables);
}
