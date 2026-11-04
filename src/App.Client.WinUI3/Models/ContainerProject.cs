using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run in a container, each by a client on this machine that runs a script in it, such as <c>docker exec</c> for <see cref="DockerProject"/>
/// and <c>kubectl exec</c> for <see cref="KubectlProject"/>; the script changes to the working folder in the container first, and is run by its shell, or by PowerShell.
/// </summary>
/// <remarks>
/// The output is shown as it is written, and the exit code is that of the script, as the client reports both as they are.
/// Commands are run as the user the container runs as, which is why they can't be run as an administrator in addition.
/// Stopping a command stops the client, but the command may keep running in the container.
/// </remarks>
public abstract record class ContainerProject : Project
{
    // The output is read without progress bars or colors, which are only meant for a console; $PSStyle isn't there before PowerShell 7.2.
    private const string PowerShellPrelude = """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'
        if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }
        $global:LASTEXITCODE = 0

        """;

    // PowerShell exits with 1 when the last command failed, even when it is a native one that exited with another code, which is passed on instead.
    private const string PowerShellEpilogue = """
        $__terminal_manager_succeeded = $?
        if ($global:LASTEXITCODE) { exit $global:LASTEXITCODE }
        if (-not $__terminal_manager_succeeded) { exit 1 }

        """;

    /// <summary>The script run before each command, in the same scope, so what it defines can be used by the command, or <see langword="null"/> when there is none.</summary>
    public string? PrependScript { get; init; }

    /// <summary>The script run after each command, in the same scope, or <see langword="null"/> when there is none.</summary>
    public string? AppendScript { get; init; }

    /// <summary>
    /// What is put before a command when it is highlighted, so what <see cref="PrependScript"/> defines, or leaves open, is taken into account.
    /// </summary>
    [JsonIgnore]
    public string EditorPrefix => PrependScript is null or "" ? string.Empty : PrependScript + "\n";

    /// <summary>The executable of the client that runs the commands in the container, which is expected to be on the path.</summary>
    private protected abstract string FileName { get; }

    /// <summary>The arguments of the client that tell it to run what follows them in the container.</summary>
    private protected abstract IEnumerable<string> CreateExecArguments();

    /// <summary>
    /// The arguments run in the container for <paramref name="body"/>, which is the command with the scripts before and after it: the shell, or PowerShell,
    /// with a script that changes to the working folder and sets <paramref name="environmentVariables"/> first.
    /// </summary>
    private protected abstract IEnumerable<string> CreateShellArguments(string body, IReadOnlyDictionary<string, string> environmentVariables);

    /// <summary>Describes the process of the client that runs <paramref name="command"/> in the container. The redirection of its streams is set by the caller.</summary>
    public ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // the container may not take carriage returns as the end of a line, which text boxes end them with
        var body = ScriptWrapping.Wrap(PrependScript, command, AppendScript).ReplaceLineEndings("\n");

        // each argument is quoted for the command line as needed, so the script reaches the container as it is;
        // the working folder is in the container, so the client is run in one that is sure to exist here.
        var startInfo = new ProcessStartInfo(FileName) { WorkingDirectory = Path.GetTempPath() };
        foreach (var argument in (string[])[.. CreateExecArguments(), .. CreateShellArguments(body, ExpandedEnvironmentVariables)])
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Runs <paramref name="body"/> by <c>sh</c>, which is in nearly every Linux image, unlike other shells.</summary>
    private protected IEnumerable<string> RunByShell(string body, IReadOnlyDictionary<string, string> environmentVariables)
    {
        var script = new StringBuilder().AppendShellPreamble(WorkingFolder, environmentVariables).Append(body).Append('\n');
        return (string[])["sh", "-c", script.ToString()];
    }

    /// <summary>Runs <paramref name="body"/> by PowerShell (pwsh); the working folder not being there stops the script.</summary>
    private protected IEnumerable<string> RunByPowerShell(string body, IReadOnlyDictionary<string, string> environmentVariables)
    {
        var script = new StringBuilder(PowerShellPrelude).AppendPowerShellPreamble(WorkingFolder, environmentVariables).Append(body).Append('\n').Append(PowerShellEpilogue);
        return (string[])["pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command", script.ToString().ReplaceLineEndings("\n")];
    }
}
