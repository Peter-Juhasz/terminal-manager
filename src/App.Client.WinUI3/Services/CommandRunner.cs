using App.Domain;
using App.Extensions.Azure;
using App.Extensions.Telnet;
using App.Services;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs commands by the terminal of their project, in its working folder, with the rights of the app: by a process on this machine for a <see cref="LocalProject"/>,
/// by <see cref="AzureVmCommandRunner"/> for an <see cref="AzureVmProject"/>, by a process of the client of the container on this machine for a <see cref="ContainerProject"/>,
/// by a process of PowerShell on this machine for a <see cref="PowerShellRemotingProject"/>, or by <see cref="TelnetCommandRunner"/> for a <see cref="TelnetProject"/>,
/// logged in by the password <paramref name="credentials"/> keep, when there are any.
/// </summary>
public sealed class CommandRunner(ICredentialStore? credentials = null) : ICommandRunner
{
    /// <inheritdoc/>
    public async Task<int> RunAsync(Project project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            return project switch
            {
                LocalProject local => await RunLocalAsync(local, command, output, cancellationToken).ConfigureAwait(false),
                AzureVmProject azureVm => await AzureVmCommandRunner.RunAsync(azureVm, command, output, cancellationToken).ConfigureAwait(false),

                // the environment variables are set by the script, as those of the client don't reach the container
                ContainerProject container => await ProcessRunner.RunAsync(container.CreateStartInfo(command), input: null, output, cancellationToken).ConfigureAwait(false),

                // the environment variables are set by the script too, as those of PowerShell here don't reach the computer
                PowerShellRemotingProject remoting => await ProcessRunner.RunAsync(remoting.CreateStartInfo(command), input: null, output, cancellationToken).ConfigureAwait(false),
                TelnetProject telnet => await TelnetCommandRunner.RunAsync(telnet, command, ReadPassword(telnet), output, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Commands of '{project.TerminalDisplayName}' projects can't be run."),
            };
        }
        finally
        {
            output.Complete();
        }
    }

    // only a user is logged in by a password
    private string? ReadPassword(TelnetProject project) => project.User is { Length: > 0 } ? credentials?.Read(project.CredentialTarget) : null;

    private static Task<int> RunLocalAsync(LocalProject project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        var startInfo = project.CreateStartInfo(command);
        var input = project.CreateStandardInput(command);
        // a folder of Linux can't be started in, so the process is started where the project tells, and the command changes to it
        if (project.IsWorkingFolderOnWindows)
        {
            startInfo.WorkingDirectory = project.ExpandedWorkingFolder;
        }

        foreach (var (name, value) in project.ResolveEnvironmentVariables())
        {
            startInfo.Environment[name] = value;
        }

        return ProcessRunner.RunAsync(startInfo, input, output, cancellationToken);
    }
}
