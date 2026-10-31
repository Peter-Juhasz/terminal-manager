using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Windows.Win32;

namespace App.Client.WinUI3.Services;

/// <summary>
/// A copy of the app started as an administrator, which runs the commands the app asks it to, until the app closes.
/// </summary>
/// <remarks>
/// The app waits for it on a pipe it created before starting it, which only one can connect to, and only the user and administrators can open.
/// Each end checks the process at the other, so a command is only run for the app that started the host, and only by the host it started.
/// </remarks>
public static class ElevatedHost
{
    /// <summary>The argument the app is started with as the host, followed by the name of the pipe, and the id of the app's process.</summary>
    public const string Argument = "--elevated-host";

    // How long the host has to connect once it was allowed to run.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    // Returned by starting a process as an administrator, when the user didn't allow it.
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Starts the host, which the user is asked to allow, and connects to it.
    /// Blocks until the user answered, so it is best called on a thread of its own.
    /// </summary>
    /// <exception cref="Win32Exception">The user didn't allow it, or it couldn't be started.</exception>
    /// <exception cref="InvalidOperationException">It exited, or didn't connect in time.</exception>
    public static async Task<ElevationClient> StartAsync(CancellationToken cancellationToken)
    {
        var pipeName = string.Create(CultureInfo.InvariantCulture, $"Terminal.Elevated.{Guid.NewGuid():N}");
        var server = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0,
            outBufferSize: 0,
            CreatePipeSecurity());
        try
        {
            using var host = StartProcess(pipeName);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            var connecting = server.WaitForConnectionAsync(timeout.Token);
            var exiting = host.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connecting, exiting).ConfigureAwait(false) != connecting)
            {
                throw new InvalidOperationException("The elevated host exited before it connected to the app.");
            }

            try
            {
                await connecting.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("The elevated host didn't connect to the app in time.");
            }

            if (!PInvoke.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientId) || clientId != (uint)host.Id)
            {
                throw new InvalidOperationException("A process other than the elevated host connected to the app.");
            }

            return new ElevationClient(server);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static Process StartProcess(string pipeName)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the app is unknown, so it can't be started as an administrator.");
        try
        {
            return Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Create(CultureInfo.InvariantCulture, $"{Argument} {pipeName} {Environment.ProcessId}"),
            }) ?? throw new InvalidOperationException("The elevated host couldn't be started.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is ErrorCancelled)
        {
            throw new Win32Exception(ex.NativeErrorCode, "The command wasn't run, as running it as an administrator wasn't allowed.");
        }
    }

    // The user, and administrators, as the host may be run by another user, who allowed it with their own credentials.
    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The user of the app is unknown.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Runs as the host: connects to the app that started it, and runs the commands it asks for, until it closes.
    /// </summary>
    /// <param name="args">The arguments that follow <see cref="Argument"/>.</param>
    /// <returns>The exit code of the host.</returns>
    public static int Run(ReadOnlySpan<string> args)
    {
        if (args is not [var pipeName, var appId] || !int.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var appProcessId))
        {
            return 1;
        }

        var connection = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            connection.Connect(ConnectTimeout);

            // Only commands of the app it was started by are run, which holds the only instance there can be of the pipe.
            if (!PInvoke.GetNamedPipeServerProcessId(connection.SafePipeHandle, out var serverId) || serverId != (uint)appProcessId)
            {
                connection.Dispose();
                return 2;
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            connection.Dispose();
            return 3;
        }

        // Blocked on, as the host has no UI to keep responding.
        new ElevationServer(connection, new CommandRunner()).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }
}
