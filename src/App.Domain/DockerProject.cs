using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run in a running Docker container, each by <c>docker exec</c>:
/// by its shell for <see cref="DockerShellProject"/>, or by PowerShell for <see cref="DockerPowerShellProject"/>.
/// </summary>
public abstract record class DockerProject : ContainerProject
{
    /// <summary>The name of the Docker context the container is in, or <see langword="null"/> for the current context of the Docker CLI.</summary>
    public string? Context { get; init; }

    /// <summary>The name or id of the container.</summary>
    public string Container { get; init; } = string.Empty;

    /// <summary>The user commands are run as, as in <c>name</c> or <c>uid:gid</c>, or <see langword="null"/> for the user of the container.</summary>
    public string? User { get; init; }

    [JsonIgnore]
    public override string LocationDisplayName => $"{Container}: {WorkingFolder}";

    private static readonly Uri IconUri = new("ms-appx:///Assets/docker.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    private protected override string FileName => "docker";

    private protected override IEnumerable<string> CreateExecArguments()
    {
        if (Context is { Length: > 0 } context)
        {
            yield return "--context";
            yield return context;
        }

        yield return "exec";
        if (User is { Length: > 0 } user)
        {
            yield return "--user";
            yield return user;
        }

        yield return Container;
    }
}
