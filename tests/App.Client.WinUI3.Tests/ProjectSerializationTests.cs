using System.Text.Json;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ProjectSerializationTests
{
    [TestMethod]
    public void OptionalValuesAreUnsetWhenMissing()
    {
        const string json = """{"$type":"pwsh","id":"0fa573f3-d373-40d7-90dc-29ec60652191","displayName":"A","workingFolder":"C:\\"}""";

        var project = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsNotNull(project);
        Assert.IsNull(project.EnvironmentVariables);
        Assert.IsEmpty(project.ResolveEnvironmentVariables());
        Assert.IsNull(project.Scripts);
        Assert.IsFalse(project.LoadEnvFile);
        var powerShell = Assert.IsInstanceOfType<PowerShellProject>(project);
        Assert.IsNull(powerShell.PrependScript);
        Assert.IsNull(powerShell.AppendScript);
        Assert.AreEqual("Get-Item .", powerShell.Wrap("Get-Item ."));
    }

    [TestMethod]
    public void UnsetValuesAreNotWritten()
    {
        var project = new DotNetRunProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.DoesNotContain("environmentVariables", json);
        Assert.DoesNotContain("loadEnvFile", json);
        Assert.DoesNotContain("scripts", json);
        Assert.DoesNotContain("runArguments", json);
        Assert.AreEqual("run -- greet", project.CreateStartInfo("greet").Arguments);
    }

    [TestMethod]
    public void SettingsWithoutWrappingRoundTrip()
    {
        var json = JsonSerializer.Serialize(new AppSettings(WrapOutput: false), AppJsonSerializerContext.Default.AppSettings);

        Assert.IsFalse(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings)!.WrapOutput);
    }

    [TestMethod]
    public void ScriptsRoundTrip()
    {
        var project = new PowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = @"C:\",
            PrependScript = "Import-Module ./build.psm1",
            AppendScript = "Write-Host done",
        };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);
        var deserialized = Assert.IsInstanceOfType<PowerShellProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));

        Assert.AreEqual("Import-Module ./build.psm1", deserialized.PrependScript);
        Assert.AreEqual("Write-Host done", deserialized.AppendScript);
    }

    [TestMethod]
    public void WindowsPowerShellRoundTrips()
    {
        var project = new WindowsPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", PrependScript = "$a = 1" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);
        var deserialized = Assert.IsInstanceOfType<WindowsPowerShellProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));

        Assert.Contains("\"$type\": \"powershell\"", json);
        Assert.AreEqual("$a = 1", deserialized.PrependScript);
        // the editor changes the scripts through the base type, which must keep the terminal.
        Assert.IsInstanceOfType<WindowsPowerShellProject>((PowerShellProject)deserialized with { AppendScript = "$b = 2" });
    }

    [TestMethod]
    public void DeveloperPowerShellRoundTrips()
    {
        var project = new DeveloperPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"devpwsh\"", json);
        Assert.IsInstanceOfType<DeveloperPowerShellProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
    }

    [TestMethod]
    public void DeveloperCmdRoundTrips()
    {
        var project = new DeveloperCmdProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"devcmd\"", json);
        Assert.IsInstanceOfType<DeveloperCmdProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
    }

    [TestMethod]
    public void DotNetRunRoundTrips()
    {
        var project = new DotNetRunProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", RunArguments = "--project src/App -c Release" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"dotnetrun\"", json);
        var deserialized = Assert.IsInstanceOfType<DotNetRunProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
        Assert.AreEqual("--project src/App -c Release", deserialized.RunArguments);
    }

    [TestMethod]
    public void DotNetRunPassesCommandAfterRunArguments()
    {
        var project = new DotNetRunProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", RunArguments = "-c Release" };

        Assert.AreEqual("run -c Release -- greet \"a b\"", project.CreateStartInfo("greet \"a b\"").Arguments);
        Assert.AreEqual("run -- greet", (project with { RunArguments = "" }).CreateStartInfo("greet").Arguments);
    }

    [TestMethod]
    public void WrapPutsScriptsOnLinesOfTheirOwn()
    {
        var project = new PowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", PrependScript = "$a = 1", AppendScript = "$b = 2" };

        Assert.AreEqual("$a = 1\nGet-Item .\n$b = 2", project.Wrap("Get-Item ."));
        Assert.AreEqual("$a = 1\n", project.EditorPrefix);
        Assert.AreEqual("Get-Item .", (project with { PrependScript = string.Empty, AppendScript = string.Empty }).Wrap("Get-Item ."));
    }

    [TestMethod]
    public void EnvironmentVariablesRoundTrip()
    {
        var project = new PowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = @"C:\",
            EnvironmentVariables = new Dictionary<string, string> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1" },
            LoadEnvFile = true,
        };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("1", deserialized.EnvironmentVariables?["DOTNET_CLI_TELEMETRY_OPTOUT"]);
        Assert.IsTrue(deserialized.LoadEnvFile);
    }
}
