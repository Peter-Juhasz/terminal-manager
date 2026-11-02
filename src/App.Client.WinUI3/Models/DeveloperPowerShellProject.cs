using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by PowerShell (pwsh) in the developer environment of the latest Visual Studio, as in Developer PowerShell for VS,
/// so the build tools, such as msbuild and cl, can be used.
/// </summary>
/// <remarks>
/// The environment is set up again for each command, which takes a second or two.
/// </remarks>
public sealed record class DeveloperPowerShellProject : PowerShellProject
{
    // the tools are for the machine, as VsDevCmd would build for and run on x86 otherwise.
    private static readonly string HostArchitecture = RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "amd64",
    };

    // vswhere is put on the path first, as VsDevCmd calls it too. Build Tools and previews are found as well.
    // In a script block, so its variables aren't left to the command.
    private static readonly string DevShellPrelude = $$"""
        & {
            $env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
            $vs = if (Get-Command vswhere -ErrorAction Ignore) { vswhere -latest -prerelease -products * -property installationPath }
            if (-not $vs) { throw 'Visual Studio was not found.' }
            Import-Module "$vs\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
            Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch={{HostArchitecture}} -host_arch={{HostArchitecture}} -no_logo'
        }

        """;

    private protected override string Prelude => base.Prelude + DevShellPrelude;

    [JsonIgnore]
    public override string TerminalDisplayName => "Developer PowerShell";

    [JsonIgnore]
    public override Uri? TerminalIconUri => null;
}
