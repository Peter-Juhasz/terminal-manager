using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run by the Command Prompt (cmd) in the developer environment of the latest Visual Studio, as in Developer Command Prompt for VS,
/// so the build tools, such as msbuild and cl, can be used.
/// </summary>
/// <remarks>
/// The environment is set up again for each command, which takes about a second.
/// </remarks>
public sealed record class DeveloperCmdProject : CmdProject
{
    // vswhere is put on the path first, as VsDevCmd calls it too. Build Tools and previews are found as well.
    // The check is grouped, as the rest of the line would only be run when it fails otherwise; it is made at run time, by "if defined".
    private static readonly string DevCmdPrelude =
        "set \"PATH=%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer;%PATH%\" & " +
        $"(for /f \"usebackq delims=\" %v in (`vswhere -latest -prerelease -products * -find Common7\\Tools\\VsDevCmd.bat 2^>nul`) do @call \"%v\" {VisualStudioEnvironment.DevCmdArguments}) & " +
        "(if not defined VSINSTALLDIR >&2 echo Visual Studio was not found.& exit /b 1) & ";

    private protected override string Prelude => base.Prelude + DevCmdPrelude;

    [JsonIgnore]
    public override string TerminalDisplayName => "Developer Command Prompt";
}
