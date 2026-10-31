using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A project, with the commands run in it since the app was started.
/// </summary>
public sealed partial class ProjectSessionViewModel(Project project, CommandRunner runner, ProjectStore store) : ObservableObject
{
    // How often the time spent by the running command is updated.
    private static readonly TimeSpan DurationRefreshInterval = TimeSpan.FromSeconds(1);

    // How often more of the output of a running command is shown, as the text block showing it lays all of it out again each time.
    private static readonly TimeSpan OutputRefreshInterval = TimeSpan.FromMilliseconds(50);

    // The commands that were run, so they can be recalled with the arrow keys; the index is that of the one recalled.
    private readonly List<string> _history = [];
    private int _historyIndex;

    [ObservableProperty]
    public partial Project Project { get; set; } = project;

    public ObservableCollection<CommandEntryViewModel> Entries { get; } = [];

    public ObservableCollection<SavedScript> Scripts { get; } = new(project.Scripts);

    [ObservableProperty]
    public partial bool HasScripts { get; private set; } = project.Scripts.Count > 0;

    [ObservableProperty]
    public partial bool HasEntries { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial string Input { get; set; } = string.Empty;

    // The commands that are running, in the order they were started.
    private readonly List<CommandEntryViewModel> _running = [];

    // The command whose progress is shown on the project, when it is the only one running.
    private CommandEntryViewModel? SoleRunning => _running.Count == 1 ? _running[0] : null;

    /// <summary>The commands that are running, in the order they were started.</summary>
    public IReadOnlyList<CommandEntryViewModel> RunningEntries => _running;

    /// <summary>The number of commands that are running; any number of them can run at the same time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(RunningBadgeValue), nameof(IsRunningBadgeVisible))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial int RunningCount { get; private set; }

    public bool IsRunning => RunningCount > 0;

    /// <summary>The number shown on the badge of the project, or -1 for a dot when only one command is running.</summary>
    public int RunningBadgeValue => RunningCount > 1 ? RunningCount : -1;

    /// <summary>Whether the badge is shown, which it is while commands are running, unless the progress of the only one is shown instead.</summary>
    public bool IsRunningBadgeVisible => IsRunning && !HasRunningProgress;

    /// <summary>Whether the only command running reports its progress.</summary>
    public bool HasRunningProgress => SoleRunning is { IsProgressVisible: true };

    public ProgressState RunningProgressState => SoleRunning?.ProgressState ?? ProgressState.None;

    public double RunningProgressValue => SoleRunning?.ProgressValue ?? 0;

    public bool IsRunningProgressIndeterminate => SoleRunning is { IsProgressIndeterminate: true };

    /// <summary>Raised when a command is started or ends, or one that is running reports its progress.</summary>
    public event EventHandler? RunningProgressChanged;

    /// <summary>Raised when a command has ended, however it did.</summary>
    public event EventHandler<CommandEntryViewModel>? CommandCompleted;

    /// <summary>Raised when a command is started, so it can be scrolled into view.</summary>
    public event EventHandler<CommandEntryViewModel>? CommandStarted;

    private bool CanRun => !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanRun), AllowConcurrentExecutions = true)]
    private Task RunAsync()
    {
        var command = Input.Trim();
        Input = string.Empty;
        return ExecuteAsync(command);
    }

    private bool CanRunScript(SavedScript? script) => script is not null;

    /// <summary>Runs <paramref name="script"/>, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript), AllowConcurrentExecutions = true)]
    private Task RunScriptAsync(SavedScript? script) => script is null ? Task.CompletedTask : ExecuteAsync(script.Command);

    /// <summary>Adds <paramref name="script"/>, or replaces the one with the same id, and saves the project.</summary>
    public Task SaveScriptAsync(SavedScript script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);

        var index = Scripts.Select(static other => other.Id).ToList().IndexOf(script.Id);
        if (index >= 0)
        {
            Scripts[index] = script;
        }
        else
        {
            Scripts.Add(script);
        }

        return SaveScriptsAsync(cancellationToken);
    }

    public Task DeleteScriptAsync(SavedScript script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);

        Scripts.Remove(script);
        return SaveScriptsAsync(cancellationToken);
    }

    private Task SaveScriptsAsync(CancellationToken cancellationToken)
    {
        HasScripts = Scripts.Count > 0;
        Project = Project with { Scripts = Scripts.ToArray() };
        return store.UpdateAsync(Project, cancellationToken);
    }

    private async Task ExecuteAsync(string command)
    {
        _history.Remove(command);
        _history.Add(command);
        _historyIndex = _history.Count;

        using var stopSource = new CancellationTokenSource();
        var entry = new CommandEntryViewModel(command, stopSource.Cancel, () => ExecuteAsync(command));
        Entries.Add(entry);
        HasEntries = true;
        _running.Add(entry);
        entry.PropertyChanged += OnRunningChanged;
        UpdateRunning();
        CommandStarted?.Invoke(this, entry);

        using var tickSource = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var ticking = TickAsync(entry, stopwatch, tickSource.Token);

        var output = new OutputBuffer();
        var project = Project;
        var run = Task.Run(() => runner.RunAsync(project, command, output, stopSource.Token), CancellationToken.None);

        try
        {
            // Everything written since the last refresh is shown at once, so a command that writes a lot doesn't flood the UI thread.
            while (await output.WaitToReadAsync(CancellationToken.None))
            {
                entry.Append(output.Read());
                await WaitToRefreshAsync(output.Completion);
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
            entry.PropertyChanged -= OnRunningChanged;
            _running.Remove(entry);
            UpdateRunning();
            CommandCompleted?.Invoke(this, entry);
        }
    }

    private void UpdateRunning()
    {
        RunningCount = _running.Count;
        NotifyRunningProgressChanged();
    }

    private void OnRunningChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommandEntryViewModel.ProgressState) or nameof(CommandEntryViewModel.ProgressValue) or nameof(CommandEntryViewModel.IsProgressVisible))
        {
            NotifyRunningProgressChanged();
        }
    }

    private void NotifyRunningProgressChanged()
    {
        OnPropertyChanged(nameof(HasRunningProgress));
        OnPropertyChanged(nameof(IsRunningBadgeVisible));
        OnPropertyChanged(nameof(RunningProgressState));
        OnPropertyChanged(nameof(RunningProgressValue));
        OnPropertyChanged(nameof(IsRunningProgressIndeterminate));
        RunningProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the command that was started last of those still running, if there is one.</summary>
    public bool StopLatest()
    {
        if (Entries.LastOrDefault(static entry => entry.IsRunning) is not { } entry)
        {
            return false;
        }

        entry.StopCommand.Execute(null);
        return true;
    }

    /// <summary>Stops all the commands that are running.</summary>
    public void StopAll()
    {
        foreach (var entry in Entries.Where(static entry => entry.IsRunning).ToList())
        {
            entry.StopCommand.Execute(null);
        }
    }

    private bool CanClear => Entries.Any(static entry => !entry.IsRunning);

    /// <summary>Removes the commands that have ended; those still running are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        foreach (var entry in Entries.Where(static entry => !entry.IsRunning).ToList())
        {
            Entries.Remove(entry);
        }

        HasEntries = Entries.Count > 0;
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

    // Waits until more of the output can be shown, or it is complete, so its end is shown right away.
    // When laying out what was shown kept the UI thread busy past the interval, it is left as long to everything else,
    // so the app keeps responding to input however much output there is to lay out.
    private static async Task WaitToRefreshAsync(Task completion)
    {
        var started = Stopwatch.GetTimestamp();
        await Task.WhenAny(Task.Delay(OutputRefreshInterval, CancellationToken.None), completion);

        // Waking up late by less than this is put down to the resolution of the timer.
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed - OutputRefreshInterval > OutputRefreshInterval / 2)
        {
            await Task.WhenAny(Task.Delay(elapsed, CancellationToken.None), completion);
        }
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
