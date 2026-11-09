using App.Domain;

namespace App.Services;

/// <summary>
/// Runs commands by the terminal of their project, in its working folder.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Runs <paramref name="command"/>, writing its output to <paramref name="output"/> as it is read, which is completed when the command ends,
    /// or couldn't be started. Canceling stops the process and all the processes it started.
    /// </summary>
    /// <returns>The exit code of the process.</returns>
    Task<int> RunAsync(Project project, string command, IOutputSink output, CancellationToken cancellationToken);
}
