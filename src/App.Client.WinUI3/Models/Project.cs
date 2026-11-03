using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A folder that commands are run in, with the terminal they are run by.
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
public abstract record class Project
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The folder commands are run in.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>The folder commands are run in, with the environment variables in <see cref="WorkingFolder"/> expanded.</summary>
    [JsonIgnore]
    public string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder);

    /// <summary>The environment variables commands are run with, in addition to those of the app, or in place of them by the same name, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>The environment variables commands are run with, with the environment variables in their values expanded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> ExpandedEnvironmentVariables =>
        (EnvironmentVariables ?? ReadOnlyDictionary<string, string>.Empty).ToDictionary(static pair => pair.Key, static pair => Environment.ExpandEnvironmentVariables(pair.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether commands are also run with the variables in the .env file in the working folder, which those in <see cref="EnvironmentVariables"/> replace by the same name.</summary>
    public bool LoadEnvFile { get; init; }

    /// <summary>
    /// The environment variables commands are run with: those in the .env file in the working folder, as it is when they are run, if <see cref="LoadEnvFile"/> is set,
    /// then those in <see cref="ExpandedEnvironmentVariables"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolveEnvironmentVariables()
    {
        if (!LoadEnvFile)
        {
            return ExpandedEnvironmentVariables;
        }

        var variables = new Dictionary<string, string>(DotEnvFile.Read(Path.Combine(ExpandedWorkingFolder, DotEnvFile.FileName)), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in ExpandedEnvironmentVariables)
        {
            variables[name] = value;
        }

        return variables;
    }

    /// <summary>The commands saved to the project, in the order they are shown, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyList<SavedScript>? Scripts { get; init; }

    /// <summary>The name of the terminal the commands are run by, as shown to the user.</summary>
    [JsonIgnore]
    public abstract string TerminalDisplayName { get; }

    /// <summary>The logo of the terminal the commands are run by, or <see langword="null"/> to show a generic terminal icon.</summary>
    [JsonIgnore]
    public abstract Uri? TerminalIconUri { get; }

    /// <summary>
    /// Describes the process that runs <paramref name="command"/>. Its working folder and the redirection of its streams are set by the caller.
    /// </summary>
    public abstract ProcessStartInfo CreateStartInfo(string command);

    /// <summary>
    /// What is written to the standard input of the process that runs <paramref name="command"/>, which is closed after it,
    /// or <see langword="null"/> to close it right away, as when the command is passed by <see cref="CreateStartInfo"/> alone.
    /// </summary>
    public virtual string? CreateStandardInput(string command) => null;
}
