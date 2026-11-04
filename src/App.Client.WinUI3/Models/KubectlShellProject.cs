using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>A project whose commands are run by <c>sh</c> in a container of a Kubernetes pod.</summary>
public sealed record class KubectlShellProject : KubectlProject
{
    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Shell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Kubernetes (shell)";
}
