using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are code, each run as a script by an interpreter that reads it from the standard input, as <c>-</c> is passed for its name,
/// such as Python for <see cref="PythonProject"/> and Node.js for <see cref="NodeProject"/>.
/// </summary>
public abstract record class InterpreterProject : Project
{
    /// <summary>The executable the commands are run by.</summary>
    private protected abstract string FileName { get; }

    /// <summary>The environment variables the interpreter needs, so the output can be read, which those of the project can still change.</summary>
    private protected virtual IEnumerable<KeyValuePair<string, string>> InterpreterEnvironment => [];

    /// <summary>The options of the interpreter, put before the <c>-</c> that stands for the script, or <see langword="null"/> when there are none.</summary>
    public string? Arguments { get; init; }

    /// <summary>The code put before each command, in the same script, so what it defines can be used by the command, or <see langword="null"/> when there is none.</summary>
    public string? PrependScript { get; init; }

    /// <summary>The code put after each command, in the same script, or <see langword="null"/> when there is none.</summary>
    public string? AppendScript { get; init; }

    /// <summary>The code that is run for <paramref name="command"/>, with <see cref="PrependScript"/> before it and <see cref="AppendScript"/> after it, each on lines of their own.</summary>
    public string Wrap(string command) => ScriptWrapping.Wrap(PrependScript, command, AppendScript);

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // the options are put on the command line as they were typed, as an option after "-" would be passed to the script.
        var startInfo = new ProcessStartInfo(FileName)
        {
            Arguments = Arguments switch
            {
                null or "" => "-",
                var arguments => $"{arguments} -",
            },
        };

        foreach (var (name, value) in InterpreterEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    public override string? CreateStandardInput(string command) => Wrap(command);
}
