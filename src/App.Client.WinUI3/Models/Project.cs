using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A folder that commands are run in, with the terminal they are run by.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PowerShellProject), "pwsh")]
public abstract record class Project
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The folder commands are run in.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>The commands saved to the project, in the order they are shown.</summary>
    public IReadOnlyList<SavedScript> Scripts { get; init; } = Array.Empty<SavedScript>();

    /// <summary>The name of the terminal the commands are run by, as shown to the user.</summary>
    [JsonIgnore]
    public abstract string TerminalDisplayName { get; }

    /// <summary>
    /// Describes the process that runs <paramref name="command"/>. Its working folder and the redirection of its streams are set by the caller.
    /// </summary>
    public abstract ProcessStartInfo CreateStartInfo(string command);
}
