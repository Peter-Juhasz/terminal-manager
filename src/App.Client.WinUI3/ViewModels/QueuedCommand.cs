namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A command waiting to be run once the commands running have ended, as an administrator if <paramref name="isElevated"/>.
/// </summary>
/// <remarks>A class rather than a record, so the same command queued twice can be told apart when one of them is cancelled.</remarks>
public sealed class QueuedCommand(string command, bool isElevated)
{
    public string Command { get; } = command;

    public bool IsElevated { get; } = isElevated;
}
