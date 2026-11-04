using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs commands by the terminal of their project, in its working folder, with the rights of the app: by a process on this machine for a <see cref="LocalProject"/>,
/// by <see cref="AzureVmCommandRunner"/> for an <see cref="AzureVmProject"/>, or by a process of the client of the container on this machine for a <see cref="ContainerProject"/>.
/// </summary>
public sealed class CommandRunner : ICommandRunner
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
                _ => throw new InvalidOperationException($"Commands of '{project.TerminalDisplayName}' projects can't be run."),
            };
        }
        finally
        {
            output.Complete();
        }
    }

    private static Task<int> RunLocalAsync(LocalProject project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        var startInfo = project.CreateStartInfo(command);
        var input = project.CreateStandardInput(command);
        startInfo.WorkingDirectory = project.ExpandedWorkingFolder;
        foreach (var (name, value) in project.ResolveEnvironmentVariables())
        {
            startInfo.Environment[name] = value;
        }

        return ProcessRunner.RunAsync(startInfo, input, output, cancellationToken);
    }
}
