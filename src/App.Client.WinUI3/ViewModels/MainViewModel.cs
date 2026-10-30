using System.Collections.ObjectModel;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

public sealed partial class MainViewModel(ProjectStore store, CommandRunner runner) : ObservableObject
{
    public ObservableCollection<ProjectSessionViewModel> Sessions { get; } = [];

    [ObservableProperty]
    public partial ProjectSessionViewModel? SelectedSession { get; set; }

    [ObservableProperty]
    public partial bool HasProjects { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var projects = await store.ListAsync(cancellationToken);
        foreach (var project in projects.OrderBy(static project => project.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Sessions.Add(new ProjectSessionViewModel(project, runner, store));
        }

        HasProjects = Sessions.Count > 0;
        SelectedSession = Sessions.FirstOrDefault();
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        await store.AddAsync(project, cancellationToken);

        var session = new ProjectSessionViewModel(project, runner, store);
        Sessions.Add(session);
        HasProjects = true;
        SelectedSession = session;
    }

    public async Task UpdateAsync(ProjectSessionViewModel session, Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await store.UpdateAsync(project, cancellationToken);
        session.Project = project;
    }

    public async Task DeleteAsync(ProjectSessionViewModel session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await store.DeleteAsync(session.Project.Id, cancellationToken);

        session.StopCommand.Execute(null);
        var index = Sessions.IndexOf(session);
        Sessions.Remove(session);
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
            session.StopCommand.Execute(null);
        }
    }
}
