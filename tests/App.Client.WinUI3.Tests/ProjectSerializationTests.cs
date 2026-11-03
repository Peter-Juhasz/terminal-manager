using System.Text.Json;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

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
        Assert.IsNull(project.Scripts);
        var powerShell = Assert.IsInstanceOfType<PowerShellProject>(project);
        Assert.IsEmpty(powerShell.ResolveEnvironmentVariables());
        Assert.IsFalse(powerShell.LoadEnvFile);
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
    public void DotNetFileRoundTrips()
    {
        var project = new DotNetFileProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", RunArguments = "-c Release" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"dotnetfile\"", json);
        var deserialized = Assert.IsInstanceOfType<DotNetFileProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
        Assert.AreEqual("-c Release", deserialized.RunArguments);
        Assert.IsInstanceOfType<DotNetFileProject>((DotNetRunProject)deserialized with { RunArguments = null });
    }

    [TestMethod]
    public void DotNetFilePassesCommandOnStandardInput()
    {
        var project = new DotNetFileProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", RunArguments = "-- a b" };

        Assert.AreEqual("run - -- a b", project.CreateStartInfo("Console.WriteLine();").Arguments);
        Assert.AreEqual("run -", (project with { RunArguments = null }).CreateStartInfo("Console.WriteLine();").Arguments);
        Assert.AreEqual("Console.WriteLine();", project.CreateStandardInput("Console.WriteLine();"));
        Assert.IsNull(new DotNetRunProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\" }.CreateStandardInput("greet"));
    }

    [TestMethod]
    public void DotNetFilePassesScriptsAroundCommand()
    {
        var project = new DotNetFileProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", PrependScript = "using System.Text;", AppendScript = "Console.WriteLine();" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);
        var deserialized = Assert.IsInstanceOfType<DotNetFileProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));

        Assert.AreEqual("using System.Text;\nvar a = 1;\nConsole.WriteLine();", deserialized.CreateStandardInput("var a = 1;"));
        Assert.AreEqual("var a = 1;", (deserialized with { PrependScript = null, AppendScript = "" }).CreateStandardInput("var a = 1;"));
    }

    [TestMethod]
    public void PythonRoundTrips()
    {
        var project = new PythonProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", Arguments = "-X dev", PrependScript = "import json", AppendScript = "print()" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"python\"", json);
        var deserialized = Assert.IsInstanceOfType<PythonProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
        Assert.AreEqual(project, deserialized with { EnvironmentVariables = project.EnvironmentVariables, Scripts = project.Scripts });
        Assert.IsInstanceOfType<PythonProject>((InterpreterProject)deserialized with { Arguments = null });
    }

    [TestMethod]
    public void NodeRoundTrips()
    {
        var project = new NodeProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", Arguments = "--input-type=module" };

        var json = JsonSerializer.Serialize<Project>(project, AppJsonSerializerContext.Default.Project);

        Assert.Contains("\"$type\": \"node\"", json);
        var deserialized = Assert.IsInstanceOfType<NodeProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
        Assert.AreEqual("--input-type=module", deserialized.Arguments);
    }

    [TestMethod]
    public void InterpreterPassesCommandOnStandardInput()
    {
        var project = new NodeProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\", Arguments = "--input-type=module", PrependScript = "const a = 1;", AppendScript = "console.log(a);" };

        var startInfo = project.CreateStartInfo("let b = 2;");

        Assert.AreEqual("node", startInfo.FileName);
        Assert.AreEqual("--input-type=module -", startInfo.Arguments);
        Assert.AreEqual("const a = 1;\nlet b = 2;\nconsole.log(a);", project.CreateStandardInput("let b = 2;"));
        Assert.AreEqual("-", (project with { Arguments = null }).CreateStartInfo("let b = 2;").Arguments);
    }

    // The output would be written by the code page of the system, and held back, otherwise.
    [TestMethod]
    public void PythonIsRunWithUtf8Unbuffered()
    {
        var startInfo = new PythonProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\" }.CreateStartInfo("print()");

        Assert.AreEqual("python", startInfo.FileName);
        Assert.AreEqual("-", startInfo.Arguments);
        Assert.AreEqual("1", startInfo.Environment["PYTHONUTF8"]);
        Assert.AreEqual("1", startInfo.Environment["PYTHONUNBUFFERED"]);
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
        Assert.IsTrue(Assert.IsInstanceOfType<LocalProject>(deserialized).LoadEnvFile);
    }

    [TestMethod]
    public void PersistHistoryRoundTrips()
    {
        Project project = new PowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = @"C:\",
            PersistHistory = true,
        };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsNotNull(deserialized);
        Assert.IsTrue(deserialized.PersistHistory);
    }

    [TestMethod]
    public void HistoryRoundTrips()
    {
        IReadOnlyList<HistoryEntry> history =
        [
            new(
                Command: "dir",
                IsElevated: true,
                StartedAt: new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(1)),
                Output: [new(OutputStream.StandardOutput, "a\n"), new(OutputStream.StandardError, "b\n")],
                ExitCode: 1,
                WasStopped: false,
                Duration: TimeSpan.FromMilliseconds(1234),
                IsPinned: true),
            new(
                Command: "sleep 10",
                IsElevated: false,
                StartedAt: DateTimeOffset.UnixEpoch,
                Output: null,
                ExitCode: null,
                WasStopped: true,
                Duration: TimeSpan.Zero,
                IsPinned: false),
        ];

        var json = JsonSerializer.Serialize(history, AppJsonSerializerContext.Default.IReadOnlyListHistoryEntry);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.IReadOnlyListHistoryEntry);

        Assert.IsNotNull(deserialized);
        Assert.Contains("\"standardError\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.HasCount(2, deserialized);
        Assert.AreEqual(history[0] with { Output = null }, deserialized[0] with { Output = null });
        Assert.AreSequenceEqual(history[0].Output!, deserialized[0].Output!);
        Assert.AreEqual(history[1], deserialized[1]);
    }
}
