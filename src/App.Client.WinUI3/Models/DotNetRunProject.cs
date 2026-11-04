using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Models;

/// <summary>
/// A project whose commands are passed to an app run by <c>dotnet run</c>, as the arguments after <c>--</c>,
/// or run as the code of a file-based app for <see cref="DotNetFileProject"/>.
/// </summary>
/// <remarks>
/// The app is built again for each command, if it changed, before it is run.
/// </remarks>
public record class DotNetRunProject : LocalProject
{
    /// <summary>The arguments of <c>dotnet run</c> itself, such as the project to run, put before the command, or <see langword="null"/> when there are none.</summary>
    public string? RunArguments { get; init; }

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Arguments;

    [JsonIgnore]
    public override string TerminalDisplayName => "dotnet run";

    private static readonly Uri IconUri = new("ms-appx:///Assets/dotnet.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The command line is built by hand, so the command is split into the arguments of the app the way it was typed, quotes included.
        return CreateDotNetStartInfo(RunArguments switch
        {
            null or "" => $"run -- {command}",
            var arguments => $"run {arguments} -- {command}",
        });
    }

    /// <summary>Describes the process that runs <c>dotnet</c> with <paramref name="arguments"/>, as they are on the command line.</summary>
    private protected static ProcessStartInfo CreateDotNetStartInfo(string arguments)
    {
        // The console is switched to UTF-8 by the CLI, which the app shares, as the output is read as UTF-8.
        // These come before the environment variables of the project, so they can still be changed by them.
        return new ProcessStartInfo("dotnet")
        {
            Arguments = arguments,
            Environment =
            {
                ["DOTNET_CLI_FORCE_UTF8_ENCODING"] = "true",
                ["DOTNET_NOLOGO"] = "true",
            },
        };
    }
}
