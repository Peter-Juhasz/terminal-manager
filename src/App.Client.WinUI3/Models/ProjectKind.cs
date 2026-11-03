namespace App.Client.WinUI3.Models;

/// <summary>
/// A kind of <see cref="Project"/> that can be created, by the terminal its commands are run by.
/// </summary>
public sealed record class ProjectKind(string DisplayName, Func<Guid, string, string, Project> Create)
{
    public static IReadOnlyList<ProjectKind> All { get; } = (ProjectKind[])
    [
        new("Command Prompt (cmd)", static (id, displayName, workingFolder) => new CmdProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new("PowerShell (pwsh)", static (id, displayName, workingFolder) => new PowerShellProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new("Windows PowerShell (powershell)", static (id, displayName, workingFolder) => new WindowsPowerShellProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new("Developer Command Prompt for VS (cmd)", static (id, displayName, workingFolder) => new DeveloperCmdProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new("Developer PowerShell for VS (pwsh)", static (id, displayName, workingFolder) => new DeveloperPowerShellProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
        new(".NET app (dotnet run)", static (id, displayName, workingFolder) => new DotNetRunProject
        {
            Id = id,
            DisplayName = displayName,
            WorkingFolder = workingFolder,
        }),
    ];
}
