using System.Text.Json;
using App.Client.WinUI3.Services;
using App.Domain;
using PeterJuhasz.Repositories.Blobs;
using PeterJuhasz.Repositories.FileSystem;
using PeterJuhasz.Repositories.Locking;
using PeterJuhasz.Repositories.Serialization.Json;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class SettingsServiceTests(TestContext testContext)
{
    private readonly DirectoryInfo _folder = new(Path.Combine(Path.GetTempPath(), "TerminalSettingsTests", Guid.NewGuid().ToString("N")));

    private string SettingsPath => Path.Combine(_folder.FullName, "settings.json");

    [TestInitialize]
    public void Initialize() => _folder.Create();

    [TestCleanup]
    public void Cleanup() => _folder.Delete(recursive: true);

    [TestMethod]
    public async Task MissingSettingsUseSystemTheme()
    {
        var service = CreateService();

        await service.LoadAsync(testContext.CancellationToken);

        Assert.AreEqual(AppTheme.System, service.Settings.Theme);
        Assert.IsFalse(File.Exists(SettingsPath));
    }

    [TestMethod]
    [DataRow(AppTheme.System)]
    [DataRow(AppTheme.Dark)]
    [DataRow(AppTheme.Light)]
    public async Task SavedThemeIsRestoredByNewService(AppTheme theme)
    {
        var service = CreateService();

        await service.SaveAsync(new AppSettings(theme), testContext.CancellationToken);

        Assert.AreEqual(theme, service.Settings.Theme);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath, testContext.CancellationToken));
        // the default theme isn't written, as it is read back the same without it
        Assert.AreEqual(theme is AppTheme.System ? null : theme.ToString(), json.RootElement.TryGetProperty("theme", out var saved) ? saved.GetString() : null);

        var restarted = CreateService();
        await restarted.LoadAsync(testContext.CancellationToken);
        Assert.AreEqual(theme, restarted.Settings.Theme);
    }

    [TestMethod]
    public async Task SaveReplacesExistingSettings()
    {
        var service = CreateService();
        await service.SaveAsync(new AppSettings(AppTheme.Dark), testContext.CancellationToken);

        await service.SaveAsync(new AppSettings(AppTheme.Light), testContext.CancellationToken);

        var restarted = CreateService();
        await restarted.LoadAsync(testContext.CancellationToken);
        Assert.AreEqual(AppTheme.Light, restarted.Settings.Theme);
    }

    [TestMethod]
    public async Task MissingThemeUsesSystemTheme()
    {
        await File.WriteAllTextAsync(SettingsPath, "{}", testContext.CancellationToken);
        var service = CreateService();

        await service.LoadAsync(testContext.CancellationToken);

        Assert.AreEqual(AppTheme.System, service.Settings.Theme);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"theme\":\"Unknown\"}")]
    [DataRow("{\"theme\":123}")]
    public async Task InvalidSettingsAreReportedAndPreserved(string json)
    {
        await File.WriteAllTextAsync(SettingsPath, json, testContext.CancellationToken);
        var service = CreateService();

        await Assert.ThrowsAsync<JsonException>(() => service.LoadAsync(testContext.CancellationToken));

        Assert.AreEqual(AppTheme.System, service.Settings.Theme);
        Assert.AreEqual(json, await File.ReadAllTextAsync(SettingsPath, testContext.CancellationToken));
    }

    [TestMethod]
    public async Task FailedSaveDoesNotApplySettings()
    {
        var service = CreateService();
        await service.SaveAsync(new AppSettings(AppTheme.Dark), testContext.CancellationToken);
        File.Delete(SettingsPath);
        Directory.CreateDirectory(SettingsPath);

        var exception = await Assert.ThrowsAsync<Exception>(() =>
            service.SaveAsync(new AppSettings(AppTheme.Light), testContext.CancellationToken));

        Assert.IsTrue(exception is IOException or UnauthorizedAccessException);
        Assert.AreEqual(AppTheme.Dark, service.Settings.Theme);
    }

    private SettingsService CreateService() => new(
        _folder.GetPartition()
            .GetBlob("settings.json")
            .WithLocking()
            .AsJsonObjectRepository<AppSettings>(AppJsonSerializerContext.Default.AppSettings));
}
