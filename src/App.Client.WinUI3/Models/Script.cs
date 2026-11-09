using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A command with a name, so it can be run with a single click: saved to a project, or read from a file in its working folder, such as the scripts of a package.json file.
/// </summary>
public sealed record class Script
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Command { get; init; }

    /// <summary>Whether <see cref="Command"/> has variables, written as <c>{{ name }}</c>, which are asked for each time it is run.</summary>
    public bool IsTemplate { get; init; }

    /// <summary>What is shown under the name in place of <see cref="Command"/>, such as what a script of a package.json file runs, or <see langword="null"/> to show the command.</summary>
    public string? Description { get; init; }

    /// <summary>What is shown under the name.</summary>
    [JsonIgnore]
    public string Summary => Description ?? Command;
}
