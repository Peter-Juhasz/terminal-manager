using System.Text.Json;
using App.Client.WinUI3.Models;
using PeterJuhasz.Repositories.Abstractions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Keeps the user's settings in a single JSON file.
/// </summary>
public sealed class SettingsService(IObjectRepository<AppSettings> repository)
{
    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? new();
        Validate(settings);
        Settings = settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        await repository.ApplyAsync(_ => settings, cancellationToken).ConfigureAwait(false);
        Settings = settings;
    }

    private static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            throw new JsonException("The saved theme must be System, Dark, or Light.");
        }
    }
}
