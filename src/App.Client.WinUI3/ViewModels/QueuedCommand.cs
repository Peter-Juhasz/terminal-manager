namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A command waiting to be run once the commands running have ended.
/// </summary>
/// <remarks>A class rather than a record, so the same command queued twice can be told apart when one of them is cancelled.</remarks>
public sealed class QueuedCommand(string command)
{
    public string Command { get; } = command;
}
