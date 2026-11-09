using App.Domain;
using PeterJuhasz.Repositories.Abstractions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Keeps the projects in a single file.
/// </summary>
public sealed class ProjectStore(ICollectionRepository<Project> projects)
{
    public Task<IReadOnlyCollection<Project>> ListAsync(CancellationToken cancellationToken) => projects.ListAsync(cancellationToken);

    public Task AddAsync(Project project, CancellationToken cancellationToken) => projects.AddAsync(project, cancellationToken);

    public Task UpdateAsync(Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        return projects.ApplyAsync(current => (current ?? []).Select(other => other.Id == project.Id ? project : other).ToArray(), cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        projects.ApplyAsync(current => (current ?? []).Where(other => other.Id != id).ToArray(), cancellationToken);
}
