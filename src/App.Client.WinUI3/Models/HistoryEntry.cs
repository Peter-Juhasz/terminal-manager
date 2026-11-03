using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A command that was run in a project, with what it wrote, and how it ended, as it is saved to be shown again after the app is started.
/// </summary>
/// <param name="ExitCode">The exit code, or <see langword="null"/> when it was stopped before it ended, or couldn't be started.</param>
public sealed record class HistoryEntry(
    string Command,
    bool IsElevated,
    DateTimeOffset StartedAt,
    IReadOnlyList<OutputChunk>? Output,
    int? ExitCode,
    bool WasStopped,
    TimeSpan Duration,
    bool IsPinned);
