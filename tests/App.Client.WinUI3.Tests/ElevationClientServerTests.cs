using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

// The app and the host are connected by a named pipe in the same process, as they are across processes, but without elevation.
// The commands are run by the Command Prompt, so these only run on Windows, as the app does.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed class ElevationClientServerTests(TestContext testContext)
{
    [TestMethod]
    public async Task RunAsyncReadsStandardOutputAndError()
    {
        await using var connection = await ConnectAsync(new CommandRunner());

        var result = await RunAsync(connection.Client, "echo hello & echo oops>&2");

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("hello \r\n", result.Output);
        Assert.AreEqual("oops\r\n", result.Error);
    }

    [TestMethod]
    public async Task RunAsyncReturnsExitCode()
    {
        await using var connection = await ConnectAsync(new CommandRunner());

        var result = await RunAsync(connection.Client, "exit /b 3");

        Assert.AreEqual(3, result.ExitCode);
    }

    [TestMethod]
    public async Task RunAsyncReadsAllOfLongOutput()
    {
        await using var connection = await ConnectAsync(new CommandRunner());

        var result = await RunAsync(connection.Client, "for /l %i in (1,1,2000) do @echo line %i");

        Assert.AreEqual(string.Concat(Enumerable.Range(1, 2000).Select(static index => $"line {index}\r\n")), result.Output);
    }

    // The progress sequences are passed on as they were written, and taken out of the output by the app.
    [TestMethod]
    public async Task RunAsyncPassesProgressOn()
    {
        await using var connection = await ConnectAsync(new FakeRunner(static output =>
        {
            output.Write(OutputStream.StandardOutput, "a\e]9;4;1;");
            output.Write(OutputStream.StandardOutput, "50\ab");
            return 0;
        }));

        var result = await RunAsync(connection.Client, "progress");

        Assert.AreEqual("ab", result.Output);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 50), result.Progress);
    }

    [TestMethod]
    public async Task RunAsyncPassesUnicodeOn()
    {
        var text = string.Concat(Enumerable.Repeat("aé€😀\r\n", 5000));
        await using var connection = await ConnectAsync(new FakeRunner(output =>
        {
            output.Write(OutputStream.StandardOutput, text);
            return 0;
        }));

        var result = await RunAsync(connection.Client, "unicode");

        Assert.AreEqual(text, result.Output);
    }

    [TestMethod]
    public async Task RunAsyncPassesProjectAndCommandOn()
    {
        Project? received = null;
        string? receivedCommand = null;
        await using var connection = await ConnectAsync(new FakeRunner((project, command, _) =>
        {
            received = project;
            receivedCommand = command;
            return 0;
        }));
        var project = new PowerShellProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test",
            WorkingFolder = @"C:\Work",
            Scripts = [new SavedScript { Id = Guid.NewGuid(), Name = "Build", Command = "dotnet build" }],
        };

        await connection.Client.RunAsync(project, "Write-Output 'é'", new OutputBuffer(), testContext.CancellationToken);

        var powerShell = received as PowerShellProject;
        Assert.IsNotNull(powerShell);
        Assert.AreEqual(project.Id, powerShell.Id);
        Assert.AreEqual(project.WorkingFolder, powerShell.WorkingFolder);
        Assert.AreEqual("dotnet build", powerShell.Scripts.Single().Command);
        Assert.AreEqual("Write-Output 'é'", receivedCommand);
    }

    [TestMethod]
    public async Task RunAsyncRunsCommandsAtTheSameTime()
    {
        await using var connection = await ConnectAsync(new CommandRunner());
        var start = Stopwatch.GetTimestamp();

        var results = await Task.WhenAll(Enumerable.Range(1, 4).Select(index => RunAsync(connection.Client, $"ping -n 3 127.0.0.1 >nul & echo {index}")));

        // Each takes 2 seconds, so they would take 8 one after the other.
        Assert.IsLessThan(TimeSpan.FromSeconds(6), Stopwatch.GetElapsedTime(start));
        Assert.AreSequenceEqual(new[] { "1\r\n", "2\r\n", "3\r\n", "4\r\n" }, results.Select(static result => result.Output));
    }

    [TestMethod]
    public async Task RunAsyncStopsCommandWhenCanceled()
    {
        await using var connection = await ConnectAsync(new CommandRunner());
        var start = Stopwatch.GetTimestamp();

        await RunAsync(connection.Client, "ping -n 30 127.0.0.1 >nul", cancelAfter: TimeSpan.FromMilliseconds(500));

        Assert.IsLessThan(TimeSpan.FromSeconds(10), Stopwatch.GetElapsedTime(start));
    }

    [TestMethod]
    public async Task RunAsyncKeepsRunningOthersWhenOneIsCanceled()
    {
        await using var connection = await ConnectAsync(new CommandRunner());

        var stopped = RunAsync(connection.Client, "ping -n 30 127.0.0.1 >nul", cancelAfter: TimeSpan.FromMilliseconds(300));
        var other = RunAsync(connection.Client, "ping -n 2 127.0.0.1 >nul & echo done");
        await stopped;

        Assert.AreEqual("done\r\n", (await other).Output);
    }

    [TestMethod]
    public async Task RunAsyncThrowsWhenHostCouldNotStartCommand()
    {
        await using var connection = await ConnectAsync(new CommandRunner());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RunAsync(connection.Client, "echo hello", workingFolder: @"C:\does\not\exist\" + Guid.NewGuid()));

        Assert.IsFalse(string.IsNullOrEmpty(exception.Message));
        Assert.IsTrue(connection.Client.IsConnected);
    }

    [TestMethod]
    public async Task RunAsyncCompletesOutputWhenHostCouldNotStartCommand()
    {
        await using var connection = await ConnectAsync(new FakeRunner(static _ => throw new InvalidOperationException("nope")));
        var output = new OutputBuffer();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => connection.Client.RunAsync(CreateProject(), "x", output, testContext.CancellationToken));

        Assert.IsTrue(output.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task RunAsyncThrowsWhenConnectionIsClosedWhileRunning()
    {
        await using var connection = await ConnectAsync(new CommandRunner());
        var running = RunAsync(connection.Client, "ping -n 30 127.0.0.1 >nul");
        await Task.Delay(300, testContext.CancellationToken);

        await connection.CloseHostEndAsync();

        await Assert.ThrowsExactlyAsync<IOException>(() => running);
        Assert.IsFalse(connection.Client.IsConnected);
        await Assert.ThrowsExactlyAsync<IOException>(() => RunAsync(connection.Client, "echo hello"));
    }

    // The host stops what it runs once the app closed, as there is no one left to show the output to, or to stop it.
    [TestMethod]
    public async Task ServerStopsCommandsWhenClientDisconnects()
    {
        await using var connection = await ConnectAsync(new CommandRunner());
        var running = RunAsync(connection.Client, "ping -n 30 127.0.0.1 >nul");
        await Task.Delay(300, testContext.CancellationToken);
        var start = Stopwatch.GetTimestamp();

        await connection.Client.DisposeAsync();

        await connection.Server.WaitAsync(TimeSpan.FromSeconds(10), testContext.CancellationToken);
        Assert.IsLessThan(TimeSpan.FromSeconds(10), Stopwatch.GetElapsedTime(start));
        await Assert.ThrowsExactlyAsync<IOException>(() => running);
    }

    // What isn't understood ends the connection, rather than being guessed at.
    [TestMethod]
    public async Task ServerClosesConnectionOnCorruptFrame()
    {
        var name = $"Terminal.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(testContext.CancellationToken), client.ConnectAsync(testContext.CancellationToken));
        var serving = new ElevationServer(server, new CommandRunner()).RunAsync(testContext.CancellationToken);

        await client.WriteAsync(new byte[] { 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 }, testContext.CancellationToken);

        await serving.WaitAsync(TimeSpan.FromSeconds(10), testContext.CancellationToken);
        Assert.AreEqual(0, await client.ReadAsync(new byte[1], testContext.CancellationToken));
    }

    private async Task<Connection> ConnectAsync(ICommandRunner runner)
    {
        var name = $"Terminal.Tests.{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(testContext.CancellationToken), client.ConnectAsync(testContext.CancellationToken));

        // The app's end of the pipe is the server, as it is in the app, and the host's the client.
        return new Connection(new ElevationClient(server), client, new ElevationServer(client, runner).RunAsync(CancellationToken.None));
    }

    private async Task<CommandResult> RunAsync(ElevationClient client, string command, string? workingFolder = null, TimeSpan? cancelAfter = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        if (cancelAfter is { } delay)
        {
            stop.CancelAfter(delay);
        }

        var output = new OutputBuffer();
        var reading = ReadToEndAsync(output);
        var exitCode = await client.RunAsync(CreateProject(workingFolder), command, output, stop.Token);
        var (text, error, progress) = await reading;
        return new CommandResult(exitCode, text, error, progress);
    }

    private static CmdProject CreateProject(string? workingFolder = null) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Test",
        WorkingFolder = workingFolder ?? Path.GetTempPath(),
    };

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

    private sealed class Connection(ElevationClient client, Stream hostEnd, Task server) : IAsyncDisposable
    {
        public ElevationClient Client { get; } = client;

        public Task Server { get; } = server;

        // As if the host exited: its end is closed under it.
        public ValueTask CloseHostEndAsync() => hostEnd.DisposeAsync();

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server;
        }
    }

    // Writes what it is given, as a command would, without starting a process.
    private sealed class FakeRunner(Func<Project, string, IOutputSink, int> run) : ICommandRunner
    {
        public FakeRunner(Func<IOutputSink, int> run)
            : this((_, _, output) => run(output))
        {
        }

        public Task<int> RunAsync(Project project, string command, IOutputSink output, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(run(project, command, output));
            }
            finally
            {
                output.Complete();
            }
        }
    }
}
