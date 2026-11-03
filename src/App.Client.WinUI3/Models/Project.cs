using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A folder that commands are run in, with the terminal they are run by: on this machine for a <see cref="LocalProject"/>,
/// or elsewhere, such as on an Azure virtual machine for an <see cref="AzureVmProject"/>.
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
[JsonDerivedType(typeof(AzureVmShellProject), "azvmshell")]
[JsonDerivedType(typeof(AzureVmPowerShellProject), "azvmpwsh")]
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

    /// <summary>The commands saved to the project, in the order they are shown, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyList<SavedScript>? Scripts { get; init; }

    /// <summary>The name of the terminal the commands are run by, as shown to the user.</summary>
    [JsonIgnore]
    public abstract string TerminalDisplayName { get; }

    /// <summary>The logo of the terminal the commands are run by, or <see langword="null"/> to show a generic terminal icon.</summary>
    [JsonIgnore]
    public abstract Uri? TerminalIconUri { get; }

    /// <summary>Where the working folder is, as shown to the user, such as the folder itself, or the machine it is on and the folder.</summary>
    [JsonIgnore]
    public virtual string LocationDisplayName => WorkingFolder;
}
