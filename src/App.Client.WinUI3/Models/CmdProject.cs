using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by the Command Prompt (cmd), or in the developer environment of Visual Studio for <see cref="DeveloperCmdProject"/>.
/// </summary>
public record class CmdProject : LocalProject
{
    /// <summary>What is run before each command, ending with an <c>&amp;</c>; the code page is switched, as the output is read as UTF-8.</summary>
    private protected virtual string Prelude => "chcp 65001 >nul & ";

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Cmd;

    [JsonIgnore]
    public override string TerminalDisplayName => "Command Prompt";

    [JsonIgnore]
    public override Uri? TerminalIconUri => null;

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The command line is built by hand, as cmd doesn't unescape quotes the way ArgumentList escapes them.
        // With /s, only the outermost quotes are stripped, so the command reaches cmd as it was typed.
        // AutoRun commands are skipped by /d.
        return new ProcessStartInfo("cmd")
        {
            Arguments = $"/d /s /c \"{Prelude}{command}\"",
        };
    }
}
