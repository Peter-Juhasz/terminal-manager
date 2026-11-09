using System.Collections.ObjectModel;
using App.Client.WinUI3.Services;
using App.Domain;
using App.Extensions.Cmd;
using App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

public sealed partial class MainViewModel(ProjectStore store, HistoryStore history, CommandRunner runner, ElevatedCommandRunner elevatedRunner, ICommandCompleter completer, CmdCompleter cmdCompleter, IQuickInfoProvider quickInfo, ISyntaxHighlighter highlighter) : ObservableObject
{
    public ObservableCollection<ProjectSessionViewModel> Sessions { get; } = [];

    [ObservableProperty]
    public partial ProjectSessionViewModel? SelectedSession { get; set; }

    // the history of a project, and the files of scripts in its working folder, are only read once it is shown
    partial void OnSelectedSessionChanged(ProjectSessionViewModel? oldValue, ProjectSessionViewModel? newValue)
    {
        oldValue?.IsShown = false;
        if (newValue is not { } value)
        {
            return;
        }

        value.IsShown = true;
        _ = value.LoadHistoryAsync(CancellationToken.None);
        value.StartWatchingScriptFiles();
    }

    [ObservableProperty]
    public partial bool HasProjects { get; private set; }

    /// <summary>
    /// The progress of the commands running in all the projects, to be shown on the taskbar: the average of those that report a value,
    /// in the most pressing state any of them is in, or indeterminate when none report a value. Commands that don't report progress aren't counted.
    /// </summary>
    [ObservableProperty]
    public partial ProgressReport OverallProgress { get; private set; }

    /// <summary>Raised when a command has ended in any of the projects.</summary>
    public event EventHandler<CommandEntryViewModel>? CommandCompleted;

    private void Attach(ProjectSessionViewModel session)
    {
        session.RunningProgressChanged += OnRunningProgressChanged;
        session.CommandCompleted += OnCommandCompleted;
        Sessions.Add(session);
    }

    private void OnCommandCompleted(object? sender, CommandEntryViewModel entry) => CommandCompleted?.Invoke(sender, entry);

    private void OnRunningProgressChanged(object? sender, EventArgs e)
    {
        var reporting = Sessions
            .SelectMany(static session => session.RunningEntries)
            .Where(static entry => entry.IsProgressVisible)
            .ToList();

        if (reporting.Count == 0)
        {
            OverallProgress = new ProgressReport(ProgressState.None, null);
            return;
        }

        var state = reporting.Any(static entry => entry.ProgressState is ProgressState.Error) ? ProgressState.Error
            : reporting.Any(static entry => entry.ProgressState is ProgressState.Paused) ? ProgressState.Paused
            : reporting.Any(static entry => entry.ProgressState is ProgressState.Normal) ? ProgressState.Normal
            : ProgressState.Indeterminate;

        var values = reporting
            .Where(static entry => entry.ProgressState is not ProgressState.Indeterminate)
            .Select(static entry => entry.ProgressValue)
            .ToList();

        OverallProgress = new ProgressReport(state, values.Count > 0 ? (int)values.Average() : null);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var projects = await store.ListAsync(cancellationToken);
        foreach (var project in projects.OrderBy(static project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Attach(new ProjectSessionViewModel(project, runner, elevatedRunner, completer, cmdCompleter, quickInfo, highlighter, store, history));
        }

        HasProjects = Sessions.Count > 0;
        SelectedSession = Sessions.FirstOrDefault();
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        await store.AddAsync(project, cancellationToken);

        var session = new ProjectSessionViewModel(project, runner, elevatedRunner, completer, cmdCompleter, quickInfo, highlighter, store, history);
        Attach(session);
        HasProjects = true;
        SelectedSession = session;
    }

    /// <summary>Adds a copy of the project of <paramref name="session"/>, with its settings and saved scripts, but not its history.</summary>
    public async Task DuplicateAsync(ProjectSessionViewModel session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await AddAsync(session.Project with { Id = Guid.NewGuid(), DisplayName = $"{session.Project.DisplayName} (copy)" }, cancellationToken);
    }

    public async Task UpdateAsync(ProjectSessionViewModel session, Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        ArgumentNullException.ThrowIfNull(project);

        var persistedHistory = session.Project.PersistHistory;
        await store.UpdateAsync(project, cancellationToken);
        session.Project = project;
        if (project.PersistHistory != persistedHistory)
        {
            await session.OnPersistHistoryChangedAsync(cancellationToken);
        }
    }

    public async Task DeleteAsync(ProjectSessionViewModel session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await store.DeleteAsync(session.Project.Id, cancellationToken);
        try
        {
            await history.DeleteAsync(session.Project.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the project is gone either way; its history is only left behind
        }

        session.StopAll();
        session.StopWatchingScriptFiles();
        var index = Sessions.IndexOf(session);
        Sessions.Remove(session);
        session.RunningProgressChanged -= OnRunningProgressChanged;
        session.CommandCompleted -= OnCommandCompleted;
        OnRunningProgressChanged(this, EventArgs.Empty);
        HasProjects = Sessions.Count > 0;
        if (SelectedSession == session || SelectedSession is null)
        {
            SelectedSession = Sessions.Count > 0 ? Sessions[Math.Min(index, Sessions.Count - 1)] : null;
        }
    }

    /// <summary>Stops the commands that are running, such as when the app is closed.</summary>
    public void StopAll()
    {
        foreach (var session in Sessions)
        {
            session.StopAll();
        }
    }
}
