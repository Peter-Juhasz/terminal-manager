using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ScriptFileWatcherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    private string _folder = null!;

    [TestInitialize]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("terminal-manager-tests-").FullName;

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    private PowerShellProject Project => new() { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = _folder };

    [TestMethod]
    public async Task ReadsTheFilesAsTheyChange()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, PackageJsonScriptFile.Name), """{ "scripts": { "build": "vite build" } }""", TestContext.CancellationToken);
        using var watcher = new ScriptFileWatcher(_folder, Project);

        var snapshots = await StartAndWaitAsync(watcher, static snapshots => snapshots is [{ Scripts: [{ Name: "build" }] }]);
        Assert.AreEqual(Path.Combine(_folder, PackageJsonScriptFile.Name), snapshots.Single().Path);

        // a file created later is shown before the others, in the order of the formats
        await File.WriteAllTextAsync(Path.Combine(_folder, TerminalManagerScriptFile.Name), "scripts:\n- name: Deploy\n  command: ./deploy.ps1\n", TestContext.CancellationToken);
        snapshots = await WaitAsync(watcher, static snapshots => snapshots is [{ Scripts: [{ Name: "Deploy" }] }, { Scripts: [{ Name: "build" }] }]);

        // one that is broken is shown with why
        await File.WriteAllTextAsync(Path.Combine(_folder, TerminalManagerScriptFile.Name), "scripts:\n  - Deploy\n", TestContext.CancellationToken);
        snapshots = await WaitAsync(watcher, static snapshots => snapshots is [{ Error: not null, Scripts: [] }, _]);

        File.Delete(Path.Combine(_folder, PackageJsonScriptFile.Name));
        await WaitAsync(watcher, static snapshots => snapshots is [{ Format: TerminalManagerScriptFile }]);
    }

    [TestMethod]
    public async Task ReadsAgainWhenALockFileChanges()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, PackageJsonScriptFile.Name), """{ "scripts": { "build": "b" } }""", TestContext.CancellationToken);
        using var watcher = new ScriptFileWatcher(_folder, Project);
        await StartAndWaitAsync(watcher, static snapshots => snapshots is [{ Scripts: [{ Command: "npm run build" }] }]);

        await File.WriteAllTextAsync(Path.Combine(_folder, "pnpm-lock.yaml"), "lockfileVersion: '9.0'\n", TestContext.CancellationToken);

        await WaitAsync(watcher, static snapshots => snapshots is [{ Scripts: [{ Command: "pnpm run build" }] }]);
    }

    [TestMethod]
    public void HasNoneForAFolderThatDoesNotExist()
    {
        using var watcher = new ScriptFileWatcher(Path.Combine(_folder, "missing"), Project);
        watcher.Start();

        Assert.IsEmpty(watcher.Snapshots);
    }

    private Task<IReadOnlyList<ScriptFileSnapshot>> StartAndWaitAsync(ScriptFileWatcher watcher, Func<IReadOnlyList<ScriptFileSnapshot>, bool> condition)
    {
        var waiting = WaitAsync(watcher, condition);
        watcher.Start();
        return waiting;
    }

    // waits until the snapshots are as expected, which they may be after a few changes, as a file can be written in several steps
    private async Task<IReadOnlyList<ScriptFileSnapshot>> WaitAsync(ScriptFileWatcher watcher, Func<IReadOnlyList<ScriptFileSnapshot>, bool> condition)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ScriptFileSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e)
        {
            if (watcher.Snapshots is var snapshots && condition(snapshots))
            {
                completion.TrySetResult(snapshots);
            }
        }

        watcher.Changed += OnChanged;
        try
        {
            OnChanged(watcher, EventArgs.Empty);
            return await completion.Task.WaitAsync(Timeout, TestContext.CancellationToken);
        }
        finally
        {
            watcher.Changed -= OnChanged;
        }
    }
}
