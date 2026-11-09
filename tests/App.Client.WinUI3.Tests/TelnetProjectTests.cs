using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using App.Domain;
using App.Extensions.Telnet;
using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed partial class TelnetProjectTests(TestContext testContext)
{
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte Echo = 1;
    private const byte SuppressGoAhead = 3;
    private const byte TerminalType = 24;
    private const byte WindowSize = 31;

    [TestMethod]
    public void RoundTrips()
    {
        Project project = new TelnetProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "~/app", Host = "router", Port = 2323, User = "admin" };

        var json = JsonSerializer.Serialize(project, AppJsonSerializerContext.Default.Project);
        var deserialized = Assert.IsInstanceOfType<TelnetProject>(JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.Project));

        Assert.Contains("\"$type\": \"telnet\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(project, deserialized with { Scripts = null });
        Assert.AreEqual("router:2323: ~/app", deserialized.LocationDisplayName);
        Assert.AreEqual("terminal-manager:telnet:admin@router:2323", deserialized.CredentialTarget);
    }

    [TestMethod]
    public void DefaultPortIsLeftOut()
    {
        var project = new TelnetProject { Id = Guid.NewGuid(), DisplayName = "A", WorkingFolder = "/srv", Host = "router" };

        Assert.AreEqual(23, project.EffectivePort);
        Assert.AreEqual("router: /srv", project.LocationDisplayName);
    }

    [TestMethod]
    public void SetupLineDoesNotEchoMarker()
    {
        const string Marker = "__start_0123456789";

        var line = TelnetProject.CreateSetupLine(Marker);

        Assert.StartsWith("stty -echo", line);
        Assert.EndsWith("\n", line);
        Assert.DoesNotContain(Marker, line);
    }

    [TestMethod]
    public void ScriptRunsInSubshellWithoutInput()
    {
        var project = new TelnetProject
        {
            Id = Guid.NewGuid(),
            DisplayName = "A",
            WorkingFolder = "/srv/it's here",
            Host = "router",
            EnvironmentVariables = new Dictionary<string, string> { ["GREETING"] = "hi" },
            AppendScript = "echo done",
        };

        var script = project.CreateScript("echo a\r\necho b", "__exit_0123456789=");

        Assert.AreEqual(
            "(\ncd -- '/srv/it'\\''s here' || exit\nexport GREETING='hi'\necho a\necho b\necho done\n) </dev/null\nprintf '\\n%s%s%d\\n' '__exit_01' '23456789=' \"$?\"\nexit\n",
            script);
    }

    [TestMethod]
    public void ProtocolAnswersOptionsOnce()
    {
        var protocol = new TelnetProtocol();
        var (data, reply) = (new ArrayBufferWriter<byte>(), new ArrayBufferWriter<byte>());

        protocol.Read([Iac, Do, TerminalType, Iac, Will, Echo, Iac, Do, WindowSize, Iac, Will, SuppressGoAhead, Iac, Do, TerminalType, Iac, Do, WindowSize], data, reply);

        Assert.AreEqual(0, data.WrittenCount);
        Assert.AreSequenceEqual((byte[])[Iac, Will, TerminalType, Iac, Do, Echo, Iac, Wont, WindowSize, Iac, Do, SuppressGoAhead], reply.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void ProtocolTellsTerminalType()
    {
        var protocol = new TelnetProtocol();
        var (data, reply) = (new ArrayBufferWriter<byte>(), new ArrayBufferWriter<byte>());

        // split, as it may be read in parts
        protocol.Read([Iac, Do, TerminalType, Iac, Sb], data, reply);
        protocol.Read([TerminalType, 1, Iac, Se], data, reply);

        Assert.AreSequenceEqual((byte[])[Iac, Will, TerminalType, Iac, Sb, TerminalType, 0, .. "dumb"u8, Iac, Se], reply.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void ProtocolKeepsData()
    {
        var protocol = new TelnetProtocol();
        var (data, reply) = (new ArrayBufferWriter<byte>(), new ArrayBufferWriter<byte>());

        protocol.Read([(byte)'a', Iac, Iac, (byte)'b', (byte)'\r', 0, (byte)'c', (byte)'\r', (byte)'\n', Iac, 249, (byte)'d'], data, reply);

        Assert.AreSequenceEqual((byte[])[(byte)'a', Iac, (byte)'b', (byte)'\r', (byte)'c', (byte)'\r', (byte)'\n', (byte)'d'], data.WrittenSpan.ToArray());
        Assert.AreEqual(0, reply.WrittenCount);
    }

    [TestMethod]
    public void EncodeEndsLinesAsTelnet() => Assert.AreSequenceEqual("a\r\nb\r\n"u8.ToArray(), TelnetProtocol.Encode("a\r\nb\n"));

    [TestMethod]
    public void ScannerSkipsPreambleAndExitCode()
    {
        var scanner = new TelnetOutputScanner("START", "EXIT=");

        Assert.AreEqual(string.Empty, scanner.Read("login: me\r\n$ printf ... STA"));
        Assert.Contains("login: me", scanner.Preamble);
        Assert.AreEqual(string.Empty, scanner.Read("RT\r"));
        Assert.IsFalse(scanner.IsStarted);
        Assert.AreEqual("hel", scanner.Read("\nhel"));
        Assert.IsTrue(scanner.IsStarted);
        Assert.AreEqual("lo", scanner.Read("lo\r"));
        Assert.AreEqual("\n50%\r100%\n", scanner.Read("\n50%\r100%\r\n\r\nEX"));
        Assert.AreEqual(string.Empty, scanner.Read("IT=4"));
        Assert.IsNull(scanner.ExitCode);
        Assert.AreEqual(string.Empty, scanner.Read("2\r\nlogout\r\n"));
        Assert.AreEqual(42, scanner.ExitCode);
    }

    [TestMethod]
    public void ScannerFlushesWhenClosedEarly()
    {
        var scanner = new TelnetOutputScanner("START", "EXIT=");

        Assert.AreEqual("partial", scanner.Read("START\r\npartial\r"));
        Assert.AreEqual("\r", scanner.Flush());
    }

    [TestMethod]
    public async Task RunsCommandAfterLogin()
    {
        var result = await RunAgainstServerAsync(password: "secret", user: "me", async (client, expect) =>
        {
            await client.SendAsync([Iac, Do, TerminalType, Iac, Will, Echo, .. "Ubuntu\r\nlogin: "u8]);
            Assert.AreEqual("me", await client.ReadLineAsync());
            await client.SendAsync("Password: "u8.ToArray());
            Assert.AreEqual("secret", await client.ReadLineAsync());
            await client.SendAsync("\r\nLast login: today\r\n$ "u8.ToArray());
            await client.StartShellAsync();

            var script = await client.ReadScriptAsync();
            Assert.Contains("cd -- '/srv/app' || exit", script);
            Assert.Contains("echo hello", script);
            await client.SendAsync(Encoding.UTF8.GetBytes($"hello\r\nwörld\r\n\r\n{ExitCodeMarker(script)}3\r\nlogout\r\n"));

            Assert.Contains((Will, TerminalType), client.Negotiations);
            Assert.Contains((Do, Echo), client.Negotiations);
        });

        Assert.AreEqual(3, result.ExitCode, result.Error);
        Assert.AreEqual("hello\nwörld\n", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task RunsCommandWithoutLogin()
    {
        var result = await RunAgainstServerAsync(password: null, user: null, async (client, _) =>
        {
            await client.SendAsync("# "u8.ToArray());
            await client.StartShellAsync();
            var script = await client.ReadScriptAsync();
            await client.SendAsync(Encoding.UTF8.GetBytes($"ok\r\n{ExitCodeMarker(script)}0\r\n"));
        });

        Assert.AreEqual(0, result.ExitCode, result.Error);
        Assert.AreEqual("ok", result.Output);
    }

    [TestMethod]
    public async Task FailsWhenLoginIsIncorrect()
    {
        var result = await RunAgainstServerAsync(password: "wrong", user: "me", async (client, _) =>
        {
            await client.SendAsync("login: "u8.ToArray());
            await client.ReadLineAsync();
            await client.SendAsync("Password: "u8.ToArray());
            await client.ReadLineAsync();
            await client.SendAsync("\r\nLogin incorrect\r\nlogin: "u8.ToArray());
            await client.ReadLineAsync();
        });

        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("Login incorrect", result.Output);
        Assert.Contains("failed", result.Error);
    }

    [TestMethod]
    public async Task FailsWhenPasswordIsMissing()
    {
        var result = await RunAgainstServerAsync(password: null, user: "me", async (client, _) =>
        {
            await client.SendAsync("login: "u8.ToArray());
            await client.ReadLineAsync();
            await client.SendAsync("Password: "u8.ToArray());
            await client.ReadLineAsync();
        });

        Assert.AreEqual(1, result.ExitCode);
        Assert.Contains("none is saved", result.Error);
    }

    [TestMethod]
    public async Task FailsWhenClosedBeforeExitCode()
    {
        var result = await RunAgainstServerAsync(password: null, user: null, async (client, _) =>
        {
            await client.StartShellAsync();
            await client.ReadScriptAsync();
            await client.SendAsync("partial\r\n"u8.ToArray());
        });

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("partial\n", result.Output);
        Assert.Contains("closed", result.Error);
    }

    [TestMethod]
    public async Task FailsWhenNothingListens()
    {
        // a port that was just free, so nothing listens on it
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var output = new OutputBuffer();
        var reading = ReadToEndAsync(output);
        var exitCode = await TelnetCommandRunner.RunAsync(CreateProject(port, user: null), "echo hi", password: null, output, testContext.CancellationToken);
        output.Complete();
        var (_, error) = await reading;

        Assert.AreEqual(1, exitCode);
        Assert.Contains("connection to 127.0.0.1", error);
    }

    private static TelnetProject CreateProject(int port, string? user) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "A",
        WorkingFolder = "/srv/app",
        Host = "127.0.0.1",
        Port = port,
        User = user,
    };

    // as the script writes it: printf '\n%s%s%d\n' 'first half' 'second half' "$?"
    private static string ExitCodeMarker(string script)
    {
        var match = ExitCodePrintf().Match(script);
        Assert.IsTrue(match.Success, script);
        return match.Groups[1].Value + match.Groups[2].Value;
    }

    [GeneratedRegex(@"printf '\\n%s%s%d\\n' '([^']*)' '([^']*)'")]
    private static partial Regex ExitCodePrintf();

    [GeneratedRegex(@"printf '%s%s\\n' '([^']*)' '([^']*)'")]
    private static partial Regex StartPrintf();

    private async Task<(int ExitCode, string Output, string Error)> RunAgainstServerAsync(string? password, string? user, Func<FakeClient, CancellationToken, Task> serve)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(testContext.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(30));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var serving = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(stop.Token);
                await using var stream = socket.GetStream();
                await serve(new FakeClient(stream, stop.Token), stop.Token);
            }, stop.Token);

            var output = new OutputBuffer();
            var reading = ReadToEndAsync(output);
            var exitCode = await TelnetCommandRunner.RunAsync(CreateProject(((IPEndPoint)listener.LocalEndpoint).Port, user), "echo hello", password, output, stop.Token);
            output.Complete();
            await serving;
            var (text, error) = await reading;
            return (exitCode, text, error);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<(string Output, string Error)> ReadToEndAsync(OutputBuffer output)
    {
        var (text, error) = (new StringBuilder(), new StringBuilder());
        while (await output.WaitToReadAsync(CancellationToken.None))
        {
            foreach (var chunk in output.Read().Chunks)
            {
                (chunk.Stream is OutputStream.StandardOutput ? text : error).Append(chunk.Text);
            }
        }

        return (text.ToString(), error.ToString());
    }

    /// <summary>The server side of the connection, as a terminal on it reads what the client typed, with the commands of the protocol taken out.</summary>
    private sealed class FakeClient(NetworkStream stream, CancellationToken cancellationToken)
    {
        public List<(byte Verb, byte Option)> Negotiations { get; } = [];

        public async Task SendAsync(byte[] bytes) => await stream.WriteAsync(bytes, cancellationToken);

        // a line typed in, which ends with a carriage return and a line feed
        public async Task<string> ReadLineAsync()
        {
            var line = new List<byte>();
            while (true)
            {
                var value = await ReadByteAsync();
                if (value is Iac)
                {
                    var verb = await ReadByteAsync();
                    if (verb is Will or Wont or Do or Dont)
                    {
                        Negotiations.Add((verb, await ReadByteAsync()));
                    }
                    else if (verb is Sb)
                    {
                        while (await ReadByteAsync() is not Se)
                        {
                        }
                    }

                    continue;
                }

                if (value is (byte)'\n' && line is [.., (byte)'\r'])
                {
                    return Encoding.UTF8.GetString(line.ToArray(), 0, line.Count - 1);
                }

                line.Add(value);
            }
        }

        // reads the setup line, echoes it, as the terminal does before its echo is turned off, then writes the start marker, as printf does
        public async Task StartShellAsync()
        {
            var line = await ReadLineAsync();
            Assert.StartsWith("stty -echo", line);
            await SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"));
            var match = StartPrintf().Match(line);
            Assert.IsTrue(match.Success, line);
            await SendAsync(Encoding.UTF8.GetBytes(match.Groups[1].Value + match.Groups[2].Value + "\r\n"));
        }

        public async Task<string> ReadScriptAsync()
        {
            var script = new StringBuilder();
            string line;
            do
            {
                line = await ReadLineAsync();
                script.Append(line).Append('\n');
            }
            while (line is not "exit");

            return script.ToString();
        }

        private async Task<byte> ReadByteAsync()
        {
            var buffer = new byte[1];
            if (await stream.ReadAsync(buffer, cancellationToken) == 0)
            {
                throw new EndOfStreamException();
            }

            return buffer[0];
        }
    }
}
