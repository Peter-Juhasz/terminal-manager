using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by the Command Prompt (cmd).
/// </summary>
public sealed record class CmdProject : Project
{
    [JsonIgnore]
    public override string TerminalDisplayName => "Command Prompt";

    [JsonIgnore]
    public override Uri? TerminalIconUri => null;

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The command line is built by hand, as cmd doesn't unescape quotes the way ArgumentList escapes them.
        // With /s, only the outermost quotes are stripped, so the command reaches cmd as it was typed.
        // The code page is switched first, as the output is read as UTF-8; AutoRun commands are skipped by /d.
        return new ProcessStartInfo("cmd")
        {
            Arguments = $"/d /s /c \"chcp 65001 >nul & {command}\"",
        };
    }
}
