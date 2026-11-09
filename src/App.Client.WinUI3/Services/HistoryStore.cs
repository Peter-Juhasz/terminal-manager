using App.Domain;
using PeterJuhasz.Repositories.Abstractions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Keeps the history of each project in a file of its own, so it is only read when it is shown.
/// </summary>
public sealed class HistoryStore(Func<Guid, IObjectRepository<IReadOnlyList<HistoryEntry>>> histories)
{
    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync(Guid projectId, CancellationToken cancellationToken) =>
        await histories(projectId).GetOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? [];

    public Task SaveAsync(Guid projectId, IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return histories(projectId).ApplyAsync(_ => entries, cancellationToken);
    }

    public Task DeleteAsync(Guid projectId, CancellationToken cancellationToken) => histories(projectId).DeleteIfExistsAsync(cancellationToken);
}
