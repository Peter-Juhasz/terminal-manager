using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are JavaScript code, each run as a script by <c>node -</c>.
/// </summary>
/// <remarks>
/// Node.js always writes its output as UTF-8, so it needs no environment variables of its own.
/// </remarks>
public sealed record class NodeProject : InterpreterProject
{
    private protected override string FileName => "node";

    [JsonIgnore]
    public override string TerminalDisplayName => "Node.js";

    private static readonly Uri IconUri = new("ms-appx:///Assets/node.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;
}
