using System.Diagnostics;
using System.Text;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

// Runs commands by the Command Prompt, so these only run on Windows, as the app does.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed class CommandRunnerTests(TestContext testContext)
{
    [TestMethod]
    public async Task RunAsyncReadsStandardOutput()
    {
        var result = await RunAsync("echo hello");

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("hello\r\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task RunAsyncReadsStandardError()
    {
        var result = await RunAsync("echo oops>&2");

        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual("oops\r\n", result.Error);
    }

    [TestMethod]
    public async Task RunAsyncReturnsExitCode()
    {
        var result = await RunAsync("exit /b 3");

        Assert.AreEqual(3, result.ExitCode);
    }

    [TestMethod]
    public async Task RunAsyncReadsAllOfLongOutput()
    {
        var result = await RunAsync("for /l %i in (1,1,2000) do @echo line %i");

        Assert.AreEqual(string.Concat(Enumerable.Range(1, 2000).Select(static index => $"line {index}\r\n")), result.Output);
    }

    // The characters take 1 to 4 bytes, so some are split between the reads.
    [TestMethod]
    public async Task RunAsyncDecodesUtf8()
    {
        var text = string.Concat(Enumerable.Repeat("aé€😀\r\n", 2000));

        var result = await RunWithFileAsync("type output.txt", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

        Assert.AreEqual(text, result.Output);
    }

    [TestMethod]
    public async Task RunAsyncSkipsByteOrderMark()
    {
        var result = await RunWithFileAsync("type output.txt", [0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n']);

        Assert.AreEqual("a\r\n", result.Output);
    }

    [TestMethod]
    public async Task RunAsyncTakesProgressOutOfOutput()
    {
        var result = await RunWithFileAsync("type output.txt", Encoding.UTF8.GetBytes("a\e]9;4;1;50\ab\r\n"));

        Assert.AreEqual("ab\r\n", result.Output);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 50), result.Progress);
    }

    [TestMethod]
    public async Task RunAsyncStopsCommandWhenCanceled()
    {
        var start = Stopwatch.GetTimestamp();

        await RunAsync("ping -n 30 127.0.0.1 >nul", cancelAfter: TimeSpan.FromMilliseconds(500));

        Assert.IsLessThan(TimeSpan.FromSeconds(10), Stopwatch.GetElapsedTime(start));
    }

    // The process started in the background keeps the output open for 5 seconds after the command ended.
    [TestMethod]
    public async Task RunAsyncDoesNotWaitForOutputOfProcessesStartedByCommand()
    {
        var start = Stopwatch.GetTimestamp();

        var result = await RunAsync("start /b ping -n 6 127.0.0.1 & echo started");

        Assert.IsLessThan(TimeSpan.FromSeconds(4), Stopwatch.GetElapsedTime(start));
        Assert.Contains("started", result.Output);
    }

    // The prelude must not fail on what only pwsh has, and the output must still be read as UTF-8.
    [TestMethod]
    public async Task RunAsyncRunsWindowsPowerShell()
    {
        var project = new WindowsPowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
        };

        var result = await RunAsync(project, "$PSVersionTable.PSVersion.Major; 'aé€'; exit 3");

        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual("5\r\naé€\r\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    // Needs Visual Studio or its Build Tools, so it is skipped where neither is installed.
    [TestMethod]
    public async Task RunAsyncRunsInDeveloperEnvironment()
    {
        if (!File.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe")))
        {
            Assert.Inconclusive("Visual Studio isn't installed.");
        }

        var project = new DeveloperPowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
        };

        var result = await RunAsync(project, "[bool]$env:VSINSTALLDIR; [bool](Get-Variable vs -ErrorAction Ignore); (Get-Location).Path");

        Assert.AreEqual($"True\r\nFalse\r\n{Path.TrimEndingDirectorySeparator(Path.GetTempPath())}\r\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    private async Task<CommandResult> RunWithFileAsync(string command, byte[] file)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "output.txt"), file);
            return await RunAsync(command, folder.FullName);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private Task<CommandResult> RunAsync(string command, string? workingFolder = null, TimeSpan? cancelAfter = null) =>
        RunAsync(new CmdProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = workingFolder ?? Path.GetTempPath(),
        }, command, cancelAfter);

    private async Task<CommandResult> RunAsync(Project project, string command, TimeSpan? cancelAfter = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        if (cancelAfter is { } delay)
        {
            stop.CancelAfter(delay);
        }

        var output = new OutputBuffer();
        var reading = ReadToEndAsync(output);
        var exitCode = await new CommandRunner().RunAsync(project, command, output, stop.Token);
        var (text, error, progress) = await reading;
        return new CommandResult(exitCode, text, error, progress);
    }

    private async Task<(string Output, string Error, ProgressReport Progress)> ReadToEndAsync(OutputBuffer output)
    {
        var text = new StringBuilder();
        var error = new StringBuilder();
        var progress = new ProgressReport(ProgressState.None, 0);
        while (await output.WaitToReadAsync(testContext.CancellationToken))
        {
            var batch = output.Read();
            foreach (var chunk in batch.Chunks)
            {
                (chunk.Stream is OutputStream.StandardOutput ? text : error).Append(chunk.Text);
            }

            progress = batch.Progress;
        }

        return (text.ToString(), error.ToString(), progress);
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error, ProgressReport Progress);
}
