using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using App.Client.WinUI3.Services;
using App.Domain;
using App.Extensions.Cmd;
using App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A project, with the commands run in it since the app was started, and before it if its history is kept.
/// </summary>
public sealed partial class ProjectSessionViewModel(Project project, CommandRunner runner, ElevatedCommandRunner elevatedRunner, ICommandCompleter completer, CmdCompleter cmdCompleter, IQuickInfoProvider quickInfo, ISyntaxHighlighter highlighter, ProjectStore store, HistoryStore history) : ObservableObject, IDisposable
{
    // Reading the saved history, which is started the first time the project is selected; until then it isn't saved either, so it isn't saved over.
    private Task? _loadingHistory;

    // Whether the saved history couldn't be read, in which case it is left as it is.
    private bool _isHistoryUnreadable;

    // Saves one at a time, so a later history isn't saved over by an earlier one.
    private readonly SemaphoreSlim _savingHistory = new(1, 1);

    // How often the time spent by the running command is updated.
    private static readonly TimeSpan DurationRefreshInterval = TimeSpan.FromSeconds(1);

    // How often more of the output of a running command is shown, as the text block showing it lays all of it out again each time.
    private static readonly TimeSpan OutputRefreshInterval = TimeSpan.FromMilliseconds(50);

    // The commands that were run, so they can be recalled with the arrow keys; the index is that of the one recalled.
    private readonly List<string> _history = [];
    private int _historyIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunElevated), nameof(IsWorkingFolderLocal))]
    [NotifyCanExecuteChangedFor(nameof(RunElevatedCommand))]
    public partial Project Project { get; set; } = project;

    /// <summary>Whether commands can be run as an administrator, which they can be on this machine; elsewhere, they are run with the rights they are run with there.</summary>
    public bool CanRunElevated => Project is LocalProject;

    /// <summary>Whether the working folder is on this machine, so it can be opened.</summary>
    public bool IsWorkingFolderLocal => Project is LocalProject { IsWorkingFolderOnWindows: true };

    public ObservableCollection<CommandEntryViewModel> Entries { get; } = [];

    /// <summary>The scripts saved to the project, in the order they are shown, which can be changed.</summary>
    public ObservableCollection<Script> Scripts { get; } = new(project.Scripts ?? []);

    /// <summary>
    /// The scripts shown, by where they are from: those saved to the project first, which is always there, even when it has none,
    /// then those of each file in the working folder there is, in the order of <see cref="ScriptFileWatcher.DefaultFormats"/>.
    /// </summary>
    public ObservableCollection<ScriptGroupViewModel> ScriptGroups { get; } = [CreateProjectGroup(project.Scripts ?? [])];

    private ScriptGroupViewModel ProjectGroup => ScriptGroups[0];

    /// <summary>Whether there are any scripts to show, saved to the project or read from files, or a file they couldn't be read from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoScriptMatches))]
    public partial bool HasScripts { get; private set; } = project.Scripts is { Count: > 0 };

    /// <summary>The text the scripts are searched for, in their names, commands and descriptions, in any case; when empty, all of them are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScriptSearch), nameof(HasNoScriptMatches))]
    public partial string ScriptFilter { get; set; } = string.Empty;

    /// <summary>What <see cref="ScriptFilter"/> is searched for with, and highlighted by; null when it is empty.</summary>
    public TextSearch? ScriptSearch { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoScriptMatches))]
    public partial bool HasScriptMatches { get; private set; } = project.Scripts is { Count: > 0 };

    /// <summary>Whether there are scripts, but none of them match the search.</summary>
    public bool HasNoScriptMatches => HasScripts && ScriptSearch is not null && !HasScriptMatches;

    partial void OnScriptFilterChanged(string value)
    {
        ScriptSearch = TextSearch.Create(value, isCaseSensitive: false, isRegex: false);
        foreach (var group in ScriptGroups)
        {
            group.Filter(ScriptSearch);
        }

        UpdateScriptGroups();
    }

    private static ScriptGroupViewModel CreateProjectGroup(IReadOnlyList<Script> scripts)
    {
        var group = new ScriptGroupViewModel(ScriptGroupViewModel.ProjectKey, "Project", filePath: null);
        group.Update(scripts, error: null, search: null);
        return group;
    }

    // the names of the groups are only shown when there are others than that of the project, so the scripts look as they always did without them
    private void UpdateScriptGroups()
    {
        HasScripts = ScriptGroups.Any(static group => group.Count > 0 || group.HasError);
        HasScriptMatches = ScriptGroups.Any(static group => group.FilteredScripts.Count > 0);
        var isHeaderVisible = ScriptGroups.Count > 1;
        foreach (var group in ScriptGroups)
        {
            group.IsHeaderVisible = isHeaderVisible;
        }
    }

    // Whether the scripts of the files in the working folder are shown, which they are from the first time the project is selected.
    private bool _isWatchingScriptFiles;

    // Where the changes of the watcher are handled, which is the UI thread it was started on.
    private SynchronizationContext? _scriptFilesContext;

    private ScriptFileWatcher? _scriptFileWatcher;

    // What the files were read for, so they are only read again when that changes.
    private (string? Folder, CommandLanguage Language) _scriptFilesKey;

    /// <summary>Shows the scripts of the files in the working folder, as they are when they change, from the first time this is called, on the UI thread.</summary>
    public void StartWatchingScriptFiles()
    {
        if (_isWatchingScriptFiles)
        {
            return;
        }

        _isWatchingScriptFiles = true;
        _scriptFilesContext = SynchronizationContext.Current;
        RestartScriptFileWatcher();
    }

    /// <summary>Stops watching the files in the working folder, such as when the project is deleted.</summary>
    public void StopWatchingScriptFiles()
    {
        _isWatchingScriptFiles = false;
        _scriptFileWatcher?.Dispose();
        _scriptFileWatcher = null;
    }

    /// <summary>Stops watching the files in the working folder, and lets go of what saving the history uses, such as when the project is deleted, or the app is closed.</summary>
    public void Dispose()
    {
        StopWatchingScriptFiles();
        _savingHistory.Dispose();
    }

    // the working folder, whether the files are read, or the language the scripts are written for may have changed
    partial void OnProjectChanged(Project value)
    {
        if (_isWatchingScriptFiles && GetScriptFilesKey(value) != _scriptFilesKey)
        {
            RestartScriptFileWatcher();
        }
    }

    private static (string? Folder, CommandLanguage Language) GetScriptFilesKey(Project project) => ((project as LocalProject)?.ScriptFilesFolder, project.Language);

    private void RestartScriptFileWatcher()
    {
        _scriptFileWatcher?.Dispose();
        _scriptFileWatcher = null;
        _scriptFilesKey = GetScriptFilesKey(Project);

        // those of the files as they were are no longer shown, until the files are read again
        ApplyScriptFiles(Array.Empty<ScriptFileSnapshot>());
        if (_scriptFilesKey.Folder is not { } folder)
        {
            return;
        }

        var watcher = new ScriptFileWatcher(folder, Project);
        watcher.Changed += OnScriptFilesChanged;
        _scriptFileWatcher = watcher;
        watcher.Start();
    }

    // raised on a background thread
    private void OnScriptFilesChanged(object? sender, EventArgs e)
    {
        if (sender is not ScriptFileWatcher watcher)
        {
            return;
        }

        if (_scriptFilesContext is not { } context)
        {
            ApplyScriptFiles(watcher);
            return;
        }

        context.Post(state => ApplyScriptFiles((ScriptFileWatcher)state!), watcher);
    }

    // one that was replaced, or stopped, before this was handled is left out
    private void ApplyScriptFiles(ScriptFileWatcher watcher)
    {
        if (watcher != _scriptFileWatcher)
        {
            return;
        }

        ApplyScriptFiles(watcher.Snapshots);
    }

    // The groups are kept by file while it is there, so whether each is collapsed is kept too.
    private void ApplyScriptFiles(IReadOnlyList<ScriptFileSnapshot> snapshots)
    {
        for (var index = ScriptGroups.Count - 1; index > 0; index--)
        {
            var group = ScriptGroups[index];
            if (!snapshots.Any(snapshot => snapshot.Format.FileName == group.Key))
            {
                ScriptGroups.RemoveAt(index);
            }
        }

        // the snapshots are in the order of the formats, as are the groups left, so each is inserted where it is shown
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var group = ScriptGroups.Skip(1).FirstOrDefault(group => group.Key == snapshot.Format.FileName);
            if (group is null)
            {
                group = new(snapshot.Format.FileName, snapshot.Format.DisplayName, snapshot.Path);
                ScriptGroups.Insert(index + 1, group);
            }

            group.Update(snapshot.Scripts, snapshot.Error, ScriptSearch);
        }

        UpdateScriptGroups();
    }

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
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(BadgeValue), nameof(IsBadgeVisible), nameof(IsFailedBadgeVisible))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand), nameof(ClearUnpinnedCommand))]
    public partial int RunningCount { get; private set; }

    /// <summary>Whether the project is the one shown, so the commands that fail in it are seen as they do.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value)
    {
        if (value)
        {
            HasUnseenFailure = false;
        }
    }

    /// <summary>Whether a command failed, other than by being stopped, since the project was last shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFailedBadgeVisible), nameof(IsBadgeVisible))]
    public partial bool HasUnseenFailure { get; private set; }

    /// <summary>Whether the badge of the project is a red dot, which it is once the commands running have ended, if one failed unseen.</summary>
    public bool IsFailedBadgeVisible => HasUnseenFailure && !IsRunning;

    public bool IsRunning => RunningCount > 0;

    /// <summary>The number shown on the badge of the project, or -1 for a dot when only one command is running, or a failure is shown.</summary>
    public int BadgeValue => RunningCount > 1 ? RunningCount : -1;

    /// <summary>
    /// Whether the badge is shown, which it is while commands are running, unless the progress of the only one is shown instead,
    /// and once they have ended, if one failed unseen.
    /// </summary>
    public bool IsBadgeVisible => (IsRunning && !HasRunningProgress) || IsFailedBadgeVisible;

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

    private bool CanRunInputElevated => CanRun && CanRunElevated;

    [RelayCommand(CanExecute = nameof(CanRun), AllowConcurrentExecutions = true)]
    private Task RunAsync() => RunInputAsync(isElevated: false);

    /// <summary>Runs what was typed in the input as an administrator, which the user is asked to allow the first time.</summary>
    [RelayCommand(CanExecute = nameof(CanRunInputElevated), AllowConcurrentExecutions = true)]
    private Task RunElevatedAsync() => RunInputAsync(isElevated: true);

    private Task RunInputAsync(bool isElevated)
    {
        var command = Input.Trim();
        Input = string.Empty;
        return ExecuteAsync(command, isElevated);
    }

    private static bool CanRunScript(Script? script) => script is not null;

    /// <summary>Runs <paramref name="script"/>, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript), AllowConcurrentExecutions = true)]
    private Task RunScriptAsync(Script? script) => script is null ? Task.CompletedTask : ExecuteAsync(script.Command, isElevated: false);

    /// <summary>Runs <paramref name="script"/> as an administrator, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript), AllowConcurrentExecutions = true)]
    private Task RunScriptElevatedAsync(Script? script) => script is null ? Task.CompletedTask : ExecuteAsync(script.Command, isElevated: true);

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
    private void EnqueueScript(Script? script)
    {
        if (script is null)
        {
            return;
        }

        AddToQueue(script.Command, isElevated: false);
    }

    /// <summary>Queues <paramref name="script"/> to be run as an administrator, and leaves what was typed in the input as it is.</summary>
    [RelayCommand(CanExecute = nameof(CanRunScript))]
    private void EnqueueScriptElevated(Script? script)
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
    public Task SaveScriptAsync(Script script, CancellationToken cancellationToken)
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

    public Task DeleteScriptAsync(Script script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);

        Scripts.Remove(script);
        return SaveScriptsAsync(cancellationToken);
    }

    private Task SaveScriptsAsync(CancellationToken cancellationToken)
    {
        ProjectGroup.Update(Scripts.ToArray(), error: null, ScriptSearch);
        UpdateScriptGroups();
        Project = Project with { Scripts = Scripts switch { [] => null, _ => Scripts.ToArray() } };
        return store.UpdateAsync(Project, cancellationToken);
    }

    /// <summary>Shows the commands saved to the history of the project, the first time this is called, if its history is kept.</summary>
    public Task LoadHistoryAsync(CancellationToken cancellationToken) => Project.PersistHistory
        ? _loadingHistory ??= LoadSavedHistoryAsync(cancellationToken)
        : Task.CompletedTask;

    // Awaited on the UI thread, so the entries are added on it.
    private async Task LoadSavedHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<HistoryEntry> saved;
        try
        {
            saved = await history.LoadAsync(Project.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _isHistoryUnreadable = true;
            return;
        }

        // shown before those run while it was read
        for (var index = 0; index < saved.Count; index++)
        {
            var (command, isElevated) = (saved[index].Command, saved[index].IsElevated);
            var entry = CommandEntryViewModel.Restore(saved[index], () => ExecuteAsync(command, isElevated));
            entry.PropertyChanged += OnEntryChanged;
            Entries.Insert(index, entry);
            if (GetHighlightingPrefix(Project) is { } prefix)
            {
                _ = HighlightAsync(entry, prefix);
            }
        }

        // recalled before those run while it was read, each only where it was run last
        var commands = saved.Select(static entry => entry.Command).Concat(_history).ToList();
        _history.Clear();
        foreach (var command in commands)
        {
            _history.Remove(command);
            _history.Add(command);
        }

        _historyIndex = _history.Count;

        HasEntries = Entries.Count > 0;
        ClearCommand.NotifyCanExecuteChanged();
        ClearUnpinnedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Saves the history as it is when it starts being kept, or deletes it when it no longer is, after <see cref="Project"/> was changed.</summary>
    public async Task OnPersistHistoryChangedAsync(CancellationToken cancellationToken)
    {
        if (Project.PersistHistory)
        {
            // as it isn't kept while it is turned off, there is none saved to be shown
            _loadingHistory ??= Task.CompletedTask;
            _isHistoryUnreadable = false;
            await SaveHistoryAsync(cancellationToken);
            return;
        }

        _loadingHistory = null;
        await _savingHistory.WaitAsync(cancellationToken);
        try
        {
            await history.DeleteAsync(Project.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _savingHistory.Release();
        }
    }

    // Saves the commands that have ended, if the history is kept, and the saved one was read first; failing to save it doesn't stop anything else.
    private async Task SaveHistoryAsync(CancellationToken cancellationToken)
    {
        if (!Project.PersistHistory || _loadingHistory is not { } loading)
        {
            return;
        }

        await loading;
        if (_isHistoryUnreadable)
        {
            return;
        }

        await _savingHistory.WaitAsync(cancellationToken);
        try
        {
            // taken once it is its turn, so it is the latest
            var entries = Entries.Where(static entry => !entry.IsRunning).Select(static entry => entry.ToHistoryEntry()).ToArray();
            await history.SaveAsync(Project.Id, entries, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _savingHistory.Release();
        }
    }

    private async Task ExecuteAsync(string command, bool isElevated)
    {
        // as it is run with the same rights either way, it isn't shown as run as an administrator
        isElevated &= CanRunElevated;

        _history.Remove(command);
        _history.Add(command);
        _historyIndex = _history.Count;

        // the project as it is when the command is started, which it is run by, whatever it is changed to while it runs
        var project = Project;

        using var stopSource = new CancellationTokenSource();
        var entry = new CommandEntryViewModel(command, isElevated, stopSource.Cancel, () => ExecuteAsync(command, isElevated))
        {
            IsExpanded = !project.CollapseSucceededOutput,
        };
        Entries.Add(entry);
        HasEntries = true;
        entry.PropertyChanged += OnEntryChanged;
        _running.Add(entry);
        entry.PropertyChanged += OnRunningChanged;
        UpdateRunning();
        CommandStarted?.Invoke(this, entry);

        if (GetHighlightingPrefix(project) is { } prefix)
        {
            _ = HighlightAsync(entry, prefix);
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
            if (!IsShown && entry is { IsFailed: true, WasStopped: false })
            {
                HasUnseenFailure = true;
            }

            _running.Remove(entry);
            UpdateRunning();

            // what it wrote is shown once it is known to be needed
            if (project.CollapseSucceededOutput && entry.IsFailed)
            {
                entry.IsExpanded = true;
            }

            CommandCompleted?.Invoke(this, entry);
            _ = SaveHistoryAsync(CancellationToken.None);
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
        OnPropertyChanged(nameof(IsBadgeVisible));
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

    /// <summary>Removes <paramref name="entry"/> from the history, once it has ended.</summary>
    [RelayCommand]
    private void RemoveEntry(CommandEntryViewModel? entry)
    {
        if (entry is not { IsRunning: false })
        {
            return;
        }

        Remove(other => other == entry);
    }

    /// <summary>The entry selected to be compared with another one, which is compared with it from the menu of the other.</summary>
    public CommandEntryViewModel? EntrySelectedForComparison { get; set; }

    /// <summary>The last run of the command of <paramref name="entry"/> before it, which has ended, if there is one.</summary>
    public CommandEntryViewModel? FindPreviousRun(CommandEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var index = Entries.IndexOf(entry);
        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (Entries[previous] is { IsCompleted: true } candidate && candidate.Command == entry.Command)
            {
                return candidate;
            }
        }

        return null;
    }

    private void Remove(Func<CommandEntryViewModel, bool> predicate)
    {
        foreach (var entry in Entries.Where(predicate).ToList())
        {
            entry.PropertyChanged -= OnEntryChanged;
            Entries.Remove(entry);
            if (entry == EntrySelectedForComparison)
            {
                EntrySelectedForComparison = null;
            }
        }

        HasEntries = Entries.Count > 0;
        ClearCommand.NotifyCanExecuteChanged();
        ClearUnpinnedCommand.NotifyCanExecuteChanged();

        // so those cleared aren't shown again
        _ = SaveHistoryAsync(CancellationToken.None);
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommandEntryViewModel.IsPinned))
        {
            ClearUnpinnedCommand.NotifyCanExecuteChanged();

            // one still running is saved as pinned once it ends
            if (sender is CommandEntryViewModel { IsRunning: false })
            {
                _ = SaveHistoryAsync(CancellationToken.None);
            }
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

    /// <summary>Whether what is typed in the input can be completed, which it can be by PowerShell, and the first word of it for the Command Prompt.</summary>
    public bool CanComplete => Project is PowerShellProject or CmdProject;

    /// <summary>Gets completion ready, so completing for the first time doesn't wait for it to start up.</summary>
    public void PrepareCompletion()
    {
        switch (Project)
        {
            case PowerShellProject powerShell:
                completer.Prepare(powerShell.ExpandedWorkingFolder);
                break;

            case CmdProject cmd:
                cmdCompleter.Prepare(cmd.ExpandedWorkingFolder, cmd.ResolveEnvironmentVariables());
                break;
        }
    }

    /// <summary>Completes the text before <paramref name="cursor"/> in <paramref name="input"/>, or returns <see langword="null"/> when it can't be completed.</summary>
    /// <remarks>The prepend script of the project is taken into account, as it can define what is typed, or change what it means.</remarks>
    public Task<CommandCompletion?> CompleteAsync(string input, int cursor, CancellationToken cancellationToken) => Project switch
    {
        PowerShellProject powerShell => completer.CompleteAsync(powerShell.ExpandedWorkingFolder, powerShell.EditorPrefix, input, cursor, cancellationToken),
        // PATH and PATHEXT can be set by the project, so the executables are those the commands find
        CmdProject cmd => cmdCompleter.CompleteAsync(cmd.ExpandedWorkingFolder, cmd.ResolveEnvironmentVariables(), input, cursor, cancellationToken),
        _ => Task.FromResult<CommandCompletion?>(null),
    };

    /// <summary>What is at <paramref name="position"/> in <paramref name="input"/>, a command of the project, or <see langword="null"/> when there is nothing there to tell about.</summary>
    /// <remarks>The prepend script of the project is taken into account, as it is for completion.</remarks>
    public Task<QuickInfo?> GetQuickInfoAsync(string input, int position, CancellationToken cancellationToken) => Project switch
    {
        PowerShellProject powerShell => quickInfo.GetQuickInfoAsync(powerShell.ExpandedWorkingFolder, powerShell.EditorPrefix, input, position, cancellationToken),
        _ => Task.FromResult<QuickInfo?>(null),
    };

    // What the commands of project are parsed after when they are highlighted, which they are only when they are run by PowerShell, or null when they aren't.
    private static string? GetHighlightingPrefix(Project project) => project switch
    {
        { Language: CommandLanguage.PowerShell } => project.EditorPrefix,
        _ => null,
    };

    // Colors the command of entry, once it was parsed, which it is only by PowerShell; it is shown as it is in the meantime.
    // It is parsed after the prepend script of the project, as it is run, though only the command is shown.
    // Awaited on the UI thread, so the entry is changed on it.
    private async Task HighlightAsync(CommandEntryViewModel entry, string prefix)
    {
        if (await highlighter.HighlightAsync(prefix, entry.Command, CancellationToken.None) is { } highlighted)
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
