using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are code, each run as a script by an interpreter that reads it from the standard input, as <c>-</c> is passed for its name,
/// such as Python for <see cref="PythonProject"/> and Node.js for <see cref="NodeProject"/>.
/// </summary>
public abstract record class InterpreterProject : LocalProject
{
    /// <summary>The executable the commands are run by.</summary>
    private protected abstract string FileName { get; }

    /// <summary>The environment variables the interpreter needs, so the output can be read, which those of the project can still change.</summary>
    private protected virtual IEnumerable<KeyValuePair<string, string>> InterpreterEnvironment => [];

    /// <summary>The options of the interpreter, put before the <c>-</c> that stands for the script, or <see langword="null"/> when there are none.</summary>
    public string? Arguments { get; init; }

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
