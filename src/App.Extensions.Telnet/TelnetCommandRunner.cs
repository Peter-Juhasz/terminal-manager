using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using App.Domain;
using App.Services;

namespace App.Extensions.Telnet;

/// <summary>
/// Runs the commands of a <see cref="TelnetProject"/> on its computer, each over a connection of its own: it logs in, when the project has a user,
/// sends <see cref="TelnetProject.CreateSetupLine"/>, waits for the shell to write the start marker, then sends <see cref="TelnetProject.CreateScript"/>,
/// and shows what the command writes as it is read, until the shell writes its exit code.
/// </summary>
/// <remarks>
/// The output of the command is all written to the standard output, as the terminal it is run in mixes both of its streams; why it couldn't be run,
/// such as that the computer couldn't be reached, or the login failed, is written to the standard error, with 1 as the exit code.
/// </remarks>
public static partial class TelnetCommandRunner
{
    /// <summary>How long the login, until the shell wrote the start marker, may take, before it is given up on.</summary>
    internal static TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // how much of what the computer wrote before the shell started is shown, when it didn't
    private const int MaxPreambleLength = 2000;

    private const int ReadBufferSize = 4096;

    private enum Phase
    {
        User,
        Password,
        Starting,
        Running,
    }

    /// <inheritdoc cref="ICommandRunner.RunAsync"/>
    /// <param name="password">The password of the user, or <see langword="null"/> when none is kept, so it is only logged in by the user.</param>
    /// <remarks>The output isn't completed, so more can be written to it after.</remarks>
    public static async Task<int> RunAsync(TelnetProject project, string command, string? password, IOutputSink output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(project.Host, project.EffectivePort, cancellationToken).ConfigureAwait(false);
            await using var stream = client.GetStream();
            return await RunAsync(stream, project, command, password, output, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the connection is closed, which hangs up the shell, and the command with it
            return -1;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            output.Write(OutputStream.StandardError, $"The connection to {project.Host}:{project.EffectivePort} failed: {ex.Message}\n");
            return 1;
        }
    }

    /// <summary>Runs <paramref name="command"/> over <paramref name="stream"/>, which is connected to the Telnet server of <paramref name="project"/>.</summary>
    internal static async Task<int> RunAsync(Stream stream, TelnetProject project, string command, string? password, IOutputSink output, CancellationToken cancellationToken)
    {
        // unique to the run, so output that happens to look like them isn't taken for them
        var id = Guid.NewGuid().ToString("N");
        var startMarker = $"__terminal_manager_start_{id}";
        var exitCodeMarker = $"__terminal_manager_exit_code_{id}=";

        var protocol = new TelnetProtocol();
        var scanner = new TelnetOutputScanner(startMarker, exitCodeMarker);
        var decoder = Encoding.UTF8.GetDecoder();
        var data = new ArrayBufferWriter<byte>(ReadBufferSize);
        var reply = new ArrayBufferWriter<byte>();
        var buffer = new byte[ReadBufferSize];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(ReadBufferSize)];

        // the login, until the shell starts, is given up on after a while, while the command may run for as long as it takes
        using var login = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        login.CancelAfter(LoginTimeout);

        var phase = project.User is { Length: > 0 } ? Phase.User : Phase.Starting;
        if (phase is Phase.Starting)
        {
            await SendAsync(stream, TelnetProject.CreateSetupLine(startMarker), cancellationToken).ConfigureAwait(false);
        }

        // what was read since the last prompt answered, which the next one is looked for in
        var screen = new StringBuilder();
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, phase is Phase.Running ? cancellationToken : login.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail(output, $"The shell didn't start in {LoginTimeout.TotalSeconds:0} seconds.", scanner.Preamble);
            }

            if (read == 0)
            {
                if (phase is not Phase.Running)
                {
                    return Fail(output, "The computer closed the connection before the shell started.", scanner.Preamble);
                }

                Write(output, scanner.Flush());
                output.Write(OutputStream.StandardError, "The connection was closed before the command ended.\n");
                return 1;
            }

            data.ResetWrittenCount();
            reply.ResetWrittenCount();
            protocol.Read(buffer.AsSpan(0, read), data, reply);
            if (reply.WrittenCount > 0)
            {
                await stream.WriteAsync(reply.WrittenMemory, cancellationToken).ConfigureAwait(false);
            }

            var text = new string(chars, 0, decoder.GetChars(data.WrittenSpan, chars, flush: false));
            screen.Append(text);
            var written = scanner.Read(text);

            switch (phase)
            {
                case Phase.User when UserPrompt().IsMatch(screen.ToString()):
                    screen.Clear();
                    await SendAsync(stream, project.User + "\n", cancellationToken).ConfigureAwait(false);
                    phase = Phase.Password;

                    // without a password, the shell is expected to start right after the user
                    if (password is null)
                    {
                        await SendAsync(stream, TelnetProject.CreateSetupLine(startMarker), cancellationToken).ConfigureAwait(false);
                        phase = Phase.Starting;
                    }

                    break;

                case Phase.Password when PasswordPrompt().IsMatch(screen.ToString()):
                    screen.Clear();
                    await SendAsync(stream, password + "\n", cancellationToken).ConfigureAwait(false);
                    await SendAsync(stream, TelnetProject.CreateSetupLine(startMarker), cancellationToken).ConfigureAwait(false);
                    phase = Phase.Starting;
                    break;

                // a password asked for when none is kept would take the lines sent next as one
                case Phase.Starting when password is null && PasswordPrompt().IsMatch(screen.ToString()):
                    return Fail(output, $"{project.Host} asked for a password, but none is saved for {project.User}. Edit the project to save one.", scanner.Preamble);

                // asked again, as the user or the password was wrong
                case Phase.Starting when !scanner.IsStarted && UserPrompt().IsMatch(screen.ToString()):
                    return Fail(output, $"The login to {project.Host} as {project.User} failed.", scanner.Preamble);

                case Phase.Starting when scanner.IsStarted:
                    phase = Phase.Running;
                    await SendAsync(stream, project.CreateScript(command, exitCodeMarker), cancellationToken).ConfigureAwait(false);
                    break;
            }

            Write(output, written);
            if (scanner.ExitCode is { } exitCode)
            {
                return exitCode;
            }
        }
    }

    private static async Task SendAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(TelnetProtocol.Encode(text), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int Fail(IOutputSink output, string message, string preamble)
    {
        // what the computer wrote last tells why, such as that the login was incorrect
        var shown = preamble.Length > MaxPreambleLength ? preamble[^MaxPreambleLength..] : preamble;
        Write(output, shown.Replace("\r\n", "\n", StringComparison.Ordinal));
        output.Write(OutputStream.StandardError, (shown is "" or [.., '\n'] ? string.Empty : "\n") + message + "\n");
        return 1;
    }

    private static void Write(IOutputSink output, string text)
    {
        if (text.Length > 0)
        {
            output.Write(OutputStream.StandardOutput, text);
        }
    }

    // as login and getty ask, and others, at the end of what was read
    [GeneratedRegex(@"(login|user ?name|user)\s*:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserPrompt();

    [GeneratedRegex(@"password\s*:\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordPrompt();
}
