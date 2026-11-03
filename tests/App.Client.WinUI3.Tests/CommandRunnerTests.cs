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

    // Needs Visual Studio or its Build Tools, so it is skipped where neither is installed.
    [TestMethod]
    public async Task RunAsyncRunsInDeveloperCommandPrompt()
    {
        if (!File.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe")))
        {
            Assert.Inconclusive("Visual Studio isn't installed.");
        }

        var project = new DeveloperCmdProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
        };

        var result = await RunAsync(project, "if defined VSINSTALLDIR (echo yes) & cd & exit /b 3");

        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual($"yes\r\n{Path.TrimEndingDirectorySeparator(Path.GetTempPath())}\r\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    // Where vswhere can't be found, the command isn't run outside the developer environment.
    [TestMethod]
    public async Task RunAsyncFailsWithoutVisualStudio()
    {
        var project = new DeveloperCmdProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
            EnvironmentVariables = new Dictionary<string, string> { ["ProgramFiles(x86)"] = @"C:\nowhere" },
        };

        var result = await RunAsync(project, "echo ran");

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual("Visual Studio was not found.\r\n", result.Error);
    }

    // A file-based app is run, so the command must reach it as its arguments, and its output must still be read as UTF-8.
    [TestMethod]
    public async Task RunAsyncRunsDotNetApp()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "app.cs"), """
                foreach (var arg in args) System.Console.WriteLine(arg);
                System.Console.WriteLine("aé€");
                return 3;
                """);
            var project = new DotNetRunProject
            {
                Id = Guid.NewGuid(),
                DisplayName = "Test",
                WorkingFolder = folder.FullName,
                RunArguments = "app.cs",
            };

            var result = await RunAsync(project, "greet \"a b\"");

            Assert.AreEqual(3, result.ExitCode, result.Output + result.Error);
            Assert.AreEqual("greet\r\na b\r\naé€\r\n", result.Output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task RunAsyncWritesStandardInput()
    {
        var result = await RunAsync(new StandardInputProject { Id = Guid.NewGuid(), DisplayName = "Test", WorkingFolder = Path.GetTempPath(), Input = "a\r\nb\r\n" }, "findstr \"^\"");

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("a\r\nb\r\n", result.Output);
    }

    // More than the pipes hold each way, so it is only read to the end when the output is read while the input is written.
    [TestMethod]
    public async Task RunAsyncWritesStandardInputLongerThanPipe()
    {
        var input = string.Concat(Enumerable.Range(1, 20000).Select(static index => $"line {index}\r\n"));

        var result = await RunAsync(new StandardInputProject { Id = Guid.NewGuid(), DisplayName = "Test", WorkingFolder = Path.GetTempPath(), Input = input }, "findstr \"^\"");

        Assert.AreEqual(input, result.Output);
    }

    [TestMethod]
    public async Task RunAsyncIgnoresStandardInputNotRead()
    {
        var result = await RunAsync(new StandardInputProject { Id = Guid.NewGuid(), DisplayName = "Test", WorkingFolder = Path.GetTempPath(), Input = new string('a', 1_000_000) }, "exit /b 3");

        Assert.AreEqual(3, result.ExitCode);
    }

    // The code is read from the standard input, which must be read as UTF-8, as the output must.
    [TestMethod]
    public async Task RunAsyncRunsDotNetFileBasedApp()
    {
        var project = new DotNetFileProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
            RunArguments = "-- greet",
        };

        var result = await RunAsync(project, """
            foreach (var arg in args) System.Console.WriteLine(arg);
            System.Console.WriteLine("aé€");
            return 3;
            """);

        Assert.AreEqual(3, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("greet\r\naé€\r\n", result.Output);
    }

    // The directives of the prepend script must still come first in the file, as they would be errors after the command otherwise.
    [TestMethod]
    public async Task RunAsyncRunsDotNetFileBasedAppWithScripts()
    {
        var project = new DotNetFileProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
            PrependScript = "#:property Nullable=enable\nusing System.Text;",
            AppendScript = "Console.WriteLine(\"after\");",
        };

        var result = await RunAsync(project, "Console.WriteLine(new StringBuilder(\"command\"));");

        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("command\r\nafter\r\n", result.Output);
    }

    // Needs Python, so it is skipped where it isn't installed.
    [TestMethod]
    public async Task RunAsyncRunsPython()
    {
        if (!IsOnPath("python"))
        {
            Assert.Inconclusive("Python isn't installed.");
        }

        var project = new PythonProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
            PrependScript = "import sys",
            AppendScript = "sys.exit(3)",
        };

        var result = await RunAsync(project, "print('aé€')");

        Assert.AreEqual(3, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("aé€\r\n", result.Output);
    }

    // Needs Node.js, so it is skipped where it isn't installed.
    [TestMethod]
    public async Task RunAsyncRunsNode()
    {
        if (!IsOnPath("node"))
        {
            Assert.Inconclusive("Node.js isn't installed.");
        }

        var project = new NodeProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = Path.GetTempPath(),
            PrependScript = "const text = 'aé€';",
            AppendScript = "process.exitCode = 3;",
        };

        var result = await RunAsync(project, "console.log(text);");

        Assert.AreEqual(3, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("aé€\n", result.Output);
    }

    // The python in WindowsApps only offers to install it from the Store, so it isn't taken for it.
    private static bool IsOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static folder => !folder.Contains(@"\WindowsApps", StringComparison.OrdinalIgnoreCase))
            .Any(folder => File.Exists(Path.Combine(folder, name + ".exe")));

    private sealed record class StandardInputProject : CmdProject
    {
        public required string Input { get; init; }

        public override string? CreateStandardInput(string command) => Input;
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
