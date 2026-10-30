using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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
        // Loaded before the window is shown, so it doesn't flash empty first.
        await _services.GetRequiredService<MainViewModel>().LoadAsync(CancellationToken.None);

        _window = _services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Packaged apps have this redirected to their own per-package folder.
        var dataFolder = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Terminal"));

        // The type infos are passed in rather than the context, as getting them from the context isn't trim safe.
        services.AddSingleton(new ProjectStore(
            dataFolder.GetPartition()
                .GetBlob("projects.json")
                    .WithLocking()
                .AsCollectionRepository(new JsonTypeInfoJsonCollectionSerializer<Project>(
                    AppJsonSerializerContext.Default.IReadOnlyCollectionProject,
                    AppJsonSerializerContext.Default.Project))));

        services.AddSingleton<CommandRunner>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
