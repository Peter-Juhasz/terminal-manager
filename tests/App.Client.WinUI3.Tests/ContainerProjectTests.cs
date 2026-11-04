using System.Diagnostics;
using System.Text;
using System.Text.Json;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ContainerProjectTests(TestContext testContext)
{
    [TestMethod]
    [DataRow(typeof(DockerShellProject), "dockersh")]
    [DataRow(typeof(DockerPowerShellProject), "dockerpwsh")]
    [DataRow(typeof(KubectlShellProject), "kubectlsh")]
    [DataRow(typeof(KubectlPowerShellProject), "kubectlpwsh")]
    public void RoundTrips(Type type, string discriminator)
    {
        Project project = type.Name switch
        {
            nameof(DockerShellProject) => new DockerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Container = "web", User = "node", PrependScript = "set -e" },
            nameof(DockerPowerShellProject) => new DockerPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Container = "web", Context = "remote" },
            nameof(KubectlShellProject) => new KubectlShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Pod = "deploy/web", Namespace = "prod", Container = "app" },
            _ => new KubectlPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Pod = "web-0", Context = "aks" },
        };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project);

        Assert.IsInstanceOfType(deserialized, type);
        Assert.Contains($"\"$type\": \"{discriminator}\"", json);
        Assert.DoesNotContain("loadEnvFile", json);
        Assert.AreEqual(project, deserialized! with { Scripts = null });
    }

    [TestMethod]
    public void DockerRunsShellInContainer()
    {
        var project = new DockerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = "/srv/it's here",
            Container = "web",
            EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "a 'b'" },
            AppendScript = "echo done",
        };

        var startInfo = project.CreateStartInfo("echo hi\r\necho there");

        Assert.AreEqual("docker", startInfo.FileName);
        Assert.AreSequenceEqual(
            (string[])["exec", "web", "sh", "-c", "cd -- '/srv/it'\\''s here' || exit\nexport GREETING='a '\\''b'\\'''\necho hi\necho there\necho done\n"],
            startInfo.ArgumentList.ToArray());
        Assert.AreEqual("web: /srv/it's here", project.LocationDisplayName);
    }

    [TestMethod]
    public void DockerPassesContextAndUser()
    {
        var project = new DockerPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Container = "web", Context = "remote", User = "1000:1000" };

        var arguments = project.CreateStartInfo("Get-Location").ArgumentList.ToArray();

        Assert.AreSequenceEqual((string[])["--context", "remote", "exec", "--user", "1000:1000", "web", "pwsh"], arguments[..7]);
    }

    [TestMethod]
    public void KubectlRunsShellInPod()
    {
        var project = new KubectlShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "~/app", Pod = "deploy/web" };

        var startInfo = project.CreateStartInfo("ls");

        Assert.AreEqual("kubectl", startInfo.FileName);
        Assert.AreSequenceEqual((string[])["exec", "deploy/web", "--", "sh", "-c", "cd -- ~/'app' || exit\nls\n"], startInfo.ArgumentList.ToArray());
        Assert.AreEqual("deploy/web: ~/app", project.LocationDisplayName);
    }

    [TestMethod]
    public void KubectlPassesContextNamespaceAndContainer()
    {
        var project = new KubectlPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/app", Pod = "web-0", Context = "aks", Namespace = "prod", Container = "app" };

        var arguments = project.CreateStartInfo("Get-Location").ArgumentList.ToArray();

        Assert.AreSequenceEqual((string[])["exec", "--context", "aks", "--namespace", "prod", "--container", "app", "web-0", "--", "pwsh"], arguments[..10]);
    }

    [TestMethod]
    [DataRow("Write-Output hi", 0, "hi\n")]
    [DataRow("Write-Output hi; cmd /c exit 3", 3, "hi\n")]
    [DataRow("throw 'boom'", 1, "")]
    public async Task PowerShellScriptRunsInFolderWithVariables(string command, int expectedExitCode, string expectedOutput)
    {
        if (!IsOnPath("pwsh"))
        {
            Assert.Inconclusive("pwsh isn't installed.");
        }

        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var project = new DockerPowerShellProject
            {
                Id = Guid.NewGuid(),
                DisplayName = "A",
                WorkingFolder = folder.FullName,
                Container = "web",
                EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "it's" },
                PrependScript = "if ($env:GREETING -ne \"it's\" -or (Get-Location).Path -ne '" + folder.FullName + "') { throw 'wrong' }",
            };

            // what is run in the container, run here instead
            var (exitCode, standardOutput, standardError) = await RunInPlaceOfContainerAsync(project.CreateStartInfo(command));

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
    public async Task PowerShellScriptStopsWhenFolderIsMissing()
    {
        if (!IsOnPath("pwsh"))
        {
            Assert.Inconclusive("pwsh isn't installed.");
        }

        var project = new KubectlPowerShellProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Pod = "web-0" };

        var (exitCode, standardOutput, _) = await RunInPlaceOfContainerAsync(project.CreateStartInfo("Write-Output ran"));

        Assert.AreNotEqual(0, exitCode);
        Assert.DoesNotContain("ran", standardOutput);
    }

    // runs what follows the arguments of the client, which is what it runs in the container
    private async Task<(int ExitCode, string StandardOutput, string StandardError)> RunInPlaceOfContainerAsync(ProcessStartInfo clientStartInfo)
    {
        var arguments = clientStartInfo.ArgumentList.ToList();
        var shell = arguments.IndexOf("pwsh");
        var startInfo = new ProcessStartInfo(arguments[shell]);
        foreach (var argument in arguments.Skip(shell + 1))
        {
            startInfo.ArgumentList.Add(argument);
        }

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
