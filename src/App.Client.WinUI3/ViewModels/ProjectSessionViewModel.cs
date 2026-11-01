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
public sealed partial class ProjectSessionViewModel(Project project, CommandRunner runner, ElevatedCommandRunner elevatedRunner, PowerShellCompleter completer, PowerShellHighlighter highlighter, ProjectStore store) : ObservableObject
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
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(RunElevatedCommand), nameof(EnqueueCommand))]
    public partial string Input { get; set; } = string.Empty;

    /// <summary>The commands waiting to be run, one at a time, each once all the commands running have ended.</summary>
    public ObservableCollection<QueuedCommand> Queue { get; } = [];

    [ObservableProperty]
    public partial bool HasQueue { get; private set; }

    // The commands that are running, in the order they were started.
    private readonly List<CommandEntryViewModel> _running = [];

    // The command whose progress is shown on the project, when it is the only one running.
    private CommandEntryViewModel? SoleRunning => _running.Count == 1 ? _running[0] : null;

    /// <summary>The commands that are running, in the order they were started.</summary>
    public IReadOnlyList<CommandEntryViewModel> RunningEntries => _running;

    /// <summary>The number of commands that are running; any number of them can run at the same time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(RunningBadgeValue), nameof(IsRunningBadgeVisible))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand), nameof(ClearUnpinnedCommand))]
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
    private Task RunAsync() => RunInputAsync(isElevated: false);

    /// <summary>Runs what was typed in the input as an administrator, which the user is asked to allow the first time.</summary>
    [RelayCommand(CanExecute = nameof(CanRun), AllowConcurrentExecutions = true)]
    private Task RunElevatedAsync() => RunInputAsync(isElevated: true);

    private Task RunInputAsync(bool isElevated)
    {
        var command = Input.Trim();
        Input = string.Empty;
        return ExecuteAsync(command, isElevated);
    }

    private bool CanRunScript(SavedScript? script) => script is not null;

    /// <summary>Runs <paramref name="script"/>, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript), AllowConcurrentExecutions = true)]
    private Task RunScriptAsync(SavedScript? script) => script is null ? Task.CompletedTask : ExecuteAsync(script.Command, isElevated: false);

    /// <summary>Runs <paramref name="script"/> as an administrator, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript), AllowConcurrentExecutions = true)]
    private Task RunScriptElevatedAsync(SavedScript? script) => script is null ? Task.CompletedTask : ExecuteAsync(script.Command, isElevated: true);

    /// <summary>Queues what was typed in the input, to be run once the commands running have ended, or right away if none are.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Enqueue()
    {
        var command = Input.Trim();
        Input = string.Empty;
        AddToQueue(command, isElevated: false);
    }

    /// <summary>Queues <paramref name="script"/>, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript))]
    private void EnqueueScript(SavedScript? script)
    {
        if (script is null)
        {
            return;
        }

        AddToQueue(script.Command, isElevated: false);
    }

    /// <summary>Queues <paramref name="script"/> to be run as an administrator, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript))]
    private void EnqueueScriptElevated(SavedScript? script)
    {
        if (script is null)
        {
            return;
        }

        AddToQueue(script.Command, isElevated: true);
    }

    private void AddToQueue(string command, bool isElevated)
    {
        Queue.Add(new(command, isElevated));
        HasQueue = true;
        RunNextQueued();
    }

    [RelayCommand]
    private void CancelQueued(QueuedCommand? queued)
    {
        if (queued is null)
        {
            return;
        }

        Queue.Remove(queued);
        HasQueue = Queue.Count > 0;
    }

    // Starts the first command of the queue, unless there are commands running, in which case it is started once they have all ended.
    private void RunNextQueued()
    {
        if (_running.Count > 0 || Queue.Count == 0)
        {
            return;
        }

        var next = Queue[0];
        Queue.RemoveAt(0);
        HasQueue = Queue.Count > 0;

        // it is added to those running before this returns, so the rest of the queue waits for it
        _ = ExecuteAsync(next.Command, next.IsElevated);
    }

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

    private async Task ExecuteAsync(string command, bool isElevated)
    {
        _history.Remove(command);
        _history.Add(command);
        _historyIndex = _history.Count;

        using var stopSource = new CancellationTokenSource();
        var entry = new CommandEntryViewModel(command, isElevated, stopSource.Cancel, () => ExecuteAsync(command, isElevated));
        Entries.Add(entry);
        HasEntries = true;
        entry.PropertyChanged += OnEntryChanged;
        _running.Add(entry);
        entry.PropertyChanged += OnRunningChanged;
        UpdateRunning();
        CommandStarted?.Invoke(this, entry);

        // the project as it is when the command is started, which it is run by, whatever it is changed to while it runs
        var project = Project;
        if (project is PowerShellProject powerShell)
        {
            _ = HighlightAsync(entry, powerShell);
        }

        using var tickSource = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var ticking = TickAsync(entry, stopwatch, tickSource.Token);

        var output = new OutputBuffer();
        ICommandRunner selected = isElevated ? elevatedRunner : runner;
        var run = Task.Run(() => selected.RunAsync(project, command, output, stopSource.Token), CancellationToken.None);

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
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
            // stopped while the user was asked to allow it to run as an administrator
            entry.Complete(null, wasStopped: true, stopwatch.Elapsed);
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
            RunNextQueued();
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

    /// <summary>Stops all the commands that are running, and cancels those queued, so none is started as the others end.</summary>
    public void StopAll()
    {
        Queue.Clear();
        HasQueue = false;


        foreach (var entry in Entries.Where(static entry => entry.IsRunning).ToList())
        {
            entry.StopCommand.Execute(null);
        }
    }

    private bool CanClear => Entries.Any(static entry => !entry.IsRunning);

    /// <summary>Removes the commands that have ended, pinned or not; those still running are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Remove(static entry => !entry.IsRunning);

    private bool CanClearUnpinned => Entries.Any(static entry => entry is { IsRunning: false, IsPinned: false });

    /// <summary>Removes the commands that have ended, except those pinned; those still running are kept.</summary>
    [RelayCommand(CanExecute = nameof(CanClearUnpinned))]
    private void ClearUnpinned() => Remove(static entry => entry is { IsRunning: false, IsPinned: false });

    private void Remove(Func<CommandEntryViewModel, bool> predicate)
    {
        foreach (var entry in Entries.Where(predicate).ToList())
        {
            entry.PropertyChanged -= OnEntryChanged;
            Entries.Remove(entry);
        }

        HasEntries = Entries.Count > 0;
        ClearCommand.NotifyCanExecuteChanged();
        ClearUnpinnedCommand.NotifyCanExecuteChanged();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommandEntryViewModel.IsPinned))
        {
            ClearUnpinnedCommand.NotifyCanExecuteChanged();
        }
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

    /// <summary>Whether what is typed in the input can be completed, which it can be by PowerShell.</summary>
    public bool CanComplete => Project is PowerShellProject;

    /// <summary>Gets completion ready, so completing for the first time doesn't wait for it to start up.</summary>
    public void PrepareCompletion()
    {
        if (!CanComplete)
        {
            return;
        }

        completer.Prepare(Project.ExpandedWorkingFolder);
    }

    /// <summary>Completes the text before <paramref name="cursor"/> in <paramref name="input"/>, or returns <see langword="null"/> when it can't be completed.</summary>
    /// <remarks>The prepend script of the project is taken into account, as it can define what is typed, or change what it means.</remarks>
    public Task<CommandCompletion?> CompleteAsync(string input, int cursor, CancellationToken cancellationToken) => Project switch
    {
        PowerShellProject powerShell => completer.CompleteAsync(powerShell.ExpandedWorkingFolder, powerShell.EditorPrefix, input, cursor, cancellationToken),
        _ => Task.FromResult<CommandCompletion?>(null),
    };

    // Colors the command of entry, once it was parsed, which it is only by PowerShell; it is shown as it is in the meantime.
    // It is parsed after the prepend script of the project, as it is run, though only the command is shown.
    // Awaited on the UI thread, so the entry is changed on it.
    private async Task HighlightAsync(CommandEntryViewModel entry, PowerShellProject project)
    {
        if (await highlighter.HighlightAsync(project.EditorPrefix, entry.Command, CancellationToken.None) is { } highlighted)
        {
            entry.HighlightedCommand = highlighted;
        }
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
