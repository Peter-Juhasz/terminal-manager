using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are C# code, each run as a file-based app by <c>dotnet run -</c>, which reads it from the standard input.
/// </summary>
/// <remarks>
/// Other files in the working folder, such as launch profiles, aren't used, but it is still the folder the app is built and run in.
/// </remarks>
public sealed record class DotNetFileProject : DotNetRunProject
{
    /// <summary>
    /// The code put before each command, in the same file, so what it declares, such as <c>#:</c> directives and <c>using</c> directives, applies to the command,
    /// or <see langword="null"/> when there is none.
    /// </summary>
    public string? PrependScript { get; init; }

    /// <summary>The code put after each command, in the same file, or <see langword="null"/> when there is none.</summary>
    public string? AppendScript { get; init; }

    /// <summary>The code that is run for <paramref name="command"/>, with <see cref="PrependScript"/> before it and <see cref="AppendScript"/> after it, each on lines of their own.</summary>
    public string Wrap(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var prefix = PrependScript is null or "" ? string.Empty : PrependScript + "\n";
        return AppendScript is null or "" ? prefix + command : prefix + command + "\n" + AppendScript;
    }

    [JsonIgnore]
    public override string TerminalDisplayName => "C# file-based app";

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // the code is read in place of a file by "-", which is put first, so arguments after "--" are still passed to the app.
        return CreateDotNetStartInfo(RunArguments switch
        {
            null or "" => "run -",
            var arguments => $"run - {arguments}",
        });
    }

    public override string? CreateStandardInput(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Wrap(command);
    }
}
