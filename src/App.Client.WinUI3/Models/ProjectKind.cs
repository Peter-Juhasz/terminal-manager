namespace App.Client.WinUI3.Models;

/// <summary>
/// A kind of <see cref="Project"/> that can be created, by the terminal its commands are run by.
/// </summary>
public sealed record class ProjectKind(string DisplayName, Func<Guid, string, string, Project> Create)
{
    public static IReadOnlyList<ProjectKind> All { get; } = (ProjectKind[])
    [
        new("PowerShell (pwsh)", static (id, displayName, workingFolder) => new PowerShellProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new("Command Prompt (cmd)", static (id, displayName, workingFolder) => new CmdProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
    ];
}
