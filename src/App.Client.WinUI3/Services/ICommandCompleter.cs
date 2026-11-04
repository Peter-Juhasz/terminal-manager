namespace App.Client.WinUI3.Services;

/// <summary>Completes commands the way the shell that runs them would.</summary>
public interface ICommandCompleter
{
    /// <summary>Gets completion ready for <paramref name="workingFolder"/>, so the first completion is quick too.</summary>
    void Prepare(string workingFolder);

    /// <summary>
    /// Completes the text before <paramref name="cursor"/> in <paramref name="input"/>, as if <paramref name="prefix"/> was before it, so what that defines is offered too;
    /// the completion is of <paramref name="input"/> alone, and <see langword="null"/> when it can't be completed, or would replace text of <paramref name="prefix"/>.
    /// </summary>
    Task<CommandCompletion?> CompleteAsync(string workingFolder, string prefix, string input, int cursor, CancellationToken cancellationToken);
}
