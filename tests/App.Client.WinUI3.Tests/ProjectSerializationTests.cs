using System.Text.Json;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ProjectSerializationTests
{
    [TestMethod]
    public void EnvironmentVariablesAreEmptyWhenMissing()
    {
        const string json = """{"$type":"pwsh","id":"0fa573f3-d373-40d7-90dc-29ec60652191","displayName":"A","workingFolder":"C:\\","scripts":[]}""";

        var project = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsNotNull(project);
        Assert.IsEmpty(project.EnvironmentVariables);
        var powerShell = Assert.IsInstanceOfType<PowerShellProject>(project);
        Assert.AreEqual(string.Empty, powerShell.PrependScript);
        Assert.AreEqual(string.Empty, powerShell.AppendScript);
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
        };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("1", deserialized.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"]);
    }
}
