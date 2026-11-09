using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A folder that commands are run in, with the terminal they are run by: on this machine for a <see cref="LocalProject"/>,
/// or elsewhere, such as on an Azure virtual machine for an <see cref="AzureVmProject"/>, in a container for a <see cref="ContainerProject"/>, or on another computer by PowerShell remoting for a <see cref="PowerShellRemotingProject"/>, or over Telnet for a <see cref="TelnetProject"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PowerShellProject), "pwsh")]
[JsonDerivedType(typeof(WindowsPowerShellProject), "powershell")]
[JsonDerivedType(typeof(DeveloperPowerShellProject), "devpwsh")]
[JsonDerivedType(typeof(CmdProject), "cmd")]
[JsonDerivedType(typeof(DeveloperCmdProject), "devcmd")]
[JsonDerivedType(typeof(DotNetRunProject), "dotnetrun")]
[JsonDerivedType(typeof(DotNetFileProject), "dotnetfile")]
[JsonDerivedType(typeof(PythonProject), "python")]
[JsonDerivedType(typeof(NodeProject), "node")]
[JsonDerivedType(typeof(WslProject), "wsl")]
[JsonDerivedType(typeof(AzureVmShellProject), "azvmshell")]
[JsonDerivedType(typeof(AzureVmPowerShellProject), "azvmpwsh")]
[JsonDerivedType(typeof(DockerShellProject), "dockersh")]
[JsonDerivedType(typeof(DockerPowerShellProject), "dockerpwsh")]
[JsonDerivedType(typeof(KubectlShellProject), "kubectlsh")]
[JsonDerivedType(typeof(KubectlPowerShellProject), "kubectlpwsh")]
[JsonDerivedType(typeof(PowerShellRemotingProject), "pwshremote")]
[JsonDerivedType(typeof(TelnetProject), "telnet")]
public abstract record class Project
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The folder commands are run in, on the machine they are run on.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>The environment variables commands are run with, in addition to those they would be run with otherwise, or in place of them by the same name, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>The environment variables commands are run with, with the environment variables of this machine in their values expanded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> ExpandedEnvironmentVariables =>
        (EnvironmentVariables ?? ReadOnlyDictionary<string, string>.Empty).ToDictionary(static pair => pair.Key, static pair => Environment.ExpandEnvironmentVariables(pair.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the commands that were run, with what they wrote, are saved, and shown again the first time the project is selected after the app is started.</summary>
    public bool PersistHistory { get; init; }

    /// <summary>Whether the output of a command is collapsed while it runs, and is only expanded once it failed.</summary>
    public bool CollapseSucceededOutput { get; init; }

    /// <summary>The commands saved to the project, in the order they are shown, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyList<Script>? Scripts { get; init; }

    /// <summary>The name of the terminal the commands are run by, as shown to the user.</summary>
    [JsonIgnore]
    public abstract string TerminalDisplayName { get; }

    /// <summary>The logo of the terminal the commands are run by, or <see langword="null"/> to show a generic terminal icon.</summary>
    [JsonIgnore]
    public abstract Uri? TerminalIconUri { get; }

    /// <summary>Where the working folder is, as shown to the user, such as the folder itself, or the machine it is on and the folder.</summary>
    [JsonIgnore]
    public virtual string LocationDisplayName => WorkingFolder;

    /// <summary>The language commands are written in, as they are passed to the terminal.</summary>
    [JsonIgnore]
    public abstract CommandLanguage Language { get; }

    /// <summary>
    /// Whether commands are code passed as a whole, so <see cref="PrependScript"/> and <see cref="AppendScript"/> can be put before and after them;
    /// they can't be for a command line, as for cmd, or for the arguments of an app.
    /// </summary>
    [JsonIgnore]
    public bool HasPrependAndAppend => Language is not (CommandLanguage.Cmd or CommandLanguage.Arguments);

    /// <summary>
    /// The code run before each command, in the same script or scope, so what it defines can be used by the command, or <see langword="null"/> when there is none.
    /// Only used when <see cref="HasPrependAndAppend"/> is set.
    /// </summary>
    public string? PrependScript { get; init; }

    /// <summary>The code run after each command, in the same script or scope, or <see langword="null"/> when there is none. Only used when <see cref="HasPrependAndAppend"/> is set.</summary>
    public string? AppendScript { get; init; }

    /// <summary>
    /// What is put before a command when it is completed, parsed or highlighted, so what <see cref="PrependScript"/> defines, or leaves open, is taken into account;
    /// positions in the command are this much further in the script.
    /// </summary>
    [JsonIgnore]
    public string EditorPrefix => PrependScript is null or "" ? string.Empty : PrependScript + "\n";

    /// <summary>The code that is run for <paramref name="command"/>, with <see cref="PrependScript"/> before it and <see cref="AppendScript"/> after it, each on lines of their own.</summary>
    public string Wrap(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AppendScript is null or "" ? EditorPrefix + command : EditorPrefix + command + "\n" + AppendScript;
    }
}
