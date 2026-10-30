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
public sealed partial class ProjectSessionViewModel(Project project, CommandRunner runner, ProjectStore store) : ObservableObject
{
    // How often the time spent by the running command is updated.
    private static readonly TimeSpan DurationRefreshInterval = TimeSpan.FromMilliseconds(100);

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

    /// <summary>The number of commands that are running; any number of them can run at the same time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(RunningBadgeValue))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial int RunningCount { get; private set; }

    public bool IsRunning => RunningCount > 0;

    /// <summary>The number shown on the badge of the project, or -1 for a dot when only one command is running.</summary>
    public int RunningBadgeValue => RunningCount > 1 ? RunningCount : -1;

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
        var entry = new CommandEntryViewModel(command, stopSource.Cancel);
        Entries.Add(entry);
        HasEntries = true;
        RunningCount++;
        CommandStarted?.Invoke(this, entry);

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
            RunningCount--;
        }
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
