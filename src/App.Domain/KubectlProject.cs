using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run in a container of a Kubernetes pod, each by <c>kubectl exec</c>:
/// by its shell for <see cref="KubectlShellProject"/>, or by PowerShell for <see cref="KubectlPowerShellProject"/>.
/// </summary>
/// <remarks>
/// When a command exits with a code other than 0, kubectl tells so on its standard error, after what the command wrote.
/// </remarks>
public abstract record class KubectlProject : ContainerProject
{
    /// <summary>The name of the kubeconfig context the pod is in, or <see langword="null"/> for the current context.</summary>
    public string? Context { get; init; }

    /// <summary>The namespace the pod is in, or <see langword="null"/> for that of the context.</summary>
    public string? Namespace { get; init; }

    /// <summary>The name of the pod, or a resource that kubectl picks one of, such as <c>deploy/my-app</c>, so it still works once the pod is replaced.</summary>
    public string Pod { get; init; } = string.Empty;

    /// <summary>The name of the container in the pod, or <see langword="null"/> for its default container.</summary>
    public string? Container { get; init; }

    [JsonIgnore]
    public override string LocationDisplayName => $"{Pod}: {WorkingFolder}";

    private static readonly Uri IconUri = new("ms-appx:///Assets/kubernetes.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    private protected override string FileName => "kubectl";

    private protected override IEnumerable<string> CreateExecArguments()
    {
        yield return "exec";
        if (Context is { Length: > 0 } context)
        {
            yield return "--context";
            yield return context;
        }

        if (Namespace is { Length: > 0 } @namespace)
        {
            yield return "--namespace";
            yield return @namespace;
        }

        if (Container is { Length: > 0 } container)
        {
            yield return "--container";
            yield return container;
        }

        yield return Pod;
        yield return "--";
    }
}
