using System.Diagnostics;
using System.Text.Json;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class AzureVmProjectTests(TestContext testContext)
{
    private const string Marker = "__exit=";

    [TestMethod]
    public void RoundTrips()
    {
        Project project = new AzureVmShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = "/srv/app",
            Subscription = "My subscription",
            ResourceGroup = "rg",
            VmName = "vm",
            PrependScript = "set -e",
        };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = Assert.IsInstanceOfType<AzureVmShellProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));

        Assert.Contains("\"$type\": \"azvmshell\"", json);
        Assert.DoesNotContain("loadEnvFile", json);
        Assert.AreEqual(project, deserialized with { Scripts = null });
        Assert.AreEqual("vm: /srv/app", deserialized.LocationDisplayName);
    }

    [TestMethod]
    public void PowerShellRoundTrips()
    {
        Project project = new AzureVmPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\app", ResourceGroup = "rg", VmName = "vm" };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);

        Assert.IsInstanceOfType<AzureVmPowerShellProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));
        Assert.DoesNotContain("subscription", json);
    }

    [TestMethod]
    public void ShellScriptChangesToFolderFirst()
    {
        var project = new AzureVmShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = "/srv/it's here",
            EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "a 'b'" },
            AppendScript = "echo done",
        };

        var script = project.CreateScript("echo hi\r\necho there", Marker);

        Assert.AreEqual(
            "(\ncd -- '/srv/it'\\''s here' || exit\nexport GREETING='a '\\''b'\\'''\necho hi\necho there\necho done\n)\nprintf '\\n%s%d\\n' '__exit=' \"$?\"\n",
            script);
    }

    [TestMethod]
    public void ShellScriptExpandsHomeFolder()
    {
        var project = new AzureVmShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "~/my app" };

        Assert.Contains("cd -- ~/'my app' || exit\n", project.CreateScript("ls", Marker));
        Assert.Contains("cd -- ~ || exit\n", (project with { WorkingFolder = "~" }).CreateScript("ls", Marker));
    }

    [TestMethod]
    public void PowerShellScriptQuotesAllSingleQuotes()
    {
        Assert.AreEqual("'it''s ‘‘a’’'", AzureVmPowerShellProject.Quote("it's ‘a’"));
    }

    [TestMethod]
    [DataRow("Write-Output hi", 0, "hi\n")]
    [DataRow("Write-Output hi; cmd /c exit 3", 3, "hi\n")]
    [DataRow("throw 'boom'", 1, "")]
    public async Task PowerShellScriptReportsExitCode(string command, int expectedExitCode, string expectedOutput)
    {
        if (!IsOnPath("pwsh"))
        {
            Assert.Inconclusive("pwsh isn't installed.");
        }

        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var project = new AzureVmPowerShellProject
            {
                Id = Guid.NewGuid(),
                DisplayName = "A",
                WorkingFolder = folder.FullName,
                EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "it's" },
            };
            var scriptPath = Path.Combine(folder.FullName, "script.ps1");
            File.WriteAllText(scriptPath, project.CreateScript(command + "\nif ($env:GREETING -ne \"it's\" -or (Get-Location).Path -ne '" + folder.FullName + "') { throw 'wrong' }", Marker));

            // run as the run command runs it, and reported as a Windows machine reports it
            var output = new OutputBuffer();
            var reading = ReadToEndAsync(output);
            await ProcessRunner.RunAsync(new ProcessStartInfo("pwsh") { ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", scriptPath } }, input: null, output, testContext.CancellationToken);
            output.Complete();
            var (standardOutput, standardError) = await reading;
            var json = JsonSerializer.Serialize(new { value = new[] { new { code = "ComponentStatus/StdOut/succeeded", message = standardOutput }, new { code = "ComponentStatus/StdErr/succeeded", message = standardError } } });

            var result = AzureVmCommandRunner.ParseResult(json, Marker);

            Assert.AreEqual(expectedExitCode, result.ExitCode, result.StandardError);
            Assert.AreEqual(expectedOutput, result.StandardOutput.ReplaceLineEndings("\n"));
            Assert.AreEqual(expectedExitCode == 1, result.StandardError.Contains("boom"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LinuxResultIsSplitIntoStreams()
    {
        const string json = """
            {"value":[{"code":"ProvisioningState/succeeded","displayStatus":"Provisioning succeeded","level":"Info","message":"Enable succeeded: \n[stdout]\nhello\n\n__exit=2\n\n[stderr]\noops\n","time":null}]}
            """;

        var result = AzureVmCommandRunner.ParseResult(json, Marker);

        Assert.AreEqual(new RunCommandResult(StandardOutput: "hello\n", StandardError: "oops\n", ExitCode: 2), result);
    }

    [TestMethod]
    public void LinuxFailureIsReportedWithoutMarker()
    {
        const string json = """
            {"value":[{"code":"ProvisioningState/failed","message":"Enable failed: script timed out\n[stdout]\npartial\n[stderr]\n"}]}
            """;

        var result = AzureVmCommandRunner.ParseResult(json, Marker);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("partial", result.StandardOutput);
        Assert.AreEqual("Enable failed: script timed out\n", result.StandardError);
    }

    [TestMethod]
    public void UnreadableResultIsShownAsItIs()
    {
        var result = AzureVmCommandRunner.ParseResult("not json", Marker);

        Assert.AreEqual(new RunCommandResult(StandardOutput: "not json", StandardError: string.Empty, ExitCode: 0), result);
    }

    [TestMethod]
    public void AzureCliIsRunWithTheMachine()
    {
        var project = new AzureVmPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\app", Subscription = "My sub", ResourceGroup = "rg", VmName = "vm" };

        var startInfo = AzureVmCommandRunner.CreateStartInfo(project, @"C:\Temp\a b.ps1");

        Assert.AreEqual("cmd", startInfo.FileName);
        Assert.AreEqual(
            "/d /s /c \"az vm run-command invoke --command-id RunPowerShellScript --subscription \"My sub\" --resource-group \"rg\" --name \"vm\" --scripts \"@C:\\Temp\\a b.ps1\" --output json --only-show-errors\"",
            startInfo.Arguments);
        Assert.DoesNotContain("--subscription", AzureVmCommandRunner.CreateStartInfo(project with { Subscription = null }, "a.ps1").Arguments);
    }

    private static bool IsOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, name + ".exe")));

    private static async Task<(string StandardOutput, string StandardError)> ReadToEndAsync(OutputBuffer output)
    {
        var (standardOutput, standardError) = (new System.Text.StringBuilder(), new System.Text.StringBuilder());
        while (await output.WaitToReadAsync(CancellationToken.None))
        {
            foreach (var chunk in output.Read().Chunks)
            {
                (chunk.Stream is OutputStream.StandardOutput ? standardOutput : standardError).Append(chunk.Text);
            }
        }

        return (standardOutput.ToString(), standardError.ToString());
    }
}
