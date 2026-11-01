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
