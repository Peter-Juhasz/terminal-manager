namespace App.Client.WinUI3.Models;

/// <summary>
/// A kind of <see cref="Project"/> that can be created, by the terminal its commands are run by.
/// </summary>
/// <param name="DisplayName">The name of the terminal, with the command it is run by.</param>
/// <param name="Description">What the commands of a project of the kind are, and where they are run, in a sentence.</param>
/// <param name="Create">Creates a project of the kind, from its id, name and working folder.</param>
public sealed record class ProjectKind(string DisplayName, string Description, Func<Guid, string, string, Project> Create)
{
    /// <summary>The logo of the terminal, or <see langword="null"/> for a generic terminal icon.</summary>
    public Uri? IconUri => Sample.TerminalIconUri;

    /// <summary>Whether the commands are run somewhere other than this machine, such as a virtual machine, a container or another computer.</summary>
    public bool IsRemote => Sample is not LocalProject;

    /// <summary>A project of the kind, which tells what it is like.</summary>
    // told by what it creates, as that of a project being edited creates the project itself.
    public Project Sample => Create(Guid.Empty, string.Empty, string.Empty);

    public static IReadOnlyList<ProjectKind> All { get; } = (ProjectKind[])
    [
        new(
            DisplayName: "Command Prompt (cmd)",
            Description: "Commands are run by cmd on this machine.",
            Create: static (id, displayName, workingFolder) => new CmdProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "PowerShell (pwsh)",
            Description: "Commands are run by PowerShell 7 on this machine.",
            Create: static (id, displayName, workingFolder) => new PowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Windows PowerShell (powershell)",
            Description: "Commands are run by Windows PowerShell 5.1, which comes with Windows.",
            Create: static (id, displayName, workingFolder) => new WindowsPowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Developer Command Prompt for VS (cmd)",
            Description: "Commands are run by cmd, with the build tools of Visual Studio.",
            Create: static (id, displayName, workingFolder) => new DeveloperCmdProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Developer PowerShell for VS (pwsh)",
            Description: "Commands are run by PowerShell, with the build tools of Visual Studio.",
            Create: static (id, displayName, workingFolder) => new DeveloperPowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: ".NET app (dotnet run)",
            Description: "Commands are passed as arguments to the app in the folder.",
            Create: static (id, displayName, workingFolder) => new DotNetRunProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "C# file-based app (dotnet run -)",
            Description: "Commands are C# code, each run as a file-based app.",
            Create: static (id, displayName, workingFolder) => new DotNetFileProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Python (python -)",
            Description: "Commands are Python code, each run as a script.",
            Create: static (id, displayName, workingFolder) => new PythonProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Node.js (node -)",
            Description: "Commands are JavaScript code, each run as a script.",
            Create: static (id, displayName, workingFolder) => new NodeProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "WSL, bash (wsl.exe)",
            Description: "Commands are run by bash in a Linux distribution of the Windows Subsystem for Linux.",
            Create: static (id, displayName, workingFolder) => new WslProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Azure VM, Linux shell (az vm run-command)",
            Description: "Commands are run by the shell of a Linux virtual machine in Azure.",
            Create: static (id, displayName, workingFolder) => new AzureVmShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Azure VM, Windows PowerShell (az vm run-command)",
            Description: "Commands are run by PowerShell on a Windows virtual machine in Azure.",
            Create: static (id, displayName, workingFolder) => new AzureVmPowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Docker container, shell (docker exec)",
            Description: "Commands are run by sh in a running Docker container.",
            Create: static (id, displayName, workingFolder) => new DockerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Docker container, PowerShell (docker exec)",
            Description: "Commands are run by PowerShell in a running Docker container.",
            Create: static (id, displayName, workingFolder) => new DockerPowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Kubernetes pod, shell (kubectl exec)",
            Description: "Commands are run by sh in a container of a Kubernetes pod.",
            Create: static (id, displayName, workingFolder) => new KubectlShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "Kubernetes pod, PowerShell (kubectl exec)",
            Description: "Commands are run by PowerShell in a container of a Kubernetes pod.",
            Create: static (id, displayName, workingFolder) => new KubectlPowerShellProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
        new(
            DisplayName: "PowerShell remoting (Invoke-Command)",
            Description: "Commands are run by PowerShell on another computer, over a remote session.",
            Create: static (id, displayName, workingFolder) => new PowerShellRemotingProject
            {
                Id = id,
                DisplayName = displayName,
                WorkingFolder = workingFolder,
            }),
    ];
}
