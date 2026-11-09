using System.Text;
using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run by the shell of another computer, over a connection of Telnet of their own, which logs in as <see cref="User"/> first, when there is one.
/// </summary>
/// <remarks>
/// <para>
/// The shell is expected to be a POSIX one, such as <c>bash</c> or <c>sh</c>: the echo of the terminal is turned off, and the prompts are emptied, before the command is sent,
/// and the shell writes markers before and after it, so only what the command writes is shown, and its exit code is known.
/// </para>
/// <para>
/// The password is kept in the Credential Manager of Windows, by <see cref="CredentialTarget"/>, never in the project. Telnet isn't encrypted, so the password,
/// the commands and what they write can be read by anyone on the network.
/// </para>
/// <para>
/// What the command writes to its standard error reaches the terminal it is run in too, so it is shown as standard output. Stopping a command closes the connection,
/// which hangs up the shell, and the command with it.
/// </para>
/// </remarks>
public sealed record class TelnetProject : Project
{
    /// <summary>The port Telnet is served on, unless another one is told.</summary>
    public const int DefaultPort = 23;

    /// <summary>The name or address of the computer commands are run on.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>The port Telnet is served on, or <see langword="null"/> for <see cref="DefaultPort"/>.</summary>
    public int? Port { get; init; }

    /// <summary>The user that is logged in as, or <see langword="null"/> when the computer doesn't ask for one, and the shell is started right away.</summary>
    public string? User { get; init; }

    [JsonIgnore]
    public int EffectivePort => Port ?? DefaultPort;

    /// <summary>The name of the credential in the Credential Manager of Windows that the password of <see cref="User"/> on <see cref="Host"/> is kept by, shared by the projects that log in the same way.</summary>
    [JsonIgnore]
    public string CredentialTarget => $"terminal-manager:telnet:{User}@{Host}:{EffectivePort}";

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Shell;

    [JsonIgnore]
    public override string TerminalDisplayName => "Telnet (shell)";

    // there is no logo of telnet itself, so it is shown with the generic icon
    [JsonIgnore]
    public override Uri? TerminalIconUri => null;

    [JsonIgnore]
    public override string LocationDisplayName => Port switch
    {
        null or DefaultPort => $"{Host}: {WorkingFolder}",
        var port => $"{Host}:{port}: {WorkingFolder}",
    };

    /// <summary>
    /// The line that is sent first, once logged in: it turns off the echo of the terminal, empties the prompts, so neither is mixed into the output,
    /// then writes <paramref name="startMarker"/> on a line of its own.
    /// </summary>
    /// <remarks>
    /// The marker is written in two parts joined by <c>printf</c>, so the echo of the line itself, which comes before the echo is turned off, isn't taken for it.
    /// </remarks>
    public static string CreateSetupLine(string startMarker)
    {
        ArgumentException.ThrowIfNullOrEmpty(startMarker);

        var (first, second) = Split(startMarker);
        return $"stty -echo 2>/dev/null; PS1=''; PS2=''; PROMPT_COMMAND=''; unset HISTFILE; printf '%s%s\\n' {ShellScript.Quote(first)} {ShellScript.Quote(second)}\n";
    }

    /// <summary>
    /// The script sent once <see cref="CreateSetupLine"/> took effect, for <paramref name="command"/>: in a subshell, with no input, so a command that reads it doesn't read the rest of the script,
    /// it changes to the working folder, sets the environment variables, and runs <see cref="Project.PrependScript"/>, the command and <see cref="Project.AppendScript"/>, each on lines of their own;
    /// then it writes <paramref name="exitCodeMarker"/>, at the start of a line of its own, followed by the exit code, and logs out.
    /// </summary>
    public string CreateScript(string command, string exitCodeMarker)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrEmpty(exitCodeMarker);

        // the computer may not take carriage returns as the end of a line, which text boxes end them with
        var body = Wrap(command).ReplaceLineEndings("\n");
        var (first, second) = Split(exitCodeMarker);
        return new StringBuilder()
            .Append("(\n")
            .AppendShellPreamble(WorkingFolder, ExpandedEnvironmentVariables)
            .Append(body).Append('\n')
            .Append(") </dev/null\n")
            .Append("printf '\\n%s%s%d\\n' ").Append(ShellScript.Quote(first)).Append(' ').Append(ShellScript.Quote(second)).Append(" \"$?\"\n")
            .Append("exit\n")
            .ToString();
    }

    private static (string First, string Second) Split(string marker) => (marker[..(marker.Length / 2)], marker[(marker.Length / 2)..]);
}
