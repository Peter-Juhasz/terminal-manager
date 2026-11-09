using System.Diagnostics;
using System.Text;
using System.Text.Json;
using App.Domain;
using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class PowerShellRemotingProjectTests(TestContext testContext)
{
    [TestMethod]
    public void RoundTrips()
    {
        Project project = new PowerShellRemotingProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\app", ComputerName = "server01", PrependScript = "Import-Module ./build.psm1" };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsInstanceOfType<PowerShellRemotingProject>(deserialized);
        Assert.Contains("\"$type\": \"pwshremote\"", json);
        Assert.Contains("\"computerName\": \"server01\"", json);
        Assert.AreEqual(project, deserialized! with { Scripts = null });
    }

    [TestMethod]
    public void OpensSessionToComputer()
    {
        var project = new PowerShellRemotingProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = @"C:\app", ComputerName = "it's-server" };

        var startInfo = project.CreateStartInfo("Get-Location");

        Assert.AreEqual("pwsh", startInfo.FileName);
        Assert.AreSequenceEqual((string[])["-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command"], startInfo.ArgumentList.ToArray()[..6]);
        Assert.Contains("New-PSSession -ComputerName 'it''s-server' -ErrorAction Stop", startInfo.ArgumentList[6]);
        Assert.AreEqual(@"it's-server: C:\app", project.LocationDisplayName);
    }

    [TestMethod]
    [DataRow("Write-Output hi", 0, "hi\n")]
    [DataRow("Write-Output hi; cmd /c exit 3", 3, "hi\n")]
    [DataRow("throw 'boom'", 1, "")]
    [DataRow("Write-Error 'boom'", 1, "")]
    public async Task ScriptRunsInFolderWithVariables(string command, int expectedExitCode, string expectedOutput)
    {
        if (!IsOnPath("pwsh"))
        {
            Assert.Inconclusive("pwsh isn't installed.");
        }

        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var project = new PowerShellRemotingProject
            {
                Id = Guid.NewGuid(),
                DisplayName = "A",
                WorkingFolder = folder.FullName,
                ComputerName = "server01",
                EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "it's" },
                PrependScript = "if ($env:GREETING -ne \"it's\" -or (Get-Location).Path -ne '" + folder.FullName + "') { throw 'wrong' }",
            };

            var (exitCode, standardOutput, standardError) = await RunWithoutSessionAsync(project.CreateStartInfo(command));

            Assert.AreEqual(expectedExitCode, exitCode, standardError);
            Assert.AreEqual(expectedOutput, standardOutput.ReplaceLineEndings("\n"));
            Assert.AreEqual(expectedExitCode == 1, standardError.Contains("boom"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task ScriptStopsWhenFolderIsMissing()
    {
        if (!IsOnPath("pwsh"))
        {
            Assert.Inconclusive("pwsh isn't installed.");
        }

        var project = new PowerShellRemotingProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ComputerName = "server01" };

        var (exitCode, standardOutput, _) = await RunWithoutSessionAsync(project.CreateStartInfo("Write-Output ran"));

        Assert.AreEqual(1, exitCode);
        Assert.DoesNotContain("ran", standardOutput);
    }

    // runs the script on this machine, with the session left out, as opening one to this machine needs remoting to be enabled, and the app to be elevated.
    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunWithoutSessionAsync(ProcessStartInfo startInfo)
    {
        var script = startInfo.ArgumentList[^1];
        var newSession = script.Split('\n').Single(static line => line.StartsWith("$__terminal_manager_session = New-PSSession", StringComparison.Ordinal));
        startInfo.ArgumentList[^1] = script
            .Replace(newSession, "$__terminal_manager_session = $null", StringComparison.Ordinal)
            .Replace("-Session $__terminal_manager_session ", string.Empty, StringComparison.Ordinal)
            .Replace("Remove-PSSession -Session $__terminal_manager_session\n", string.Empty, StringComparison.Ordinal);

        var output = new OutputBuffer();
        var reading = ReadToEndAsync(output);
        var exitCode = await ProcessRunner.RunAsync(startInfo, input: null, output, testContext.CancellationToken);
        output.Complete();
        var (standardOutput, standardError) = await reading;
        return (exitCode, standardOutput, standardError);
    }

    private static bool IsOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, name + ".exe")));

    private static async Task<(string StandardOutput, string StandardError)> ReadToEndAsync(OutputBuffer output)
    {
        var (standardOutput, standardError) = (new StringBuilder(), new StringBuilder());
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
