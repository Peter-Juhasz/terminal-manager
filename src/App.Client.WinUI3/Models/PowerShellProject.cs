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

    /// <summary>The script run before each command, in the same scope, so what it defines can be used by the command.</summary>
    // the source generated deserializer sets it to null when it is missing, as projects saved before it was added are.
    public string PrependScript { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>The script run after each command, in the same scope.</summary>
    public string AppendScript { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>
    /// What is put before a command when it is completed or parsed, so what <see cref="PrependScript"/> defines, or leaves open, is taken into account;
    /// positions in the command are this much further in the script.
    /// </summary>
    [JsonIgnore]
    public string EditorPrefix => PrependScript is "" ? string.Empty : PrependScript + "\n";

    /// <summary>The script that is run for <paramref name="command"/>, with <see cref="PrependScript"/> before it and <see cref="AppendScript"/> after it, each on lines of their own.</summary>
    public string Wrap(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AppendScript is "" ? EditorPrefix + command : EditorPrefix + command + "\n" + AppendScript;
    }

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
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command", Prelude + Wrap(command) },
        };
    }
}
