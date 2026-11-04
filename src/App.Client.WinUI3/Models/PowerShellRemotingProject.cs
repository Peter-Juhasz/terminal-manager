using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run on another computer by PowerShell remoting: PowerShell (pwsh) on this machine opens a session to the computer for each command,
/// runs a script in it that changes to the working folder on the computer first, then closes the session.
/// </summary>
/// <remarks>
/// The computer needs to have remoting enabled, and the user of the app to be allowed to connect to it, as no credentials are asked for.
/// The output is shown as it is written, formatted on this machine, and the exit code is that of the script, which is read from the session after it.
/// Commands are run as the user of the app on the computer, so they can't be run as an administrator in addition.
/// Stopping a command stops PowerShell on this machine, but the command may keep running on the computer until its session times out.
/// </remarks>
public sealed record class PowerShellRemotingProject : Project
{
    // the output is read without progress bars or colors, which are only meant for a console.
    private const string LocalPrelude = """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'
        $PSStyle.OutputRendering = 'PlainText'

        """;

    /// <summary>The variable the script sets on the computer to whether the command succeeded, which it is left unset by when it is stopped by an error.</summary>
    private const string SucceededVariable = "$global:__terminal_manager_succeeded";

    /// <summary>The name of the computer commands are run on, as in <c>Invoke-Command -ComputerName</c>.</summary>
    public string ComputerName { get; init; } = string.Empty;

    /// <summary>The script run before each command, in the same scope, so what it defines can be used by the command, or <see langword="null"/> when there is none.</summary>
    public string? PrependScript { get; init; }

    /// <summary>The script run after each command, in the same scope, or <see langword="null"/> when there is none.</summary>
    public string? AppendScript { get; init; }

    /// <summary>
    /// What is put before a command when it is highlighted, so what <see cref="PrependScript"/> defines, or leaves open, is taken into account.
    /// </summary>
    [JsonIgnore]
    public string EditorPrefix => PrependScript is null or "" ? string.Empty : PrependScript + "\n";

    [JsonIgnore]
    public override string TerminalDisplayName => "PowerShell remoting";

    [JsonIgnore]
    public override string LocationDisplayName => $"{ComputerName}: {WorkingFolder}";

    private static readonly Uri IconUri = new("ms-appx:///Assets/pwsh.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    /// <summary>
    /// The script run on the computer for <paramref name="command"/>: it changes to the working folder, stopping when it can't, sets the environment variables,
    /// and runs <see cref="PrependScript"/>, the command and <see cref="AppendScript"/>, each on lines of their own, then tells whether the command succeeded,
    /// which, with <c>$LASTEXITCODE</c>, the exit code is told by.
    /// </summary>
    /// <remarks>The computer may run Windows PowerShell, which is why nothing newer is used.</remarks>
    public string CreateRemoteScript(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var body = ScriptWrapping.Wrap(PrependScript, command, AppendScript).ReplaceLineEndings("\n");
        return new StringBuilder()
            .Append(SucceededVariable).Append(" = $false\n")
            .Append("$ProgressPreference = 'SilentlyContinue'\n")
            .Append("$global:LASTEXITCODE = 0\n")
            .AppendPowerShellPreamble(WorkingFolder, ExpandedEnvironmentVariables)
            .Append(body).Append('\n')
            .Append(SucceededVariable).Append(" = $?\n")
            .ToString();
    }

    /// <summary>
    /// Describes the process of PowerShell on this machine that runs <paramref name="command"/> on the computer. The redirection of its streams is set by the caller.
    /// </summary>
    /// <remarks>
    /// PowerShell exits with the exit code of the last native command of the script on the computer, when it isn't 0, otherwise with 1 when the script failed,
    /// including when the session can't be opened.
    /// </remarks>
    public ProcessStartInfo CreateStartInfo(string command)
    {
        var script = new StringBuilder(LocalPrelude)
            .Append("$__terminal_manager_session = New-PSSession -ComputerName ").Append(PowerShellScript.Quote(ComputerName)).Append(" -ErrorAction Stop\n")
            .Append("try {\n")
            .Append("Invoke-Command -Session $__terminal_manager_session -ScriptBlock ([scriptblock]::Create(").Append(PowerShellScript.Quote(CreateRemoteScript(command))).Append("))\n")
            .Append("$__terminal_manager_status = Invoke-Command -Session $__terminal_manager_session -ScriptBlock { ").Append(SucceededVariable).Append(", $global:LASTEXITCODE }\n")
            .Append("}\n")
            .Append("finally {\n")
            .Append("Remove-PSSession -Session $__terminal_manager_session\n")
            .Append("}\n")
            .Append("if ($__terminal_manager_status[1]) { exit $__terminal_manager_status[1] }\n")
            .Append("if (-not $__terminal_manager_status[0]) { exit 1 }\n");

        // the working folder is on the computer, so PowerShell is run in one that is sure to exist here.
        return new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = Path.GetTempPath(),
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command", script.ToString() },
        };
    }
}
