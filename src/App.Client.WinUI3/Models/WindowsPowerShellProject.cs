using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are run by Windows PowerShell (powershell), the version 5.1 that comes with Windows.
/// </summary>
/// <remarks>
/// Commands are completed and highlighted by pwsh, as for <see cref="PowerShellProject"/>, so what only Windows PowerShell has may not be completed.
/// </remarks>
public sealed record class WindowsPowerShellProject : PowerShellProject
{
    // $PSStyle was added in PowerShell 7.2, and Windows PowerShell doesn't color its output anyway.
    private protected override string Prelude => EncodingPrelude;

    private protected override string FileName => "powershell";

    [JsonIgnore]
    public override string TerminalDisplayName => "Windows PowerShell";

    [JsonIgnore]
    public override Uri? TerminalIconUri => null;
}
