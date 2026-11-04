using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by PowerShell (pwsh), or by Windows PowerShell for <see cref="WindowsPowerShellProject"/>.
/// </summary>
public record class PowerShellProject : LocalProject
{
    /// <summary>The script run before everything else, so the output can be read.</summary>
    private protected virtual string Prelude => PowerShellScript.PlainTextPrelude;

    /// <summary>The executable the commands are run by.</summary>
    private protected virtual string FileName => "pwsh";

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.PowerShell;

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
        return new ProcessStartInfo(FileName)
        {
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command", Prelude + Wrap(command) },
        };
    }
}
