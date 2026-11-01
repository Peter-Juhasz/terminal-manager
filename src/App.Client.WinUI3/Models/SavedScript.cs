namespace App.Client.WinUI3.Models;

/// <summary>
/// A command saved to a project under a name, so it can be run with a single click.
/// </summary>
public sealed record class SavedScript
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Command { get; init; }

    /// <summary>Whether <see cref="Command"/> has variables, written as <c>{{ name }}</c>, which are asked for each time it is run.</summary>
    public bool IsTemplate { get; init; }
}
