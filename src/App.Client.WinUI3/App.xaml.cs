using System.Text.Json;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using App.Client.WinUI3.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeterJuhasz.Repositories.Blobs;
using PeterJuhasz.Repositories.FileSystem;
using PeterJuhasz.Repositories.Locking;
using PeterJuhasz.Repositories.Serialization.Json;

namespace App.Client.WinUI3;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;

    public App()
    {
        _services = ConfigureServices();
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string? settingsError = null;
        try
        {
            await _services.GetRequiredService<SettingsService>().LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settingsError = $"The saved settings couldn't be loaded. The system theme is used instead. The settings file hasn't been changed.\n\n{ex.Message}";
        }

        // Loaded before the window is shown, so it doesn't flash empty first.
        await _services.GetRequiredService<MainViewModel>().LoadAsync(CancellationToken.None);

        _window = _services.GetRequiredService<MainWindow>();
        _window.Activate();

        if (settingsError is not null)
        {
            await Dialogs.ShowAsync(new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                Style = (Style)Resources["DefaultContentDialogStyle"],
                Title = "Couldn't load settings",
                Content = new TextBlock { Text = settingsError, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
            });
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Defaults to OneDrive so data roams across machines; falls back to local app data (redirected per-package
        // for packaged apps) when OneDrive isn't set up.
        var dataFolderPath = Environment.GetEnvironmentVariable("TERMINAL_DATA_FOLDER") switch
        {
            { Length: > 0 } overridden => overridden,
            _ => Environment.GetEnvironmentVariable("OneDrive") switch
            {
                { Length: > 0 } oneDrive => Path.Combine(oneDrive, "Apps", "Terminal"),
                _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terminal"),
            },
        };
        var dataFolder = new DirectoryInfo(Environment.ExpandEnvironmentVariables(dataFolderPath));

        // The type infos are passed in rather than the context, as getting them from the context isn't trim safe.
        services.AddSingleton(new ProjectStore(
            dataFolder.GetPartition()
                .GetBlob("projects.json")
                    .WithLocking()
                .AsCollectionRepository(new JsonTypeInfoJsonCollectionSerializer<Project>(
                    AppJsonSerializerContext.Default.IReadOnlyCollectionProject,
                    AppJsonSerializerContext.Default.Project))));

        services.AddSingleton(new SettingsService(
            dataFolder.GetPartition()
                .GetBlob("settings.json")
                .WithLocking()
                .AsJsonObjectRepository<AppSettings>(AppJsonSerializerContext.Default.AppSettings)));

        var historyPartition = dataFolder.GetPartition("history");
        services.AddSingleton(new HistoryStore(projectId =>
            historyPartition
                .GetBlob($"{projectId}.json")
                .WithLocking()
                .AsJsonObjectRepository<IReadOnlyList<HistoryEntry>>(AppJsonSerializerContext.Default.IReadOnlyListHistoryEntry)));

        services.AddSingleton<CommandRunner>();
        services.AddSingleton<ElevatedCommandRunner>();
        // one instance for both, so completion and quick info share the pwsh of each working folder
        services.AddSingleton<PowerShellLanguageService>();
        services.AddSingleton<ICommandCompleter>(static provider => provider.GetRequiredService<PowerShellLanguageService>());
        services.AddSingleton<IQuickInfoProvider>(static provider => provider.GetRequiredService<PowerShellLanguageService>());
        services.AddSingleton<PowerShellHighlighter>();
        services.AddSingleton<ISyntaxHighlighter>(static provider => provider.GetRequiredService<PowerShellHighlighter>());
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
