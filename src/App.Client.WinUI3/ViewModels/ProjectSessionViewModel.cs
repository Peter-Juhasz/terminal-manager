using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A project, with the commands run in it since the app was started.
/// </summary>
public sealed partial class ProjectSessionViewModel(Project project, CommandRunner runner) : ObservableObject
{
    // How often the time spent by the running command is updated.
    private static readonly TimeSpan DurationRefreshInterval = TimeSpan.FromMilliseconds(100);

    // The commands that were run, so they can be recalled with the arrow keys; the index is that of the one recalled.
    private readonly List<string> _history = [];
    private int _historyIndex;

    private CancellationTokenSource? _stopSource;

    [ObservableProperty]
    public partial Project Project { get; set; } = project;

    public ObservableCollection<CommandEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial bool HasEntries { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial string Input { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand), nameof(ClearCommand))]
    public partial bool IsRunning { get; private set; }

    /// <summary>Raised when a command is started, so it can be scrolled into view.</summary>
    public event EventHandler<CommandEntryViewModel>? CommandStarted;

    private bool CanRun => !IsRunning && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var command = Input.Trim();
        Input = string.Empty;

        _history.Remove(command);
        _history.Add(command);
        _historyIndex = _history.Count;

        var entry = new CommandEntryViewModel(command);
        Entries.Add(entry);
        HasEntries = true;
        IsRunning = true;
        CommandStarted?.Invoke(this, entry);

        using var stopSource = _stopSource = new CancellationTokenSource();
        using var tickSource = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var ticking = TickAsync(entry, stopwatch, tickSource.Token);

        var output = Channel.CreateUnbounded<OutputChunk>(new UnboundedChannelOptions { SingleReader = true });
        var project = Project;
        var run = Task.Run(() => runner.RunAsync(project, command, output.Writer, stopSource.Token), CancellationToken.None);

        try
        {
            // Everything read since the last update is shown at once, so a command that writes a lot doesn't flood the UI thread.
            while (await output.Reader.WaitToReadAsync(CancellationToken.None))
            {
                while (output.Reader.TryRead(out var chunk))
                {
                    entry.Append(chunk);
                }
            }

            var exitCode = await run;
            entry.Complete(exitCode, stopSource.IsCancellationRequested, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            entry.Append(new OutputChunk(OutputStream.StandardError, ex.Message));
            entry.Complete(null, wasStopped: false, stopwatch.Elapsed);
        }
        finally
        {
            await tickSource.CancelAsync();
            await ticking;
            _stopSource = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _stopSource?.Cancel();

    private bool CanClear => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Entries.Clear();
        HasEntries = false;
    }

    /// <summary>Puts the command run before the one recalled into the input, if there is one.</summary>
    public bool RecallPrevious()
    {
        if (_historyIndex <= 0)
        {
            return false;
        }

        Input = _history[--_historyIndex];
        return true;
    }

    /// <summary>Puts the command run after the one recalled into the input, or clears it after the last one.</summary>
    public bool RecallNext()
    {
        if (_historyIndex >= _history.Count)
        {
            return false;
        }

        _historyIndex++;
        Input = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
        return true;
    }

    private static async Task TickAsync(CommandEntryViewModel entry, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(DurationRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                entry.Duration = stopwatch.Elapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
