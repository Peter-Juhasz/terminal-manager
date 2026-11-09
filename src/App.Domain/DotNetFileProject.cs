using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are C# code, each run as a file-based app by <c>dotnet run -</c>, which reads it from the standard input.
/// </summary>
/// <remarks>
/// Other files in the working folder, such as launch profiles, aren't used, but it is still the folder the app is built and run in.
/// </remarks>
public sealed record class DotNetFileProject : DotNetRunProject
{
    // the prepend script is put in the same file, so what it declares, such as #: directives and using directives, applies to the command.
    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.CSharp;

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
