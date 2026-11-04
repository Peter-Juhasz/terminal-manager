using System.Text.Json;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class WslProjectTests
{
    [TestMethod]
    public void RoundTrips()
    {
        var project = new WslProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "~/src/app", Distribution = "Ubuntu", User = "me", PrependScript = "set -e", LoadEnvFile = true };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsInstanceOfType<WslProject>(deserialized);
        Assert.Contains("\"$type\": \"wsl\"", json);
        Assert.DoesNotContain("isWorkingFolderOnWindows", json);
        Assert.AreEqual(project, deserialized with { Scripts = null });
    }

    [TestMethod]
    [DataRow(@"C:\src\app", true)]
    [DataRow(@"%USERPROFILE%\src", true)]
    [DataRow(@"\\wsl.localhost\Ubuntu\home\me", true)]
    [DataRow("~", false)]
    [DataRow("~/src/app", false)]
    [DataRow("/home/me/app", false)]
    [DataRow(" /home/me/app", false)]
    public void TellsWindowsFoldersFromLinuxFolders(string folder, bool isOnWindows)
    {
        var project = new WslProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = folder };

        Assert.AreEqual(isOnWindows, project.IsWorkingFolderOnWindows);
        Assert.AreEqual(!isOnWindows, WslProject.IsLinuxFolder(folder));
    }

    [TestMethod]
    public void RunsLoginBashInWindowsFolder()
    {
        var project = new WslProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = @"C:\src\app",
            EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "a 'b'" },
            AppendScript = "echo done",
        };

        var startInfo = project.CreateStartInfo("echo \"$HOME\"\r\necho there");

        // the process is started in the folder by the runner, which WSL opens as /mnt/c/src/app on its own
        Assert.AreEqual("wsl.exe", startInfo.FileName);
        Assert.AreEqual(string.Empty, startInfo.WorkingDirectory);
        Assert.AreSequenceEqual(
            (string[])["--exec", "bash", "-l", "-c", "export GREETING='a '\\''b'\\'''\necho \"$HOME\"\necho there\necho done\n"],
            startInfo.ArgumentList.ToArray());
        Assert.AreEqual("1", startInfo.Environment["WSL_UTF8"]);
        Assert.AreEqual(@"C:\src\app", project.LocationDisplayName);
    }

    [TestMethod]
    public void ChangesToLinuxFolderInScript()
    {
        var project = new WslProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "~/my src", Distribution = "Ubuntu-24.04", User = "root", LoadEnvFile = true };

        var startInfo = project.CreateStartInfo("ls");

        // a folder of Linux can't be started in, and the .env file isn't looked for in it
        Assert.AreEqual(Path.GetTempPath(), startInfo.WorkingDirectory);
        Assert.AreSequenceEqual(
            (string[])["--distribution", "Ubuntu-24.04", "--user", "root", "--exec", "bash", "-l", "-c", "cd -- ~/'my src' || exit\nls\n"],
            startInfo.ArgumentList.ToArray());
        Assert.IsEmpty(project.ResolveEnvironmentVariables());
        Assert.AreEqual("Ubuntu-24.04: ~/my src", project.LocationDisplayName);
    }
}
