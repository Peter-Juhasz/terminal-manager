using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A folder that commands are run in, with the terminal they are run by.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PowerShellProject), "pwsh")]
[JsonDerivedType(typeof(CmdProject), "cmd")]
public abstract record class Project
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The folder commands are run in.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>The folder commands are run in, with the environment variables in <see cref="WorkingFolder"/> expanded.</summary>
    [JsonIgnore]
    public string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder);

    /// <summary>The environment variables commands are run with, in addition to those of the app, or in place of them by the same name.</summary>
    // the source generated deserializer sets it to null when it is missing, as projects saved before it was added are.
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init => field = value ?? ReadOnlyDictionary<string, string>.Empty; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>The environment variables commands are run with, with the environment variables in their values expanded.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> ExpandedEnvironmentVariables =>
        EnvironmentVariables.ToDictionary(static pair => pair.Key, static pair => Environment.ExpandEnvironmentVariables(pair.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>The commands saved to the project, in the order they are shown.</summary>
    public IReadOnlyList<SavedScript> Scripts { get; init; } = Array.Empty<SavedScript>();

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
}
