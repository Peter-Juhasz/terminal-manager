using App.Domain;
using App.Services;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs commands as an administrator, by the <see cref="ElevatedHost"/>. It is started the first time a command is run, which the user is asked to allow,
/// and runs all the commands after, until the app closes; it is started again if it was not allowed, or has exited since.
/// When the app itself runs as an administrator, or the commands aren't run on this machine, they are run by <paramref name="runner"/> instead.
/// </summary>
public sealed class ElevatedCommandRunner(CommandRunner runner) : ICommandRunner
{
    private readonly Lock _lock = new();
    private Task<ElevationClient>? _starting;

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException">Canceled before the host was started.</exception>
    public async Task<int> RunAsync(Project project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);

        // commands run elsewhere don't depend on the rights of the app, such as those run on an Azure virtual machine, which are run as an administrator there anyway
        if (Environment.IsPrivilegedProcess || project is not LocalProject local)
        {
            return await runner.RunAsync(project, command, output, cancellationToken).ConfigureAwait(false);
        }

        ElevationClient client;
        try
        {
            client = await GetClientAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            output.Complete();
            throw;
        }

        // Expanded here, so they are the variables of the user, even if the host is run by another one, who allowed it with their own credentials;
        // the .env file is read here too, as that user may not be allowed to read it.
        var expanded = local with { WorkingFolder = local.ExpandedWorkingFolder, EnvironmentVariables = local.ResolveEnvironmentVariables(), LoadEnvFile = false };
        return await client.RunAsync(expanded, command, output, cancellationToken).ConfigureAwait(false);
    }

    // Shared by the commands run while it is started, so the user is only asked once; started on a thread of its own, as it blocks until the user answered.
    private Task<ElevationClient> GetClientAsync()
    {
        lock (_lock)
        {
            if (_starting is null or { IsFaulted: true } or { IsCanceled: true } or { IsCompletedSuccessfully: true, Result.IsConnected: false })
            {
                _starting = Task.Run(() => ElevatedHost.StartAsync(CancellationToken.None), CancellationToken.None);
            }

            return _starting;
        }
    }
}
