using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are Python code, each run as a script by <c>python -</c>.
/// </summary>
/// <remarks>
/// The code before and after each command is put at the start of lines of their own, so it must not be indented.
/// </remarks>
public sealed record class PythonProject : InterpreterProject
{
    private protected override string FileName => "python";

    // the output is written as UTF-8 rather than by the code page of the system,
    // and as it is printed, as it would be held back until a buffer is full otherwise, since it isn't written to a console.
    private protected override IEnumerable<KeyValuePair<string, string>> InterpreterEnvironment => (KeyValuePair<string, string>[])
    [
        new("PYTHONUTF8", "1"),
        new("PYTHONUNBUFFERED", "1"),
    ];

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Python;

    [JsonIgnore]
    public override string TerminalDisplayName => "Python";

    private static readonly Uri IconUri = new("ms-appx:///Assets/python.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;
}
