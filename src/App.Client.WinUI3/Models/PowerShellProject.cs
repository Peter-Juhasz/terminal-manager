using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by PowerShell (pwsh).
/// </summary>
public sealed record class PowerShellProject : Project
{
    // The output is read as UTF-8 without colors or progress bars, which are only meant for a console.
    private const string Prelude = """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $PSStyle.OutputRendering = 'PlainText'
        $ProgressPreference = 'SilentlyContinue'

        """;

    [JsonIgnore]
    public override string TerminalDisplayName => "PowerShell";

    private static readonly Uri IconUri = new("ms-appx:///Assets/pwsh.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A single argument, which is quoted for the command line as needed, so the command reaches PowerShell as it was typed.
        // Not encoded, as errors would be written as CLIXML then.
        return new ProcessStartInfo("pwsh")
        {
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command", Prelude + command },
        };
    }
}
